using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

/// <summary>
/// Scans AppServices.SptEnvironment.InstallPath for what's actually installed (see InstalledModScanner),
/// then merges and paginates the results the same way BrowseViewModel does for the sp-mod.com catalog.
///
/// A fresh instance is created every time InstalledPage is navigated to, so it re-scans disk on every visit.
/// </summary>
public partial class InstalledViewModel : ObservableObject
{
    // Fills the grid exactly at the 3- and 4-column width breakpoints (see UpdateLayoutForWidth).
    private const int DefaultPageSize = 12;

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
    private string? _lastMoveLabel;

    // The current page of the Cards grid.
    public ObservableCollection<InstalledModCardViewModel> Results { get; } = [];

    // Every filtered mod, unpaginated, for the List view - which scrolls rather than pages.
    public ObservableCollection<InstalledModCardViewModel> ListItems { get; } = [];

    public List<UpdateFilterItem> UpdateFilterOptions { get; } =
    [
        new("All", UpdateFilter.All),
        new("Needs update", UpdateFilter.NeedsUpdate),
        new("Up to date", UpdateFilter.UpToDate),
        new("Not found on sp-mod.com", UpdateFilter.NotFound),
    ];

    [ObservableProperty]
    private UpdateFilterItem _selectedUpdateFilter;

    public List<EnabledFilterItem> EnabledFilterOptions { get; } =
    [
        new("All", EnabledFilter.All),
        new("Enabled only", EnabledFilter.EnabledOnly),
        new("Disabled only", EnabledFilter.DisabledOnly),
    ];

    [ObservableProperty]
    private EnabledFilterItem _selectedEnabledFilter;

    // Rebuilt from the group store by RefreshGroups whenever groups change, so the dropdown always
    // lists exactly the groups that exist.
    public ObservableCollection<GroupFilterItem> GroupFilterOptions { get; } =
        [GroupFilterItem.All, GroupFilterItem.Ungrouped];

    [ObservableProperty]
    private GroupFilterItem _selectedGroupFilter;

    public List<ModSortItem> SortOptions { get; } =
    [
        new("Name (A-Z)", ModSortOption.NameAscending),
        new("Name (Z-A)", ModSortOption.NameDescending),
        new("Author (A-Z)", ModSortOption.AuthorAscending),
        new("Author (Z-A)", ModSortOption.AuthorDescending),
        new("Group (A-Z)", ModSortOption.GroupAscending),
        new("Group (Z-A)", ModSortOption.GroupDescending),
        new("Recently installed", ModSortOption.RecentlyInstalled),
    ];

    [ObservableProperty]
    private ModSortItem _selectedSortOption;

    // Set while ClearFilters is resetting several properties at once, so each individual
    // OnXxxChanged below doesn't run its own AutoApplyFilter.
    private bool _suppressAutoApplyFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    //
    // The same five tick boxes Browse carries, in one dropdown rather than three toggle switches.
    // "Has dependencies" is a stronger answer here than on Browse: it comes from the dependency
    // graph built off the scan, which reads BepInEx's own metadata, so it covers everything
    // installed rather than whatever has been looked up so far.
    //
    public ObservableCollection<ModAttributeOption> AttributeOptions { get; } =
    [
        new(ModAttributeFilter.FikaCompatible, "Fika compatible only"),
        new(ModAttributeFilter.HideAds, "Hide mods with ads"),
        new(ModAttributeFilter.HideAiContent, "Hide mods with AI content"),
        new(ModAttributeFilter.HasDependencies, "Has dependencies",
            "Only mods that require another mod you have installed."),
        new(ModAttributeFilter.HasAddons, "Has addons", "Only mods with addons published for them."),
    ];

    [ObservableProperty]
    private string _attributeFilterSummary = "Any mod";

    /// <summary>The Category dropdown's entries, rebuilt after each scan from the categories actually present in the install.</summary>
    public ObservableCollection<CategoryFilterItem> CategoryOptions { get; } = [CategoryFilterItem.All];

    [ObservableProperty]
    private CategoryFilterItem _selectedCategory = CategoryFilterItem.All;

    private bool IsOn(ModAttributeFilter filter) =>
        AttributeOptions.Any(o => o.Value == filter && o.IsSelected);

