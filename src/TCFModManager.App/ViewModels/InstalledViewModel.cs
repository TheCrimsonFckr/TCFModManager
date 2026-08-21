using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    }

    partial void OnSelectedUpdateFilterChanged(UpdateFilterItem value) => AutoApplyFilter();

    partial void OnPageSizeChanged(int value) => AutoApplyFilter();

    partial void OnSearchTextChanged(string value) => AutoApplyFilter();

    partial void OnFikaCompatibleOnlyChanged(bool value) => AutoApplyFilter();

    partial void OnHideContainsAdsChanged(bool value) => AutoApplyFilter();

    partial void OnHideContainsAiContentChanged(bool value) => AutoApplyFilter();

    /// <summary>Re-filters and jumps back to page 1 whenever a filter/search control changes. Suppressed
    /// while ClearFilters resets several properties at once.</summary>
    private void AutoApplyFilter()
    {
        if (_suppressAutoApplyFilter) return;
        ApplyFilter();
        GoToPage(1);
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
        GoToPage(1);
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
            GoToPage(1);
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

            var unmatched = _all.Where(m => m.ModId is null).Select(m => m.Name).ToList();
            AppLog.Info("Installed",
                $"scanned {scanned.Count} folder(s) -> {_all.Count} card(s); " +
                $"{_all.Count(m => m.IsAppManaged)} app-managed, {unmatched.Count} unmatched");
            if (unmatched.Count > 0) AppLog.Debug("Installed", $"unmatched: {string.Join(", ", unmatched)}");

            ApplyFilter();
            GoToPage(resetPage ? 1 : CurrentPage);

            StatusMessage = _all.Count == 0
                ? $"No mods found under \"{installPath}\"."
                : _filtered.Count == _all.Count
                    ? $"{_all.Count} mod(s) found."
                    : $"{_filtered.Count} of {_all.Count} mod(s) shown.";
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

    private static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <summary>Confirms a removal and, when the mod has config files of its own, asks what should happen to
    /// them in the same prompt. Returns null when the user backed out.</summary>
    private static ConfigAction? ConfirmRemoval(string modName, string message, int configCount)
    {
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

        _filtered = _all
            .Where(m => SelectedUpdateFilter.Value switch
            {
                UpdateFilter.NeedsUpdate => m.UpdateAvailable == true,
                UpdateFilter.UpToDate => m.UpdateAvailable == false,
                UpdateFilter.NotFound => m.MatchedModName is null,
                _ => true, // All - no restriction
            })
            .Where(m => authorQuery is not null
                ? MatchesAuthor(m, authorQuery)
                : query.Length == 0 || Matches(m.DisplayTitle, query) || Matches(m.Name, query))
            .Where(m => !FikaCompatibleOnly || m.IsFikaCompatible)
            .Where(m => !HideContainsAds || !m.ContainsAds)
            .Where(m => !HideContainsAiContent || !m.ContainsAiContent)
            .ToList();

        TotalPages = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));

        // Keeps the status line up to date as soon as a filter/search control changes; skipped when
        // _all is empty since ScanAsync's own "No mods found under ..." message is more useful there.
        if (_all.Count > 0)
        {
            StatusMessage = _filtered.Count == _all.Count
                ? $"{_all.Count} mod(s) found."
                : $"{_filtered.Count} of {_all.Count} mod(s) shown.";
        }
    }

    private static bool Matches(string? haystack, string needle) =>
        haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>Matches a "@name" query against the matched catalog listing's author. An empty query matches
    /// everything, same as an empty plain-text query.</summary>
    private static bool MatchesAuthor(InstalledModCardViewModel mod, string authorQuery) =>
        authorQuery.Length == 0 || Matches(mod.Author, authorQuery);
}
