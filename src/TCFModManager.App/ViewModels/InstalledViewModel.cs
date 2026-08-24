using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

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

    /// <summary>Raised after RemoveAsync actually removes a mod from disk. Lets BrowseViewModel refresh its
    /// cards' install/update status dots. Static since InstalledViewModel is a fresh instance per navigation.</summary>
    public static event EventHandler? ModRemoved;

    private List<InstalledModCardViewModel> _all = [];
    private List<InstalledModCardViewModel> _filtered = [];

    // Who needs whom among the mods currently on disk, rebuilt on every scan. Backs the warning
    // shown before a disable takes something else's dependency away.
    private ModDependencyGraph _dependencies = ModDependencyGraph.Build([]);

    // Maps each raw scan entry back to the card it was merged into, so a dependency link (which is
    // between InstalledMods) can be reported and acted on as whole mods.
    private Dictionary<InstalledMod, InstalledModCardViewModel> _cardByEntry = [];

    // The moves the last disable/enable made, and what to call it - the undo payload.
    private List<ModMove> _lastMoves = [];
    private string? _lastMoveLabel;

    public ObservableCollection<InstalledModCardViewModel> Results { get; } = [];

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

    public List<ModSortItem> SortOptions { get; } =
    [
        new("Name (A-Z)", ModSortOption.NameAscending),
        new("Name (Z-A)", ModSortOption.NameDescending),
        new("Author (A-Z)", ModSortOption.AuthorAscending),
        new("Author (Z-A)", ModSortOption.AuthorDescending),
        new("Recently installed", ModSortOption.RecentlyInstalled),
    ];

    [ObservableProperty]
    private ModSortItem _selectedSortOption;

    // Set while ClearFilters is resetting several properties at once, so each individual
    // OnXxxChanged below doesn't run its own AutoApplyFilter.
    private bool _suppressAutoApplyFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>ON restricts results to InstalledModCardViewModel.IsFikaCompatible == true.</summary>
    [ObservableProperty]
    private bool _fikaCompatibleOnly;

    /// <summary>ON hides mods with InstalledModCardViewModel.ContainsAds == true.</summary>
    [ObservableProperty]
    private bool _hideContainsAds;

    /// <summary>ON hides mods with InstalledModCardViewModel.ContainsAiContent == true.</summary>
    [ObservableProperty]
    private bool _hideContainsAiContent;

    // ON swaps the results area from the plain card grid to collapsible, drag-sortable groups (see
    // Sections) - the user's own MO2-style separators for organizing installed mods. Purely
    // organizational: persisted via AppServices.ModGroups, nothing else reads it.
    [ObservableProperty]
    private bool _groupViewEnabled;

    /// <summary>Inverse of GroupViewEnabled - lets the flat grid/pagination controls bind their
    /// Visibility directly without a second converter.</summary>
    public bool ShowFlatList => !GroupViewEnabled;

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

    /// <summary>Whether the last disable/enable can still be put back.</summary>
    public bool CanUndo => _lastMoves.Count > 0;

    public string UndoLabel => _lastMoveLabel is null ? "Undo" : $"Undo {_lastMoveLabel}";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _columns = 1;

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
        _selectedUpdateFilter = UpdateFilterOptions[0];
        _selectedEnabledFilter = EnabledFilterOptions[0];
        _selectedSortOption = SortOptions[0];
        _selectedGroupSortOption = GroupSortOptions[0];
    }

    partial void OnSelectedUpdateFilterChanged(UpdateFilterItem value) => AutoApplyFilter();

    partial void OnSelectedEnabledFilterChanged(EnabledFilterItem value) => AutoApplyFilter();

    // Leaving selection mode drops the selection with it, so a stale tick can't be acted on later.
    partial void OnSelectionModeChanged(bool value)
    {
        if (!value) ClearSelection();
    }

    partial void OnSelectedSortOptionChanged(ModSortItem value) => AutoApplyFilter();

    partial void OnPageSizeChanged(int value) => AutoApplyFilter();

    partial void OnSearchTextChanged(string value) => AutoApplyFilter();

    partial void OnFikaCompatibleOnlyChanged(bool value) => AutoApplyFilter();

    partial void OnHideContainsAdsChanged(bool value) => AutoApplyFilter();

    partial void OnHideContainsAiContentChanged(bool value) => AutoApplyFilter();

    partial void OnGroupViewEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFlatList));

        // Group view has its own per-group enable/disable buttons and no card grid to tick, so the
        // flat grid's selection mode is turned off rather than left running invisibly behind it.
        if (value) SelectionMode = false;

        AutoApplyFilter();
    }

    partial void OnSelectedGroupSortOptionChanged(GroupSortItem value)
    {
        OnPropertyChanged(nameof(CanReorderGroups));
        RebuildSections();
    }

    /// <summary>Re-filters/re-sorts and refreshes whichever view is active (paginated grid or
    /// groups) whenever a filter/search/sort control changes. Suppressed while ClearFilters resets
    /// several properties at once.</summary>
    private void AutoApplyFilter()
    {
        if (_suppressAutoApplyFilter) return;
        ApplyFilter();
        if (GroupViewEnabled) RebuildSections(); else GoToPage(1);
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
            SelectedSortOption = SortOptions[0];
            FikaCompatibleOnly = false;
            HideContainsAds = false;
            HideContainsAiContent = false;
            PageSize = DefaultPageSize;
        }
        finally
        {
            _suppressAutoApplyFilter = false;
        }

        ApplyFilter();
        if (GroupViewEnabled) RebuildSections(); else GoToPage(1);
    }

    public void UpdateLayoutForWidth(double availableWidth)
    {
        Columns = availableWidth switch
        {
            < 700 => 1,
            < 1050 => 2,
            < 1400 => 3,
            _ => 4,
        };
    }

    [RelayCommand]
    private Task ScanAsync() => ScanAsync(resetPage: true);

    /// <summary>The actual scan. <see cref="ScanAsync()"/> always resets to page 1; a rescan triggered by
    /// something else closing (e.g. ShowDetailsAsync) can pass resetPage: false to stay on the current page.</summary>
    private async Task ScanAsync(bool resetPage)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _all = [];
            ApplyFilter();
            if (GroupViewEnabled) RebuildSections(); else GoToPage(1);
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        IsBusy = true;
        try
        {
            // Reuses whatever's already cached; triggers the one-time catalog fetch if Browse
            // hasn't been visited yet this session.
            await AppServices.ModCache.EnsureLoadedAsync();

            // Off the UI thread since the scan can take a noticeable amount of time for large collections.
            var scanned = await Task.Run(() => InstalledModScanner.Scan(installPath));

            // What this app itself installed, and which folders it placed - identifies those mods
            // exactly instead of inferring them from folder names.
            var installRecords = AppServices.InstallManifest.Load().Mods;

            _all = InstalledModCardViewModel.BuildFrom(
                    scanned, AppServices.ModCache.AllMods, AppServices.SptEnvironment.InstalledVersion, installRecords)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Built off the same scan the cards came from, so every link points at an entry some
            // card owns.
            _dependencies = ModDependencyGraph.Build(scanned);
            _cardByEntry = [];
            foreach (var card in _all)
            {
                card.PropertyChanged += OnCardPropertyChanged;
                foreach (var entry in card.Entries) _cardByEntry[entry] = card;
            }

            // A rescan replaces every card, so any previous selection is gone with them.
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedCountLabel));
            DisableSelectedCommand.NotifyCanExecuteChanged();
            EnableSelectedCommand.NotifyCanExecuteChanged();

            var unmatched = _all.Where(m => m.ModId is null).Select(m => m.Name).ToList();
            AppLog.Info("Installed",
                $"scanned {scanned.Count} folder(s) -> {_all.Count} card(s); " +
                $"{_all.Count(m => m.IsAppManaged)} app-managed, {_all.Count(m => m.IsDisabled)} disabled, " +
                $"{unmatched.Count} unmatched");
            if (unmatched.Count > 0) AppLog.Debug("Installed", $"unmatched: {string.Join(", ", unmatched)}");

            var mixed = _all.Where(m => m.IsMixedState).Select(m => m.Name).ToList();
            if (mixed.Count > 0)
                AppLog.Warn("Installed", $"present in both an enabled and a disabled folder: {string.Join(", ", mixed)}");

            ApplyFilter();
            if (GroupViewEnabled) RebuildSections(); else GoToPage(resetPage ? 1 : CurrentPage);

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

        await show(mod);
        await ScanAsync(resetPage: false);
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

    private bool HasSelection() => SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DisableSelectedAsync() => ApplyDisableAsync(SelectedCards(), disable: true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task EnableSelectedAsync() => ApplyDisableAsync(SelectedCards(), disable: false);

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
        await ScanAsync(resetPage: false);
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

        await ScanAsync(resetPage: false);
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
        CurrentPage = Math.Clamp(page, 1, TotalPages);
        Results.Clear();
        foreach (var mod in _filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
            Results.Add(mod);
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
            .Where(m => authorQuery is not null
                ? MatchesAuthor(m, authorQuery)
                : query.Length == 0 || Matches(m.DisplayTitle, query) || Matches(m.Name, query))
            .Where(m => !FikaCompatibleOnly || m.IsFikaCompatible)
            .Where(m => !HideContainsAds || !m.ContainsAds)
            .Where(m => !HideContainsAiContent || !m.ContainsAiContent);

        _filtered = SortMods(matched, SelectedSortOption.Value).ToList();

        TotalPages = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));

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
            ModSortOption.RecentlyInstalled => mods
                .OrderByDescending(m => m.InstalledAt ?? DateTimeOffset.MinValue)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            _ => mods,
        };

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
    }

    [RelayCommand]
    private void AddGroup()
    {
        var name = NewGroupName.Trim();
        if (name.Length == 0) return;

        AppServices.ModGroups.AddGroup(name);
        NewGroupName = string.Empty;
        RebuildSections();
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
            RebuildSections();
            return;
        }

        AppServices.ModGroups.RenameGroup(section.GroupId!.Value, name);
        RebuildSections();
    }

    // Discards an in-progress rename without saving - the edited Name only lives on the section VM
    // in memory, so reloading fresh ones from the store (still holding the old name) undoes it.
    [RelayCommand]
    private void CancelRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        RebuildSections();
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
        RebuildSections();
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
        RebuildSections();
    }

    // Called by InstalledPage's drag-drop code-behind when a mod card is dropped on a section
    // (groupId null for the Ungrouped bucket).
    public void MoveModToGroup(InstalledModCardViewModel mod, Guid? groupId)
    {
        AppServices.ModGroups.AssignMod(mod.Name, groupId);
        RebuildSections();
    }
}