    //
    // Which of the three views the results area is showing. All three render the same filtered,
    // sorted set - Cards paginates summary cards, Groups arranges compact rows under the user's own
    // MO2-style separators, List scrolls one expandable row per mod with its full details.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCards))]
    [NotifyPropertyChangedFor(nameof(ShowGroups))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    [NotifyPropertyChangedFor(nameof(ScrollsItself))]
    private InstalledViewMode _viewMode = InstalledViewMode.Cards;

    public bool ShowCards => ViewMode == InstalledViewMode.Cards;

    public bool ShowGroups => ViewMode == InstalledViewMode.Groups;

    public bool ShowList => ViewMode == InstalledViewMode.List;

    /// <summary>True for the two views that scroll rather than paginate - what hides the pagination
    /// controls and the per-page picker.</summary>
    public bool ScrollsItself => !ShowCards;

    public ObservableCollection<ModGroupSectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    public List<GroupSortItem> GroupSortOptions { get; } =
    [
        new("Manual order", GroupSortOption.Manual),
        new("Group name (A-Z)", GroupSortOption.NameAscending),
        new("Group name (Z-A)", GroupSortOption.NameDescending),
    ];

    [ObservableProperty]
    private GroupSortItem _selectedGroupSortOption;

    /// <summary>Whether the group header's move-up/move-down buttons should show - only while
    /// groups are in their manual order; an alphabetical group sort would just override them.</summary>
    public bool CanReorderGroups => SelectedGroupSortOption.Value == GroupSortOption.Manual;

    // ON turns the flat grid's cards into a tick-list so several can be disabled/enabled at once;
    // a card click toggles its tick instead of opening the details dialog while this is on.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionModeOff))]
    private bool _selectionMode;

    /// <summary>Inverse of SelectionMode, for controls that only show while it's off.</summary>
    public bool SelectionModeOff => !SelectionMode;

    public int SelectedCount => _all.Count(m => m.IsSelected);

    public string SelectedCountLabel => SelectedCount == 1 ? "1 selected" : $"{SelectedCount} selected";

    //
    // Everything the current filters match, not just the page on screen - which is why the count is
    // in the label. "Select all" over a paginated list is ambiguous otherwise, and someone who has
    // filtered to "Update available" means all of them, not the first twelve.
    //
    public string SelectAllLabel => $"Select all {_filtered.Count}";

    public bool CanSelectAll => _filtered.Any(m => !m.IsSelected);

    /// <summary>Whether the last disable/enable can still be put back.</summary>
    public bool CanUndo => _lastMoves.Count > 0;

    public string UndoLabel => _lastMoveLabel is null ? "Undo" : $"Undo {_lastMoveLabel}";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _columns = 1;

    // Width of one card slot in the Cards WrapPanel - see UpdateLayoutForWidth for why the cards
    // are no longer in a UniformGrid.
    [ObservableProperty]
    private double _cardWidth = MinimumCardWidth;

    //
    // Whether cards and rows show which mod lists a mod belongs to. Persisted rather than reset per
    // session: it is a display preference, not a filter, which is also why ClearFilters leaves it
    // alone.
    //
    [ObservableProperty]
    private bool _showListBadges = true;

    public List<int> PageSizeOptions { get; } = [6, 9, DefaultPageSize, 15, 21, 30];

    [ObservableProperty]
    private int _pageSize = DefaultPageSize;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages = 1;

    public InstalledViewModel()
    {
        _showListBadges = new SettingsService().Load().ShowModListBadges;
        _selectedUpdateFilter = UpdateFilterOptions[0];
        _selectedEnabledFilter = EnabledFilterOptions[0];
        _selectedGroupFilter = GroupFilterItem.All;
        _selectedSortOption = SortOptions[0];
        _selectedGroupSortOption = GroupSortOptions[0];

        // Each tick box drives the same re-filter a dropdown selection does.
        foreach (var option in AttributeOptions)
        {
            option.PropertyChanged += (_, _) =>
            {
                UpdateAttributeFilterSummary();
                AutoApplyFilter();
            };
        }
    }

    partial void OnSelectedCategoryChanged(CategoryFilterItem value) => AutoApplyFilter();

    // "Any mod", the one option's own label, or a count.
    private void UpdateAttributeFilterSummary()
    {
        var selected = AttributeOptions.Where(o => o.IsSelected).ToList();

        AttributeFilterSummary = selected.Count switch
        {
            0 => "Any mod",
            1 => selected[0].Label,
            _ => $"{selected.Count} selected",
        };
    }

    //
    // Rebuilt from what is actually installed rather than from the whole catalog: filtering your
    // own mods by a category none of them are in would only ever produce an empty page.
    //
    private void RebuildCategoryOptions()
    {
        var previous = SelectedCategory;

        CategoryOptions.Clear();
        CategoryOptions.Add(CategoryFilterItem.All);

        var categories = _all
            .Select(c => c.CategoryTag)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories) CategoryOptions.Add(new CategoryFilterItem(category, category));

        // Keep the current choice if that category is still installed, rather than silently
        // resetting the page's filter under someone every time they come back to it.
        SelectedCategory = CategoryOptions.FirstOrDefault(c => c.SameAs(previous)) ?? CategoryOptions[0];
    }

    partial void OnSelectedUpdateFilterChanged(UpdateFilterItem value) => AutoApplyFilter();

    partial void OnSelectedEnabledFilterChanged(EnabledFilterItem value) => AutoApplyFilter();

    partial void OnSelectedGroupFilterChanged(GroupFilterItem value) => AutoApplyFilter();

    // Leaving selection mode drops the selection with it, so a stale tick can't be acted on later.
    partial void OnSelectionModeChanged(bool value)
    {
        if (!value) ClearSelection();
    }

    partial void OnSelectedSortOptionChanged(ModSortItem value) => AutoApplyFilter();

    partial void OnPageSizeChanged(int value) => AutoApplyFilter();

    //
    // Deliberately not AutoApplyFilter: nothing about what is shown changes, only whether each row
    // draws its chips, so the three views don't need rebuilding.
    //
    partial void OnShowListBadgesChanged(bool value)
    {
        foreach (var card in _all) card.ShowBadges = value;

        var settings = new SettingsService();
        var current = settings.Load();
        current.ShowModListBadges = value;
        settings.Save(current);
    }

    partial void OnSearchTextChanged(string value) => AutoApplyFilter();


    partial void OnViewModeChanged(InstalledViewMode value)
    {
        // Multi-select is built on the card grid, so leaving Cards turns it off rather than leaving
        // it running invisibly behind another view.
        if (value != InstalledViewMode.Cards) SelectionMode = false;

        // Deliberately not AutoApplyFilter: no filter, search or sort has changed, so re-running
        // ApplyFilter would produce the same _filtered it already holds. Refreshing the view being
        // switched to is all that's needed, and that does nothing at all when it is already current.
        // CurrentPage rather than the default 1 so switching away from Cards and back returns you to
        // the page you were on.
        RefreshActiveView(CurrentPage);
    }

    partial void OnSelectedGroupSortOptionChanged(GroupSortItem value)
    {
        OnPropertyChanged(nameof(CanReorderGroups));
        RebuildSections();
    }

    /// <summary>Re-filters/re-sorts and refreshes whichever of the three views is active whenever a
    /// filter/search/sort control changes. Suppressed while ClearFilters resets several properties
    /// at once.</summary>
    private void AutoApplyFilter()
    {
        if (_suppressAutoApplyFilter) return;
        ApplyFilter();

        // CurrentPage, not 1. Narrowing a filter shrinks TotalPages and GoToPage clamps to it, so a
        // page that no longer exists still lands somewhere sensible - and nothing is left that can
        // silently put you back on page 1. Dropping the resetPage flag was not enough on its own:
        // this method runs whenever any filter/sort/search/page-size property changes, including
        // when one is reassigned in code, and its RefreshActiveView() was taking the default of 1.
        RefreshActiveView(CurrentPage);
    }

    /// <summary>Refills whichever results collection the current view reads from, if it has fallen behind
    /// _filtered. Only the active one is rebuilt; switching views rebuilds the one being switched to, and
    /// only if something has actually changed since that view last drew.</summary>
    // No default for `page`: every caller has to say which page it means. A default of 1 sitting
    // here is what let a caller reset the page without looking like it was doing anything.
    private void RefreshActiveView(int page)
    {
        switch (ViewMode)
        {
            case InstalledViewMode.Groups:
                if (_sectionsDirty) RebuildSections();
                break;
            case InstalledViewMode.List:
                if (_listDirty) RebuildList();
                break;
            default:
                GoToPage(page);
                break;
        }
    }

    private void RebuildList()
    {
        // Synced rather than cleared, for the same reason the card grid is - see Sync.
        ItemsSync.Apply(ListItems, _filtered);
        _listDirty = false;
    }

    /// <summary>Resets every Installed filter/search control back to its opening default, then re-applies once immediately.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        _suppressAutoApplyFilter = true;
        try
        {
            SearchText = string.Empty;
            SelectedUpdateFilter = UpdateFilterOptions[0];
            SelectedEnabledFilter = EnabledFilterOptions[0];
            SelectedGroupFilter = GroupFilterOptions[0];
            SelectedSortOption = SortOptions[0];
            SelectedCategory = CategoryOptions[0];
            foreach (var option in AttributeOptions) option.IsSelected = false;
            UpdateAttributeFilterSummary();
            PageSize = DefaultPageSize;
        }
        finally
        {
            _suppressAutoApplyFilter = false;
        }

