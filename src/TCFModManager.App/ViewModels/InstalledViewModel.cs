using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

/// <summary>
/// Scans AppServices.SptEnvironment.InstallPath for what's actually installed (see InstalledModScanner),
/// then merges and paginates the results the same way BrowseViewModel does for the sp-mod.com catalog.
///
/// A fresh instance is created every time InstalledPage is navigated to, so it re-scans disk on every visit.
/// </summary>
public partial class InstalledViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // Fills the grid exactly at the 3- and 4-column width breakpoints (see UpdateLayoutForWidth).
    // Only used when nothing has been saved as this page's default - see DefaultPageSize().
    private const int DefaultPageSizeValue = 12;

    // Below this a card can't show its summary line without truncating it to uselessness. Only
    // reachable on a very narrow window, where Columns is already 1.
    private const double MinimumCardWidth = 240;

    /// <summary>Raised after RemoveAsync actually removes a mod from disk. Lets BrowseViewModel refresh its
    /// cards' install/update status dots. Static since InstalledViewModel is a fresh instance per navigation.</summary>
    public static event EventHandler? ModRemoved;

    private List<InstalledModCardViewModel> _all = [];
    private List<InstalledModCardViewModel> _filtered = [];

    //
    // Whether each view's collection still reflects _filtered, i.e. whether it needs rebuilding
    // before being shown. All three start dirty so the first fill always happens.
    //
    // Only the view on screen is ever rebuilt, so the other two go stale while they're hidden and
    // have to catch up when switched to. Without these flags that catch-up ran on every switch,
    // whether or not anything had changed: each rebuild clears the collection, which destroys every
    // realized row, and the List and Groups views are unpaginated and don't virtualize - so
    // switching to List regenerated a CardExpander per installed mod each time, for a result
    // identical to what was already there. ApplyFilter is the one place _filtered changes, so it is
    // the one place that marks all three dirty again.
    //
    private bool _cardsDirty = true;
    private bool _listDirty = true;
    private bool _sectionsDirty = true;

    // Who needs whom among the mods currently on disk, rebuilt on every scan. Backs the warning
    // shown before a disable takes something else's dependency away.
    private ModDependencyGraph _dependencies = ModDependencyGraph.Build([]);

    // Maps each raw scan entry back to the card it was merged into, so a dependency link (which is
    // between InstalledMods) can be reported and acted on as whole mods.
    private Dictionary<InstalledMod, InstalledModCardViewModel> _cardByEntry = [];

    // The moves the last disable/enable made, and what to call it - the undo payload.
    private List<ModMove> _lastMoves = [];
    private string? _lastMoveLabelKey;
    private object? _lastMoveLabelArg;

    // The current page of the Cards grid.
    public ObservableCollection<InstalledModCardViewModel> Results { get; } = [];

    // Every filtered mod, unpaginated, for the List view - which scrolls rather than pages.
    public ObservableCollection<InstalledModCardViewModel> ListItems { get; } = [];

    // Batch sizes keep each frame's worth of row building to roughly 50-80ms - see GradualFill.
    private readonly GradualFill<InstalledModCardViewModel> _cardsFill = new(batchSize: 4);

    // While a fresh page of cards is still being built - the grid stays hidden until it is whole,
    // then fades in, rather than showing cards arriving a few at a time.
    [ObservableProperty]
    private bool _isCardsFilling;
    private readonly GradualFill<InstalledModCardViewModel> _listFill = new(batchSize: 6);
    private readonly GradualFill<ModGroupSectionViewModel> _sectionsFill = new(batchSize: 2);

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    // Named filter presets (OPEN-25), the first section of the Filters panel.
    public FilterPresetList<InstalledFilterPreset> Presets { get; }

    // The Filters panel sections pinned under the search bar (OPEN-24).
    public PinnedFilterSections<InstalledFilterSection> FilterPins { get; }

    public InstalledViewModel()
    {
        _cardsFill.IsFillingChanged += (_, _) => IsCardsFilling = _cardsFill.IsFilling;

        var settings = new SettingsService().Load();
        _showListBadges = settings.ShowModListBadges;
        _defaults = settings.InstalledDefaults;

        //
        // Backing fields rather than the properties: this is the page opening at its default, not
        // someone changing a filter, so none of the OnXxxChanged handlers should run a filter pass
        // over a page that has not scanned anything yet.
        //
        _selectedUpdateFilter = DefaultUpdateFilter();
        _selectedEnabledFilter = DefaultEnabledFilter();
        _selectedSortOption = DefaultSortOption();
        _selectedGroupSortOption = DefaultGroupSortOption();
        _viewMode = DefaultViewMode();
        _pageSize = DefaultPageSize();

        // Category and group are resolved later instead: both dropdowns are rebuilt from what is
        // actually installed, and neither list exists yet - see RebuildCategoryOptions/RefreshGroups.
        _selectedGroupFilter = GroupFilterItem.All;

        // Before the subscription below, so applying a saved default doesn't count as a change.
        SavedFilterDefaults.ApplyAttributes(AttributeOptions, DefaultAttributes());
        UpdateAttributeFilterSummary();

        // Each tick box drives the same re-filter a dropdown selection does.
        foreach (var option in AttributeOptions)
        {
            option.PropertyChanged += (_, _) =>
            {
                UpdateAttributeFilterSummary();
                AutoApplyFilter();
            };
        }

        //
        // A click on an update notification opens this page on "Updates available" (§6). Taken
        // here when the click is what built the page, and from the event when it already existed.
        //
        if (AppNavigation.TakeShowUpdates()) _selectedUpdateFilter = UpdatesAvailableFilter();

        AppNavigation.ShowUpdatesRequested += (_, _) =>
        {
            if (AppNavigation.TakeShowUpdates()) SelectedUpdateFilter = UpdatesAvailableFilter();
        };

        // OPEN-13: Play's "not for this SPT" note opens this page filtered to those mods.
        if (AppNavigation.TakeShowNotForSpt()) ShowNotForSpt();

        AppNavigation.ShowNotForSptRequested += (_, _) =>
        {
            if (AppNavigation.TakeShowNotForSpt()) ShowNotForSpt();
        };

        // Diagnose logs' Show the mod: the page searched for that mod's name.
        if (AppNavigation.TakeInstalledSearch() is { } search) _searchText = search;

        AppNavigation.InstalledSearchRequested += (_, _) =>
        {
            if (AppNavigation.TakeInstalledSearch() is { } wanted) SearchText = wanted;
        };

        //
        // The update watcher patched the catalog, so the arrows on screen are out of date. Only once
        // the page has scanned - before that, its first scan reads the patched catalog anyway.
        //
        AppServices.UpdateWatcher.UpdatesFound += async (_, _) =>
        {
            if (!_hasScanned || ScanCommand.IsRunning) return;
            await ScanCommand.ExecuteAsync(null);
        };

        // A queued install or update finished (Chris, 2026-10-07): the cards were built before it.
        AppServices.DownloadQueue.ItemInstalled += async (_, _) => await RescanAfterInstallAsync();

        // sp-mod's answer on held-back updates changed (OPEN-12 F11): the cards are built with it.
        AppServices.HeldBack.Changed += async (_, _) =>
        {
            if (!_hasScanned || ScanCommand.IsRunning) return;
            await ScanCommand.ExecuteAsync(null);
        };

        // Last, so every filter the dropdown labels read is in place.
        FilterPins = new PinnedFilterSections<InstalledFilterSection>(
            "Installed", settings, x => x.InstalledPinnedFilters, (x, v) => x.InstalledPinnedFilters = v, this, PinnedLabel, PinnedShown);

        Presets = new FilterPresetList<InstalledFilterPreset>(
            "Installed", settings, x => x.InstalledFilterPresets, CapturePreset, ApplyPreset, text => StatusMessage = text);
        Presets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Presets.ActiveName)) FilterPins.RefreshLabels();
        };
        FilterPins.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FilterPins.Names)) Presets.Refresh();
        };
        Presets.Refresh();

        RebuildActiveFilters();
    }

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Expansion is driven from the cards themselves as well as from the two buttons - clicking
        // one open has to re-tint them just as pressing Expand all does.
        if (e.PropertyName is nameof(InstalledModCardViewModel.IsCardExpanded)
            or nameof(InstalledModCardViewModel.IsRowExpanded))
        {
            NotifyExpandedState();
            return;
        }

        if (e.PropertyName != nameof(InstalledModCardViewModel.IsSelected)) return;

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountLabel));
        OnPropertyChanged(nameof(AllSelected));
        DisableSelectedCommand.NotifyCanExecuteChanged();
        EnableSelectedCommand.NotifyCanExecuteChanged();
        UpdateSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }
}