        ApplyFilter();
        RefreshActiveView(CurrentPage);
    }

    //
    // Tags each card with the mod lists that name it. Worked out through ModListMembership, which
    // runs the real planner, so a badge can never disagree with what applying that list would do.
    //
    // Runs per scan rather than being watched: the Installed page rescans when it is navigated to,
    // so editing a list on the Mod lists page and coming back here shows the change.
    //
    private void ApplyListMembership(IReadOnlyList<InstalledModCardViewModel> cards)
    {
        var lists = AppServices.ModLists.Load().Lists;
        if (lists.Count == 0)
        {
            foreach (var card in cards) card.Lists = [];
            return;
        }

        var names = ModListMembership.Names(lists, ModListCandidates.From(cards));

        for (var i = 0; i < cards.Count; i++) cards[i].Lists = names[i];
    }

    // A fresh scan builds new cards, which default to showing badges - they have to be told.
    private void ApplyBadgeVisibility()
    {
        foreach (var card in _all) card.ShowBadges = ShowListBadges;
    }

    public void UpdateLayoutForWidth(double availableWidth)
    {
        //
        // Ignore a width of zero rather than treating it as "very narrow".
        //
        // Collapsing an element makes WPF raise SizeChanged with 0 x 0, so switching to Groups or
        // List view - which collapses the card grid - reported no width and dropped Columns to 1.
        // Coming back to Cards then showed every card full width, one per row, looking like a list,
        // and it stayed that way until the window was resized and a real width arrived. The last
        // real width is still the right answer for a grid nobody can currently see.
        //
        // Also covers the first Loaded call, which can run before the list has been arranged.
        //
        if (availableWidth <= 0) return;

        Columns = availableWidth switch
        {
            < 700 => 1,
            < 1050 => 2,
            < 1400 => 3,
            _ => 4,
        };

        //
        // The cards sit in a WrapPanel now rather than a UniformGrid, because a UniformGrid forces
        // every cell to the same size - opening one card would have made every card on the page
        // that tall. A WrapPanel lets each card keep its own height, which is the whole point of
        // making them expandable, and ItemWidth is what keeps the columns lining up.
        //
        // Floored, and a pixel taken off first: ItemWidth * Columns landing even a fraction over
        // the available width wraps a card onto the next row, so a four-column layout would
        // silently become three.
        //
        CardWidth = Math.Max(MinimumCardWidth, Math.Floor((availableWidth - 1) / Columns));
    }

    //
    // Re-reads the install and rebuilds every card. Deliberately leaves you on the page you were
    // on: a scan changes what the list holds, not which part of it you were looking at.
    //
    // This used to take a resetPage flag, true for the Rescan button and the page's Loaded handler
    // and false for everything else. That is what sent you back to page 1 on closing the mod
    // dialog - Loaded fires again every time the page is re-attached, not just the first time, and
    // any scan that went through the flag-true path clobbered the page. Resetting to page 1
    // belongs to a *filter* change, where the list you are paging through is genuinely different,
    // and AutoApplyFilter already does exactly that.
    //
    [RelayCommand]
    private async Task ScanAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _all = [];
            ApplyFilter();
            RefreshActiveView(CurrentPage);
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        IsBusy = true;
        try
        {
            // Reuses whatever's already cached; triggers the one-time catalog fetch if Browse
            // hasn't been visited yet this session.
            await AppServices.ModCache.EnsureLoadedAsync();
            await AppServices.Addons.EnsureLoadedAsync();

            // What this app itself installed, and which folders it placed - identifies those mods
            // exactly instead of inferring them from folder names.
            var installRecords = AppServices.InstallManifest.Load().Mods;

            // Read once here rather than inside the background work, so a catalog refresh landing
            // mid-scan can't swap the list out from under it.
            var catalog = AppServices.ModCache.AllMods;
            var addons = AppServices.Addons.AllAddons;
            var sptVersion = AppServices.SptEnvironment.InstalledVersion;

            // The whole scan-and-match pass runs off the UI thread. Matching a large install
            // against a full catalog is the slower half of the two, and doing it inline is what
            // made navigating to this page hang.
            var (scanned, cards, dependencies) = await Task.Run(() =>
            {
                var found = InstalledModScanner.Scan(installPath);

                var built = InstalledModCardViewModel.BuildFrom(found, catalog, sptVersion, installRecords, addons)
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Built off the same scan the cards came from, so every link points at an entry
                // some card owns.
                return (found, built, ModDependencyGraph.Build(found));
            });

            // Which cards were open, keyed the same way group assignments are, so the set survives
            // every card object being replaced. Captured before _all is reassigned.
            var expanded = _all
                .Where(m => m.IsExpanded)
                .Select(m => ModGroupStore.KeyFor(m.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _all = cards;

            if (expanded.Count > 0)
                foreach (var card in cards)
                    card.IsExpanded = expanded.Contains(ModGroupStore.KeyFor(card.Name));

            ApplyListMembership(cards);
            ApplyBadgeVisibility();
            _dependencies = dependencies;

            // Set before the cards are subscribed to below, so filling it in doesn't read as a
            // card changing under the page.
            foreach (var card in _all)
                card.HasDependencies = card.Entries.Any(e => dependencies.DependenciesOf(e).Count > 0);

            RebuildCategoryOptions();

            _cardByEntry = [];
            foreach (var card in _all)
            {
                card.PropertyChanged += OnCardPropertyChanged;
                foreach (var entry in card.Entries) _cardByEntry[entry] = card;
            }

            RefreshGroups();

            // A rescan replaces every card, so any previous selection is gone with them.
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedCountLabel));
            DisableSelectedCommand.NotifyCanExecuteChanged();
            EnableSelectedCommand.NotifyCanExecuteChanged();
            UpdateSelectedCommand.NotifyCanExecuteChanged();
            SelectAllCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SelectAllLabel));

            var unmatched = _all.Where(m => m.ModId is null).Select(m => m.Name).ToList();
            AppLog.Info("Installed",
                $"scanned {scanned.Count} folder(s) -> {_all.Count} card(s); " +
                $"{_all.Count(m => m.IsAppManaged)} app-managed, {_all.Count(m => m.IsDisabled)} disabled, " +
                $"{unmatched.Count} unmatched; on page {CurrentPage} of {TotalPages}");
            if (unmatched.Count > 0) AppLog.Debug("Installed", $"unmatched: {string.Join(", ", unmatched)}");

            var mixed = _all.Where(m => m.IsMixedState).Select(m => m.Name).ToList();
            if (mixed.Count > 0)
                AppLog.Warn("Installed", $"present in both an enabled and a disabled folder: {string.Join(", ", mixed)}");

            ApplyFilter();
            RefreshActiveView(CurrentPage);

            StatusMessage = _all.Count == 0
                ? $"No mods found under \"{installPath}\"."
                : DescribeCounts();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Removes a mod from the install: the manifest-precise ModInstallService.UninstallAsync path
    /// for anything IsAppManaged, or RemoveLegacyPath (delete the mod's whole folder) otherwise. Both paths
    /// confirm first.</summary>
    [RelayCommand]
    private async Task RemoveAsync(InstalledModCardViewModel? mod)
    {
        if (mod is null) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        // A disabled mod's install record still points at the folders it was installed into, which
        // it no longer occupies - so removal would delete nothing and report success. Enabling it
        // first puts those paths back where the record expects them.
        if (mod.IsDisabled)
        {
            StatusMessage = $"{mod.DisplayTitle} is disabled - enable it before removing it.";
            return;
        }

        if (mod.IsAppManaged && mod.ModId is { } modId)
        {
            var manifest = AppServices.InstallManifest.Load();
            var record = manifest.Mods.FirstOrDefault(m => m.ModId == modId);
            if (record is null)
            {
                StatusMessage = $"Couldn't find an install record for {mod.Name} - it may have already been removed.";
                return;
            }

            var configs = ModConfigFiles.InRecord(record);
            if (ConfirmRemoval(mod.Name, "This deletes exactly the files this app installed for it.", configs.Count)
                is not { } configAction)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var result = await AppServices.ModInstall.UninstallAsync(installPath, record, configAction);
                StatusMessage = DescribeRemoval(mod.Name, result.FailedFiles.Count, result.ConfigsKept, result.ConfigsFolder);
                ModRemoved?.Invoke(this, EventArgs.Empty);
            }
            catch (InvalidOperationException ex)
            {
                // SPT or its server is running - ModInstallService's message names what to close.
                StatusMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
        else
        {
            var paths = new[] { mod.ClientFolderPath, mod.ServerFolderPath }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (paths.Count == 0)
            {
                StatusMessage = $"Couldn't find {mod.Name}'s folder to remove.";
                return;
            }

            var configs = ModInstallService.FindLegacyConfigs(installPath, paths!);
            if (ConfirmRemoval(
                    mod.Name,
                    "This mod wasn't installed through this app, so this permanently deletes its whole folder rather than " +
                    $"just the files it placed:\n\n{string.Join("\n", paths)}",
                    configs.Count) is not { } configAction)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var kept = configAction == ConfigAction.Keep && configs.Count > 0
                    ? ModInstallService.KeepLegacyConfigs(installPath, configs, mod.Name)
                    : new KeptConfigs(0, null);

                foreach (var path in paths) ModInstallService.RemoveLegacyPath(path!);

                // A manually-confirmed version record would otherwise dangle, pointing at a mod
                // that's no longer on disk.
                if (mod.IsManualOverride && mod.ModId is { } overriddenModId)
                    AppServices.InstallManifest.ClearManualVersion(overriddenModId);

                StatusMessage = DescribeRemoval(mod.Name, failedFiles: 0, kept.Count, kept.Folder);
                ModRemoved?.Invoke(this, EventArgs.Empty);
            }
            catch (InvalidOperationException ex)
            {
                StatusMessage = ex.Message;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusMessage = $"Couldn't remove {mod.Name}: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        await ScanAsync();
    }

    /// <summary>Opens the mod details/update dialog for whichever card was clicked. Routed through
    /// AppServices.ModUpdateOverlay since only MainWindow owns the dialog presenter. Always rescans once
    /// the dialog closes, staying on the current page.</summary>
    [RelayCommand]
    private async Task ShowDetailsAsync(InstalledModCardViewModel? mod)
    {
        if (mod is null) return;
        if (AppServices.ModUpdateOverlay.ShowAsync is not { } show) return;

        // Only when the dialog actually did something. A rescan re-reads the whole install and
        // replaces every card, which visibly rebuilds the page - not something opening a mod to
        // read its changelog should cause.
        if (await show(mod)) await ScanAsync();
    }

    //
    // Opens a mod's folder in Explorer. The path comes from the card rather than being rebuilt
    // here, so it is the folder the scan actually found - including the disabled location, if the
    // mod is currently disabled.
    //
    [RelayCommand]
    private void OpenModFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        // Checked rather than assumed: a folder removed or renamed outside the app since the last
        // scan would otherwise open an Explorer error the user has to dismiss.
        if (!Directory.Exists(path))
        {
            StatusMessage = $"That folder is no longer there - {path}. Rescan to pick up the change.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Installed", $"couldn't open {path}: {ex.Message}");
            StatusMessage = "Couldn't open that folder.";
        }
    }

    /// <summary>Flips one mod between enabled and disabled.</summary>
    [RelayCommand]
    private Task ToggleDisableAsync(InstalledModCardViewModel? mod) =>
        mod is null ? Task.CompletedTask : ApplyDisableAsync([mod], !mod.IsDisabled);

    [RelayCommand]
    private Task DisableGroupAsync(ModGroupSectionViewModel? section) =>
        section is null ? Task.CompletedTask : ApplyDisableAsync(section.Items.ToList(), disable: true);

    [RelayCommand]
    private Task EnableGroupAsync(ModGroupSectionViewModel? section) =>
        section is null ? Task.CompletedTask : ApplyDisableAsync(section.Items.ToList(), disable: false);

    /// <summary>Disables everything currently enabled in a group and enables everything currently
    /// disabled in it, as one undoable step.</summary>
    [RelayCommand]
    private async Task InvertGroupAsync(ModGroupSectionViewModel? section)
    {
        if (section is null) return;

        var toDisable = section.Items.Where(m => !m.IsDisabled).ToList();
        var toEnable = section.Items.Where(m => m.IsDisabled).ToList();

        // The disable half is the one that can break other mods, so it asks first; the enable half
        // then runs without a second prompt and its moves are merged into the same undo step.
        var moves = await ApplyDisableAsync(toDisable, disable: true, label: $"inverting {section.Name}");
        if (moves is null) return;

        await ApplyDisableAsync(toEnable, disable: false, label: $"inverting {section.Name}", confirm: false, carryOver: moves);
    }

    //
    // Settles a mod found in both a container and its ".disabled" sibling. The user picks which
    // copy to keep; the other is moved into a hidden folder in the install rather than deleted, so
    // a wrong answer costs nothing but a drag back.
    //
    [RelayCommand]
    private async Task ResolveDuplicateAsync(InstalledModCardViewModel? mod)
    {
        if (mod is not { HasDuplicateFolders: true }) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        if (ModInstallService.RunningBlockers() is { Count: > 0 } blockers)
        {
            StatusMessage =
                $"Close {string.Join(" and ", blockers)} first - files inside the SPT install are locked while it's running.";
            return;
        }

        var pairs = mod.DuplicateFolders;
        var folders = string.Join("\n", pairs.SelectMany(p => new[] { p.Enabled.FolderPath, p.Disabled.FolderPath }));

        var answer = MessageBox.Show(
            $"{mod.DisplayTitle} is in both an enabled and a disabled folder:\n\n{folders}\n\n" +
            "Yes  -  keep the enabled copy\n" +
            "No  -  keep the disabled copy, and enable it\n" +
            "Cancel  -  leave it as it is\n\n" +
            "The copy you don't keep is moved into a hidden .tcfmm-duplicates folder in your SPT install, not deleted.",
            $"Sort out {mod.DisplayTitle}?",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (answer is not (MessageBoxResult.Yes or MessageBoxResult.No)) return;

        var keepEnabled = answer == MessageBoxResult.Yes;
        var moves = new List<ModMove>();
        var failed = new List<ModDisableFailure>();

        IsBusy = true;
        try
        {
            var timestamp = DateTimeOffset.UtcNow;
            foreach (var pair in pairs)
            {
                var outcome = ModDisableService.ResolveDuplicate(installPath, pair, keepEnabled, timestamp);
                moves.AddRange(outcome.Moved);
                failed.AddRange(outcome.Failed);
            }
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        SetLastMoves(moves, $"sorting out {mod.DisplayTitle}");

        var message = $"Sorted out {mod.DisplayTitle} - kept the {(keepEnabled ? "enabled" : "disabled")} copy, " +
            "the other is in .tcfmm-duplicates in your SPT install.";
        if (failed.Count > 0) message = $"{message} {DescribeFailures(failed)}";

        await ScanAsync();
        StatusMessage = message;
    }

    private bool HasSelection() => SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DisableSelectedAsync() => ApplyDisableAsync(SelectedCards(), disable: true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task EnableSelectedAsync() => ApplyDisableAsync(SelectedCards(), disable: false);

    //
    // True when at least one selected mod could actually be updated. Deliberately stricter than
    // HasSelection: offering an enabled Update button that then reports "nothing to update" is
    // worse than a greyed-out one, and the reasons a mod is excluded are explained when it runs.
    //
    private bool HasUpdatableSelection() => SelectedCards().Any(IsUpdatable);

    private static bool IsUpdatable(InstalledModCardViewModel card) =>
        card is { UpdateAvailable: true, IsDisabled: false, IsAddon: false, ModId: not null }
        && card.LatestPublishedVersion is not null;

    //
    // Updates every selected mod that has one waiting, in a single pass.
    //
    // The prompts are asked ONCE for the whole batch rather than once per mod - the same reasoning
    // as applying a mod list: per-mod modals over a long selection train people into turning the
    // gate off globally, which is strictly worse for them than one demanding prompt.
    //
    [RelayCommand(CanExecute = nameof(HasUpdatableSelection))]
    private void UpdateSelected()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        var selected = SelectedCards();
        var targets = selected.Where(IsUpdatable).ToList();

        if (targets.Count == 0)
        {
            StatusMessage = DescribeNothingToUpdate(selected);
            return;
        }

        // Resolved against the cached catalog before anything is asked, so a mod the catalog can no
        // longer identify is reported now rather than failing halfway through the batch.
        var resolved = new List<(InstalledModCardViewModel Card, Mod Mod, string Version)>();
        var unmatched = new List<string>();

        foreach (var card in targets)
        {
            var match = AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == card.ModId);
            var version = match is null
                ? null
                : ModCardViewModel.PickDisplayVersion(match, AppServices.SptEnvironment.InstalledVersion)?.Version;

            if (match is null || version is null) unmatched.Add(card.DisplayTitle);
            else resolved.Add((card, match, version));
        }

        if (resolved.Count == 0)
        {
            StatusMessage = $"Couldn't find {Join(unmatched)} in the cached catalog - try Rescan.";
            return;
        }

        //
        // One warning covering every hand-installed mod in the batch, for the same reason the
        // single-mod path warns at all: there is no record of which files the current version
        // placed, so the new one goes on top of it.
        //
        var handInstalled = resolved.Where(r => !r.Card.IsAppManaged).Select(r => r.Card.DisplayTitle).ToList();
        if (handInstalled.Count > 0 && !Confirm(
                handInstalled.Count == 1 ? "Update a hand-installed mod?" : $"Update {handInstalled.Count} hand-installed mods?",
                $"{Join(handInstalled)} wasn't installed through this app, so there's no record of exactly which "
                + "files the current version placed. Updating installs the new version's files on top of what's "
                + "already there rather than cleanly removing the old version first, so you may end up with "
                + "leftover files from the old version."))
        {
            StatusMessage = "Update cancelled.";
            return;
        }

        // The same gate a single install goes through, asked once for the batch. ConfirmAll honours
        // the Options switch that turns the gate off.
        var links = resolved.Select(r => new ModPageLink(r.Card.DisplayTitle, r.Mod.DetailUrl)).ToList();
        if (!ReadModPageConfirmationWindow.ConfirmAll(links))
        {
            StatusMessage = "Update cancelled - the mod pages weren't confirmed as read.";
            return;
        }

        foreach (var (_, mod, version) in resolved)
        {
            AppServices.DownloadQueue.Enqueue(
                InstallTarget.For(mod), version, installPath, () => ResolveVersionLinkAsync(mod, version));
        }

        var queued = resolved.Count == 1
            ? $"Queued 1 update"
            : $"Queued {resolved.Count} updates";
        var skipped = DescribeSkipped(selected, resolved.Count, unmatched);
        StatusMessage = $"{queued} - see the Downloads page for progress.{skipped}";

        AppLog.Info("Installed", $"queued {resolved.Count} update(s) from a selection of {selected.Count}");
    }

    //
    // Why a selection that looked updatable produced nothing. Each reason is separate because they
    // need different things done about them - enabling a mod, opening its parent, or a rescan.
    //
    private static string DescribeNothingToUpdate(IReadOnlyList<InstalledModCardViewModel> selected)
    {
        if (selected.Count == 0) return "Nothing selected.";

        var disabled = selected.Count(c => c is { UpdateAvailable: true, IsDisabled: true });
        var addons = selected.Count(c => c is { UpdateAvailable: true, IsAddon: true });

        var reasons = new List<string>();
        if (disabled > 0) reasons.Add($"{disabled} disabled (enable them first)");
        if (addons > 0) reasons.Add($"{addons} addon(s), which update from their parent mod");

        return reasons.Count == 0
            ? "None of the selected mods have an update."
            : $"None of the selected mods can be updated here: {string.Join(", ", reasons)}.";
    }

    private static string DescribeSkipped(
        IReadOnlyList<InstalledModCardViewModel> selected,
        int queued,
        IReadOnlyList<string> unmatched)
    {
        var parts = new List<string>();

        var disabled = selected.Count(c => c is { UpdateAvailable: true, IsDisabled: true });
        var addons = selected.Count(c => c is { UpdateAvailable: true, IsAddon: true });
        var noUpdate = selected.Count(c => c.UpdateAvailable != true);

        if (noUpdate > 0) parts.Add($"{noUpdate} already up to date");
        if (disabled > 0) parts.Add($"{disabled} disabled");
        if (addons > 0) parts.Add($"{addons} addon(s) - update those from their parent mod");
        if (unmatched.Count > 0) parts.Add($"{unmatched.Count} not found in the catalog");

        return parts.Count == 0 ? string.Empty : $" Skipped {string.Join(", ", parts)}.";
    }

    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

    // Twin of BrowseViewModel's resolver. Duplicated rather than shared: pulling it out would mean
    // editing a file this change otherwise doesn't touch, for four lines.
    private static async Task<ModVersion?> ResolveVersionLinkAsync(Mod mod, string version)
    {
        var versions = await AppServices.SpModApi.GetModVersionsAsync(
            mod.Id.ToString(), new ModVersionsQuery { FilterVersion = version, PerPage = 5 });
        return versions.Data.FirstOrDefault(v => v.Version == version) ?? versions.Data.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanSelectAll))]
    private void SelectAll()
    {
        foreach (var mod in _filtered) mod.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var mod in _all) mod.IsSelected = false;
    }

    /// <summary>Puts the last disable/enable back. Fails softly per mod if anything moved on disk since.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_lastMoves.Count == 0) return;

        IsBusy = true;
        string message;
        try
        {
            var outcome = ModDisableService.Revert(_lastMoves);
            message = outcome.Failed.Count == 0
                ? $"Put {outcome.Moved.Count} mod(s) back."
                : $"Put {outcome.Moved.Count} mod(s) back; {DescribeFailures(outcome.Failed)}";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        SetLastMoves([], null);
        await ScanAsync();
        StatusMessage = message;
    }

    private List<InstalledModCardViewModel> SelectedCards() => _all.Where(m => m.IsSelected).ToList();

    //
    // The one path every disable/enable goes through: works out what else the change reaches, asks
    // about it, moves the folders, then rescans. Returns the moves it made so a caller running two
    // passes (InvertGroupAsync) can merge them into one undo step, or null when nothing happened.
    //
    private async Task<List<ModMove>?> ApplyDisableAsync(
        IReadOnlyList<InstalledModCardViewModel> cards,
        bool disable,
        string? label = null,
        bool confirm = true,
        List<ModMove>? carryOver = null)
    {
        var verb = disable ? "disable" : "enable";

        var targets = cards.Where(c => c.IsDisabled != disable).ToList();
        if (targets.Count == 0)
        {
            if (carryOver is null) StatusMessage = $"Nothing to {verb}.";
            return carryOver ?? [];
        }

        // Checked before anything is asked or moved, so a locked install is reported up front
        // rather than after the user has answered a dialog. ModDisableService guards again itself.
        if (ModInstallService.RunningBlockers() is { Count: > 0 } blockers)
        {
            StatusMessage =
                $"Close {string.Join(" and ", blockers)} before {verb.TrimEnd('e')}ing a mod - " +
                "files inside the SPT install are locked while it's running.";
            return null;
        }

        var affected = AffectedCards(targets, disable);

        if (confirm && affected.Count > 0)
        {
            var rows = affected
                .Select(a => new ModDisableImpactRow(a.Card.DisplayTitle, a.Detail, a.IsSoft))
                .ToList();

            switch (ModDisableConfirmationWindow.Confirm(disable, targets.Select(t => t.DisplayTitle).ToList(), rows))
            {
                case ModDisableChoice.Cancel:
                    return null;
                case ModDisableChoice.ProceedWithCascade:
                    targets = targets.Concat(affected.Select(a => a.Card)).Distinct().ToList();
                    break;
            }
        }

        var entries = targets.SelectMany(c => c.Entries).Where(e => e.IsDisabled != disable).ToList();

        IsBusy = true;
        ModDisableOutcome outcome;
        try
        {
            outcome = ModDisableService.Apply(entries, disable);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            return null;
        }
        finally
        {
            IsBusy = false;
        }

        var moves = (carryOver ?? []).Concat(outcome.Moved).ToList();
        SetLastMoves(moves, label ?? DescribeTargets(targets, disable));

        var message = $"{(disable ? "Disabled" : "Enabled")} {targets.Count} mod(s).";
        if (outcome.Failed.Count > 0) message = $"{message} {DescribeFailures(outcome.Failed)}";

        await ScanAsync();
        StatusMessage = message;

        return moves;
    }

    //
    // What else this change reaches: the mods that would lose a dependency when disabling, or the
    // disabled dependencies still needed when enabling. Reported as whole cards rather than
    // individual scan entries, so a client+server mod is never half-moved.
    //
    private List<(InstalledModCardViewModel Card, string Detail, bool IsSoft)> AffectedCards(
        IReadOnlyList<InstalledModCardViewModel> targets, bool disable)
    {
        var roots = targets.SelectMany(c => c.Entries).ToList();
        var links = disable ? _dependencies.DisableImpact(roots) : _dependencies.EnableRequirements(roots);

        var results = new List<(InstalledModCardViewModel, string, bool)>();
        var seen = new HashSet<InstalledModCardViewModel>(targets);

        foreach (var link in links)
        {
            var reached = disable ? link.Dependent : link.Dependency;
            if (!_cardByEntry.TryGetValue(reached, out var card) || !seen.Add(card)) continue;

            var otherName = _cardByEntry.TryGetValue(disable ? link.Dependency : link.Dependent, out var other)
                ? other.DisplayTitle
                : (disable ? link.Dependency : link.Dependent).Name;

            var detail = disable
                ? link.IsSoft ? $"optionally uses {otherName}" : $"needs {otherName}"
                : link.IsSoft ? $"optionally used by {otherName}" : $"needed by {otherName}";

            results.Add((card, detail, link.IsSoft));
        }

        return results;
    }

    private void SetLastMoves(List<ModMove> moves, string? label)
    {
        _lastMoves = moves;
        _lastMoveLabel = label;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
        UndoCommand.NotifyCanExecuteChanged();
    }

    private static string DescribeTargets(IReadOnlyList<InstalledModCardViewModel> targets, bool disable)
    {
        var what = targets.Count == 1 ? targets[0].DisplayTitle : $"{targets.Count} mods";
        return $"{(disable ? "disabling" : "enabling")} {what}";
    }

    private static string DescribeFailures(IReadOnlyList<ModDisableFailure> failures) =>
        failures.Count == 1
            ? $"{failures[0].ModName} couldn't be moved: {failures[0].Reason}"
            : $"{failures.Count} couldn't be moved: {string.Join("; ", failures.Select(f => $"{f.ModName} - {f.Reason}"))}";

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InstalledModCardViewModel.IsSelected)) return;

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountLabel));
        DisableSelectedCommand.NotifyCanExecuteChanged();
        EnableSelectedCommand.NotifyCanExecuteChanged();
        UpdateSelectedCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
    }

    private static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <summary>Confirms a removal and, when the mod has config files of its own, asks what should happen to
    /// them in the same prompt. Returns null when the user backed out.</summary>
    private static ConfigAction? ConfirmRemoval(string modName, string message, int configCount)
    {
        // Most people reaching for Remove are troubleshooting, where disabling does the job without
        // deleting anything - worth saying at the point they're about to delete.
        message += "\n\nTo take it out of the game without deleting it, use Disable instead.";

        if (configCount == 0)
            return Confirm($"Remove {modName}?", message) ? ConfigAction.Keep : null;

        var answer = MessageBox.Show(
            $"{message}\n\n" +
            $"{modName} has {configCount} config file(s) of its own:\n\n" +
            $"Yes  -  keep them, moved to {AppPaths.LegacyConfigsDirectory}\n" +
            "No  -  delete them along with the rest of the mod\n" +
            "Cancel  -  don't remove anything",
            $"Remove {modName}?",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return answer switch
        {
            MessageBoxResult.Yes => ConfigAction.Keep,
            MessageBoxResult.No => ConfigAction.Delete,
            _ => null,
        };
    }

    private static string DescribeRemoval(string modName, int failedFiles, int configsKept, string? configsFolder)
    {
        var message = failedFiles == 0
            ? $"Removed {modName}."
            : $"Removed {modName}, but {failedFiles} file(s) couldn't be deleted (locked or already gone) - you may need to remove them by hand.";

        return configsKept > 0 && configsFolder is not null
            ? $"{message} {configsKept} config file(s) kept in {configsFolder}."
            : message;
    }

    private bool CanGoToPreviousPage() => CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void PreviousPage() => GoToPage(CurrentPage - 1);

    private bool CanGoToNextPage() => CurrentPage < TotalPages;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void NextPage() => GoToPage(CurrentPage + 1);

    private void GoToPage(int page)
    {
        var target = Math.Clamp(page, 1, TotalPages);

        // Unlike the other two views this one also depends on which page is asked for, so being
        // clean isn't enough on its own - paging forward and back has to rebuild even though
        // _filtered hasn't moved. TotalPages only changes inside ApplyFilter, which marks this
        // dirty, so a clamp landing somewhere new can't be missed here.
        if (!_cardsDirty && target == CurrentPage) return;

        CurrentPage = target;
        ItemsSync.Apply(Results, _filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList());

        _cardsDirty = false;
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();

        // A leading "@" switches the search text to matching against the matched catalog listing's
        // author instead of name - e.g. "@Acidphantasm".
        var authorQuery = query.StartsWith('@') ? query[1..].Trim() : null;

        var matched = _all
            .Where(m => SelectedUpdateFilter.Value switch
            {
                UpdateFilter.NeedsUpdate => m.UpdateAvailable == true,
                UpdateFilter.UpToDate => m.UpdateAvailable == false,
                UpdateFilter.NotFound => m.MatchedModName is null,
                _ => true, // All - no restriction
            })
            .Where(m => SelectedEnabledFilter.Value switch
            {
                EnabledFilter.EnabledOnly => !m.IsDisabled,
                EnabledFilter.DisabledOnly => m.IsDisabled,
                _ => true,
            })
            .Where(m => SelectedGroupFilter.AllGroups
                || (SelectedGroupFilter.GroupId is { } groupId ? m.GroupId == groupId : !m.IsGrouped))
            .Where(m => authorQuery is not null
                ? MatchesAuthor(m, authorQuery)
                : query.Length == 0 || Matches(m.DisplayTitle, query) || Matches(m.Name, query))
            .Where(m => SelectedCategory.Title is not { } category
                || string.Equals(m.CategoryTag, category, StringComparison.OrdinalIgnoreCase))
            .Where(m => !IsOn(ModAttributeFilter.FikaCompatible) || m.IsFikaCompatible)
            .Where(m => !IsOn(ModAttributeFilter.HideAds) || !m.ContainsAds)
            .Where(m => !IsOn(ModAttributeFilter.HideAiContent) || !m.ContainsAiContent)
            .Where(m => !IsOn(ModAttributeFilter.HasDependencies) || m.HasDependencies)
            .Where(m => !IsOn(ModAttributeFilter.HasAddons) || m.HasAddons);

        _filtered = SortMods(matched, SelectedSortOption.Value).ToList();

        // Every view now disagrees with _filtered, including the two that aren't on screen - they
        // catch up when switched to.
        _cardsDirty = _listDirty = _sectionsDirty = true;

        TotalPages = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));

        // The button names the number it would select, so narrowing the filter has to re-label it.
        OnPropertyChanged(nameof(SelectAllLabel));
        SelectAllCommand.NotifyCanExecuteChanged();

        // Keeps the status line up to date as soon as a filter/search control changes; skipped when
        // _all is empty since ScanAsync's own "No mods found under ..." message is more useful there.
        if (_all.Count > 0) StatusMessage = DescribeCounts();
    }

    // How many mods are shown out of how many are installed, plus how many of them are disabled.
    private string DescribeCounts()
    {
        var shown = _filtered.Count == _all.Count
            ? $"{_all.Count} mod(s) found."
            : $"{_filtered.Count} of {_all.Count} mod(s) shown.";

        var disabled = _all.Count(m => m.IsDisabled);
        return disabled == 0 ? shown : $"{shown} {disabled} disabled.";
    }

    private static IEnumerable<InstalledModCardViewModel> SortMods(IEnumerable<InstalledModCardViewModel> mods, ModSortOption sort) =>
        sort switch
        {
            ModSortOption.NameAscending => mods.OrderBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.NameDescending => mods.OrderByDescending(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            // No-author mods sort last in both directions rather than clumping at whichever end an
            // empty/null string would fall on.
            ModSortOption.AuthorAscending => mods
                .OrderBy(m => m.Author is null)
                .ThenBy(m => m.Author, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.AuthorDescending => mods
                .OrderBy(m => m.Author is null)
                .ThenByDescending(m => m.Author, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            // Ungrouped mods sort last in both directions, same treatment as a missing author.
            ModSortOption.GroupAscending => mods
                .OrderBy(m => m.GroupName is null)
                .ThenBy(m => m.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.GroupDescending => mods
                .OrderBy(m => m.GroupName is null)
                .ThenByDescending(m => m.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.RecentlyInstalled => mods
                .OrderByDescending(m => m.InstalledAt ?? DateTimeOffset.MinValue)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            _ => mods,
        };

    //
    // Copies each mod's group assignment onto its card and rebuilds the Group dropdown, from the
    // one place groups are stored. Run after every scan and every group change, so the cards, the
    // filter and the store can't drift apart. The current filter selection is carried across by
    // value rather than by instance, since the dropdown's items are replaced each time.
    //
    private void RefreshGroups()
    {
        var data = AppServices.ModGroups.Load();
        var namesById = data.Groups.ToDictionary(g => g.Id, g => g.Name);

        foreach (var card in _all)
        {
            // An assignment pointing at a group that's since been deleted reads as ungrouped, the
            // same way RebuildSections already treats it.
            Guid? assigned = data.Assignments.TryGetValue(ModGroupStore.KeyFor(card.Name), out var id)
                && namesById.ContainsKey(id)
                ? id
                : null;

            card.GroupId = assigned;
            card.GroupName = assigned is { } value ? namesById[value] : null;
        }

        var previous = SelectedGroupFilter;

        _suppressAutoApplyFilter = true;
        try
        {
            GroupFilterOptions.Clear();
            GroupFilterOptions.Add(GroupFilterItem.All);
            GroupFilterOptions.Add(GroupFilterItem.Ungrouped);

            foreach (var group in data.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
                GroupFilterOptions.Add(new GroupFilterItem(group.Name, group.Id, AllGroups: false));

            SelectedGroupFilter = GroupFilterOptions.FirstOrDefault(o => o.SameAs(previous)) ?? GroupFilterItem.All;
        }
        finally
        {
            _suppressAutoApplyFilter = false;
        }
    }

    // Everything that has to happen after groups are added, renamed, deleted, reordered, or a mod
    // is moved between them.
    private void GroupsChanged()
    {
        RefreshGroups();
        ApplyFilter();
        RefreshActiveView(CurrentPage);
    }

    private static bool Matches(string? haystack, string needle) =>
        haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>Matches a "@name" query against the matched catalog listing's author. An empty query matches
    /// everything, same as an empty plain-text query.</summary>
    private static bool MatchesAuthor(InstalledModCardViewModel mod, string authorQuery) =>
        authorQuery.Length == 0 || Matches(mod.Author, authorQuery);

    // Rebuilds Sections from the current group store contents and _filtered (so group view honors
    // the same search/update-status/Fika/ads/AI filters and sort as the flat grid) - called after
    // every filter/sort change and every group mutation (add/rename/delete/move/assign) rather than
    // patched incrementally; Installed's mod counts are small enough that a full rebuild is simpler
    // and can't drift out of sync with the store.
    private void RebuildSections()
    {
        var data = AppServices.ModGroups.Load();

        Sections.Clear();

        IEnumerable<ModGroup> ordered = SelectedGroupSortOption.Value switch
        {
            GroupSortOption.NameAscending => data.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            GroupSortOption.NameDescending => data.Groups.OrderByDescending(g => g.Name, StringComparer.OrdinalIgnoreCase),
            _ => data.Groups.OrderBy(g => g.SortOrder),
        };

        foreach (var group in ordered)
            Sections.Add(ModGroupSectionViewModel.FromGroup(group));

        var ungrouped = ModGroupSectionViewModel.Ungrouped();
        Sections.Add(ungrouped);

        var byId = Sections.Where(s => s.GroupId is not null).ToDictionary(s => s.GroupId!.Value);

        foreach (var mod in _filtered)
        {
            var key = ModGroupStore.KeyFor(mod.Name);
            var section = data.Assignments.TryGetValue(key, out var groupId) && byId.TryGetValue(groupId, out var found)
                ? found
                : ungrouped;

            section.Items.Add(mod);
        }

        _sectionsDirty = false;
    }

    [RelayCommand]
    private void AddGroup()
    {
        var name = NewGroupName.Trim();
        if (name.Length == 0) return;

        AppServices.ModGroups.AddGroup(name);
        NewGroupName = string.Empty;
        GroupsChanged();
    }

    [RelayCommand]
    private void BeginRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        section.IsEditing = true;
    }

    [RelayCommand]
    private void CommitRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        section.IsEditing = false;

        var name = section.Name.Trim();
        if (name.Length == 0)
        {
            // Blank name isn't allowed - reload the real name from disk rather than saving it.
            GroupsChanged();
            return;
        }

        AppServices.ModGroups.RenameGroup(section.GroupId!.Value, name);
        GroupsChanged();
    }

    // Discards an in-progress rename without saving - the edited Name only lives on the section VM
    // in memory, so reloading fresh ones from the store (still holding the old name) undoes it.
    [RelayCommand]
    private void CancelRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        GroupsChanged();
    }

    [RelayCommand]
    private void DeleteGroup(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;

        var message = section.Items.Count == 0
            ? $"Delete \"{section.Name}\"?"
            : $"Delete \"{section.Name}\"? Its {section.CountLabel} move back to Ungrouped.";

        if (MessageBox.Show(message, "Delete group?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        AppServices.ModGroups.DeleteGroup(section.GroupId!.Value);
        GroupsChanged();
    }

    [RelayCommand]
    private void ToggleCollapsed(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;

        section.IsCollapsed = !section.IsCollapsed;
        AppServices.ModGroups.SetCollapsed(section.GroupId!.Value, section.IsCollapsed);
    }

    [RelayCommand]
    private void MoveGroupUp(ModGroupSectionViewModel? section) => MoveGroup(section, -1);

    [RelayCommand]
    private void MoveGroupDown(ModGroupSectionViewModel? section) => MoveGroup(section, 1);

    private void MoveGroup(ModGroupSectionViewModel? section, int direction)
    {
        if (section is not { IsRealGroup: true }) return;

        AppServices.ModGroups.Move(section.GroupId!.Value, direction);
        GroupsChanged();
    }

    // Called by InstalledPage's drag-drop code-behind when a mod card is dropped on a section
    // (groupId null for the Ungrouped bucket).
    public void MoveModToGroup(InstalledModCardViewModel mod, Guid? groupId)
    {
        AppServices.ModGroups.AssignMod(mod.Name, groupId);
        GroupsChanged();
    }
}
