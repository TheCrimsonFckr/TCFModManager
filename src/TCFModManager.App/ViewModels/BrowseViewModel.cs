using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Views;
using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel : ObservableObject
{
    private readonly SpModApiClient _spModApi;
    private List<Mod> _filtered = [];

    /// <summary>The SPT release lines ticked in the version filter, kept so each card can describe
    /// what the mod supports on exactly those lines.</summary>
    private List<(int Major, int Minor)> _selectedLines = [];

    // Populated by RefreshInstalledIndexAsync, consumed by GoToPage/FindInstalledMatch to drive
    // each card's install/update status dot.
    private Dictionary<string, InstalledModCardViewModel> _installedByGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, InstalledModCardViewModel> _installedByName = new(StringComparer.OrdinalIgnoreCase);

    public BrowseViewModel() : this(AppServices.SpModApi)
    {
    }

    public BrowseViewModel(SpModApiClient spModApi)
    {
        _spModApi = spModApi;

        // Defaults: Newest sort, no Featured restriction.
        _selectedSortOption = SortOptions[0];
        _selectedFeaturedFilter = FeaturedFilterOptions[0];

        // Refreshes each card's install/update status dot once a queued install completes.
        AppServices.DownloadQueue.ItemInstalled += async (_, _) =>
        {
            await RefreshInstalledIndexAsync();
            GoToPage(CurrentPage);
        };

        // Refreshes the status dots when a mod is removed from the Installed page.
        InstalledViewModel.ModRemoved += async (_, _) =>
        {
            await RefreshInstalledIndexAsync();
            GoToPage(CurrentPage);
        };
    }

    // Set while ClearFilters is resetting several properties at once, so each individual
    // OnXxxChanged below doesn't run its own ApplyFilter.
    private bool _suppressAutoApplyFilter;

    partial void OnSearchTextChanged(string value) => AutoApplyFilter();

    partial void OnSelectedSortOptionChanged(SortOptionItem value) => AutoApplyFilter();

    partial void OnPageSizeChanged(int value) => AutoApplyFilter();

    partial void OnSelectedFeaturedFilterChanged(FeaturedFilterItem value) => AutoApplyFilter();

    partial void OnFikaCompatibleOnlyChanged(bool value) => AutoApplyFilter();

    partial void OnHideContainsAdsChanged(bool value) => AutoApplyFilter();

    partial void OnHideContainsAiContentChanged(bool value) => AutoApplyFilter();

    private void AutoApplyFilter()
    {
        if (!_suppressAutoApplyFilter && HasLoadedResults) ApplyFilter();
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    public List<SortOptionItem> SortOptions { get; } =
    [
        new("Newest", ModSortOrder.Newest),
        new("Last updated", ModSortOrder.LastUpdated),
        new("Most downloaded", ModSortOrder.MostDownloaded),
        new("Most favourited", ModSortOrder.MostFavourited),
    ];

    [ObservableProperty]
    private SortOptionItem _selectedSortOption;

    // How many matching cards make up one page.
    private const int DefaultPageSize = 12;

    public List<int> PageSizeOptions { get; } = [6, 9, DefaultPageSize, 15, 21, 30];

    [ObservableProperty]
    private int _pageSize = DefaultPageSize;

    public List<FeaturedFilterItem> FeaturedFilterOptions { get; } =
    [
        new("Include", FeaturedFilter.Include),
        new("Exclude", FeaturedFilter.Exclude),
        new("Only", FeaturedFilter.Only),
    ];

    [ObservableProperty]
    private FeaturedFilterItem _selectedFeaturedFilter;

    /// <summary>ON restricts results to Mod.FikaCompatibility == true.</summary>
    [ObservableProperty]
    private bool _fikaCompatibleOnly;

    /// <summary>ON hides mods with Mod.ContainsAds == true.</summary>
    [ObservableProperty]
    private bool _hideContainsAds;

    /// <summary>ON hides mods with Mod.ContainsAiContent == true.</summary>
    [ObservableProperty]
    private bool _hideContainsAiContent;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>How many columns the results grid should show, driven by the results area's available width.</summary>
    [ObservableProperty]
    private int _columns = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages = 1;

    /// <summary>The SPT version filter's checkable options, built once per session from the distinct
    /// major.minor release lines present in the cached catalog. The detected install's version starts
    /// pre-checked; an empty selection means no filter.</summary>
    public ObservableCollection<SptVersionOption> SptVersionOptions { get; } = [];

    [ObservableProperty]
    private string _sptVersionFilterSummary = "All versions";

    private bool _sptVersionOptionsBuilt;

    public ObservableCollection<ModCardViewModel> Results { get; } = [];

    /// <summary>True once a search has actually completed. Lets BrowsePage skip redundantly re-running the initial search on re-navigation.</summary>
    public bool HasLoadedResults { get; private set; }

    public void UpdateLayoutForWidth(double availableWidth)
    {
        Columns = availableWidth switch
        {
            < 700 => 1,   // small
            < 1050 => 2,  // medium
            < 1400 => 3,  // large
            _ => 4,       // extra large
        };
    }

    /// <summary>Ensures the full mod catalog is cached (fetches only on the first call each session), then filters/sorts it locally.</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        AppLog.Debug("Browse", "SearchAsync: start");
        IsBusy = true;
        try
        {
            await AppServices.SptCatalog.EnsureLoadedAsync();
            await AppServices.ModCache.EnsureLoadedAsync();
            await RefreshInstalledIndexAsync();
            AppLog.Debug("Browse", "SearchAsync: ModCache ready, applying filter");
            EnsureSptVersionOptionsBuilt();
            ApplyFilter();
            HasLoadedResults = true;
        }
        catch (SpModApiException ex)
        {
            StatusMessage = $"sp-mod.com error: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Network error: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            // HttpClient throws this (not HttpRequestException) on a request timeout.
            StatusMessage = "Timed out reaching sp-mod.com - check your connection and revisit Browse to retry.";
        }
        catch (Exception ex)
        {
            // Last-resort catch-all so the command never gets stuck without an error message.
            StatusMessage = $"Unexpected error loading the mod catalog: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            AppLog.Debug("Browse", "SearchAsync: end");
        }
    }

    /// <summary>Forces a fresh fetch of the whole sp-mod.com catalog, bypassing any cache, then re-applies the current filters.</summary>
    [RelayCommand]
    private async Task RefreshCacheAsync()
    {
        AppLog.Debug("Browse", "RefreshCacheAsync: start");
        IsBusy = true;
        try
        {
            await AppServices.ModCache.RefreshAsync();
            await RefreshInstalledIndexAsync();
            AppLog.Debug("Browse", "RefreshCacheAsync: ModCache refreshed, applying filter");
            ApplyFilter();
            HasLoadedResults = true;
        }
        catch (SpModApiException ex)
        {
            StatusMessage = $"sp-mod.com error: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Network error: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Timed out reaching sp-mod.com - check your connection and try Refresh cache again.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unexpected error refreshing the mod catalog: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            AppLog.Debug("Browse", "RefreshCacheAsync: end");
        }
    }

    /// <summary>Resets every Browse filter/sort control back to its opening default, then re-applies once immediately.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        _suppressAutoApplyFilter = true;
        try
        {
            SearchText = string.Empty;
            foreach (var option in SptVersionOptions) option.IsSelected = option.IsDefault;
            SelectedSortOption = SortOptions[0];
            PageSize = DefaultPageSize;
            SelectedFeaturedFilter = FeaturedFilterOptions[0];
            FikaCompatibleOnly = false;
            HideContainsAds = false;
            HideContainsAiContent = false;
        }
        finally
        {
            _suppressAutoApplyFilter = false;
        }

        // Only re-run the filter if there's actually a catalog to filter against yet.
        if (HasLoadedResults) ApplyFilter();
    }

    private bool CanGoToPreviousPage() => CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void PreviousPage() => GoToPage(CurrentPage - 1);

    private bool CanGoToNextPage() => CurrentPage < TotalPages;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void NextPage() => GoToPage(CurrentPage + 1);

    /// <summary>Replaces Results with exactly one page's worth of cards - never grows it, so only one page's thumbnails are ever in flight.</summary>
    private void GoToPage(int page)
    {
        var sw = Stopwatch.StartNew();
        CurrentPage = Math.Clamp(page, 1, TotalPages);
        var installedVersion = AppServices.SptEnvironment.InstalledVersion;

        Results.Clear();
        foreach (var mod in _filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
            Results.Add(ModCardViewModel.From(
                mod, installedVersion, FindInstalledMatch(mod), _selectedLines, AppServices.SptCatalog.Releases));

        AppLog.Debug("Browse", $"GoToPage: page {CurrentPage}/{TotalPages} rendered in {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>Scans the configured SPT install folder and matches it against the cached catalog to drive the
    /// install/update status dot on Browse's cards. Best-effort: no install path or nothing found just means
    /// no dot shows, not an error.</summary>
    private async Task RefreshInstalledIndexAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _installedByGuid = new(StringComparer.OrdinalIgnoreCase);
            _installedByName = new(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var scanned = await Task.Run(() => InstalledModScanner.Scan(installPath));

        // The manifest is passed even though Browse ignores IsAppManaged: it's what lets an
        // app-installed mod resolve to its exact catalog listing rather than a folder-name guess.
        var matched = InstalledModCardViewModel.BuildFrom(
            scanned, AppServices.ModCache.AllMods, AppServices.SptEnvironment.InstalledVersion,
            AppServices.InstallManifest.Load().Mods);

        // Keyed by Guid when available, MatchedModName as a fallback. Only matched entries are indexed.
        _installedByGuid = matched
            .Where(m => !string.IsNullOrWhiteSpace(m.Guid))
            .GroupBy(m => m.Guid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        _installedByName = matched
            .Where(m => !string.IsNullOrWhiteSpace(m.MatchedModName))
            .GroupBy(m => m.MatchedModName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private InstalledModCardViewModel? FindInstalledMatch(Mod mod)
    {
        if (!string.IsNullOrWhiteSpace(mod.Guid) && _installedByGuid.TryGetValue(mod.Guid, out var byGuid))
            return byGuid;

        if (!string.IsNullOrWhiteSpace(mod.Name) && _installedByName.TryGetValue(mod.Name, out var byName))
            return byName;

        return null;
    }

    private void ApplyFilter()
    {
        var sw = Stopwatch.StartNew();
        var query = SearchText.Trim();

        // A leading "@" switches the search text to matching against the mod's author(s) instead
        // of name/teaser/slug - e.g. "@Acidphantasm".
        var authorQuery = query.StartsWith('@') ? query[1..].Trim() : null;

        // Kept for GoToPage, so each card can describe what the mod supports on the ticked lines.
        _selectedLines = SptVersionOptions
            .Where(o => o.IsSelected)
            .Select(o => ExtractMajorMinor(o.Label))
            .Where(v => v is not null)
            .Select(v => (v!.Value.Major, v.Value.Minor))
            .ToList();
        var selectedLines = _selectedLines;
        var featured = SelectedFeaturedFilter.Value;

        var matched = AppServices.ModCache.AllMods
            // This app's own sp-mod.com listing is hidden here rather than dropped from the cached
            // catalog, which the self-updater still needs to be able to read. It just has no
            // business appearing among the mods this app installs into an SPT folder: it isn't a
            // mod, and installing it from here would drop a second copy of the manager into
            // BepInEx\plugins where SPT would try to load it.
            .Where(m => !string.Equals(m.Id.ToString(), SelfMod.ModId, StringComparison.Ordinal))
            .Where(m => authorQuery is not null
                ? MatchesAuthor(m, authorQuery)
                : query.Length == 0 || Matches(m.Name, query) || Matches(m.Teaser, query) || Matches(m.Slug, query))
            .Where(m => selectedLines.Count == 0 || MatchesSptVersionFilter(m, selectedLines))
            .Where(m => featured switch
            {
                FeaturedFilter.Only => m.Featured == true,
                FeaturedFilter.Exclude => m.Featured != true,
                _ => true, // Include - no restriction
            })
            .Where(m => !FikaCompatibleOnly || m.FikaCompatibility == true)
            .Where(m => !HideContainsAds || m.ContainsAds != true)
            .Where(m => !HideContainsAiContent || m.ContainsAiContent != true);

        _filtered = SelectedSortOption.Value switch
        {
            ModSortOrder.LastUpdated => matched.OrderByDescending(m => m.UpdatedAt ?? DateTimeOffset.MinValue).ToList(),
            ModSortOrder.MostDownloaded => matched.OrderByDescending(m => m.Downloads ?? 0).ToList(),
            ModSortOrder.MostFavourited => matched.OrderByDescending(m => m.FavouritesCount ?? 0).ToList(),
            // Newest - by the most recent release, so a mod that shipped an update today sorts
            // above an older mod that happened to be created more recently.
            _ => matched.OrderByDescending(NewestReleaseDate).ToList(),
        };
        AppLog.Debug("Browse", $"ApplyFilter: filter/sort took {sw.ElapsedMilliseconds}ms over {AppServices.ModCache.AllMods.Count} cached mods, {_filtered.Count} matched");

        // A fresh search always jumps back to page 1 of the new result set.
        TotalPages = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
        GoToPage(1);

        StatusMessage = _filtered.Count == 0 ? "No mods matched." : $"{_filtered.Count} mod(s) found.";
    }

    /// <summary>True if any of the mod's cached versions actually runs on one of the selected SPT
    /// release lines. Checking every cached version rather than only the newest is what keeps a mod
    /// whose latest release targets 4.1 visible under a 4.0 filter when it still has a 4.0 release.
    /// A mod with no cached version data is never hidden.</summary>
    /// <summary>The publish date of the mod's most recent cached version, falling back to the mod's
    /// own dates when it carries no version data.</summary>
    private static DateTimeOffset NewestReleaseDate(Mod mod)
    {
        var newest = (mod.Versions ?? [])
            .Select(v => v.PublishedAt)
            .Where(d => d is not null)
            .DefaultIfEmpty(null)
            .Max();

        return newest ?? mod.PublishedAt ?? mod.CreatedAt ?? DateTimeOffset.MinValue;
    }

    private static bool MatchesSptVersionFilter(Mod mod, List<(int Major, int Minor)> selectedLines)
    {
        var versions = mod.Versions ?? [];
        if (versions.Count == 0 || selectedLines.Count == 0) return true;

        var readable = versions
            .Select(v => v.SptVersionConstraint)
            .Where(c => SptVersionRange.TryParse(c, out _))
            .ToList();

        // Only when nothing about this mod is readable do we give it the benefit of the doubt.
        // Doing that per-version let one blank constraint pull a mod through every filter.
        if (readable.Count == 0) return true;

        return selectedLines.Any(line =>
            readable.Any(c => SptVersionRange.IntersectsReleaseLine(c, line.Major, line.Minor)));
    }

    /// <summary>Builds the checkable SPT version list from the release lines sp-mod.com publishes,
    /// once per session. Using the published list rather than scraping mod constraints keeps
    /// boundary versions nobody ever shipped (e.g. "4.0.4" from a "~4.0.4" constraint) out of the
    /// dropdown. The option matching the detected install starts checked; the rest start unchecked.</summary>
    private void EnsureSptVersionOptionsBuilt()
    {
        if (_sptVersionOptionsBuilt) return;

        var lines = AppServices.SptCatalog.Lines;
        if (lines.Count == 0) return; // Release list not loaded yet; try again on the next search.

        _sptVersionOptionsBuilt = true;

        var installedVersion = AppServices.SptEnvironment.InstalledVersion;
        var installedMajorMinor = ExtractMajorMinor(installedVersion);

        var majorMinors = lines.Select(l => $"{l.Major}.{l.Minor}").ToList();

        if (installedMajorMinor is not null && !majorMinors.Contains(installedMajorMinor.Value.Label))
            majorMinors.Insert(0, installedMajorMinor.Value.Label);

        foreach (var label in majorMinors)
        {
            var isInstalled = label == installedMajorMinor?.Label;
            // The installed option uses the exact detected version; every other option uses ".0"
            // as that release line's representative version.
            var value = isInstalled && !string.IsNullOrWhiteSpace(installedVersion) ? installedVersion! : $"{label}.0";
            var option = new SptVersionOption(label, value, isSelected: isInstalled);
            option.PropertyChanged += (_, _) =>
            {
                UpdateSptVersionFilterSummary();
                AutoApplyFilter();
            };
            SptVersionOptions.Add(option);
        }

        UpdateSptVersionFilterSummary();
    }

    private void UpdateSptVersionFilterSummary()
    {
        // No "SPT:" prefix here - the dropdown's own leading label (see BrowsePage.xaml) supplies it.
        var selected = SptVersionOptions.Where(o => o.IsSelected).Select(o => o.Label).ToList();
        SptVersionFilterSummary = selected.Count switch
        {
            0 => "All versions",
            <= 3 => string.Join(", ", selected),
            _ => $"{selected.Count} versions",
        };
    }

    /// <summary>Pulls the major.minor out of a version or constraint string, ignoring any leading
    /// operator (^, ~, &gt;=, etc.) - e.g. "^3.9.0" and "3.9.4" both yield (3, 9).</summary>
    private static (int Major, int Minor, string Label)? ExtractMajorMinor(string? versionOrConstraint)
    {
        if (string.IsNullOrWhiteSpace(versionOrConstraint)) return null;

        var match = MajorMinorPattern.Match(versionOrConstraint);
        if (!match.Success) return null;

        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        return (major, minor, $"{major}.{minor}");
    }

    private static readonly Regex MajorMinorPattern = new(@"(\d+)\.(\d+)", RegexOptions.Compiled);

    private static bool Matches(string? haystack, string needle) =>
        haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>Matches a "@name" query against the mod's owner and any additional authors. An
    /// empty query matches everything, same as an empty plain-text query.</summary>
    private static bool MatchesAuthor(Mod mod, string authorQuery) =>
        authorQuery.Length == 0
        || Matches(mod.Owner?.Name, authorQuery)
        || (mod.AdditionalAuthors?.Any(a => Matches(a.Name, authorQuery)) ?? false);

    /// <summary>Queues <paramref name="card"/>'s mod for download and install. Picks the newest version
    /// that targets the installed SPT version when known, otherwise the newest version overall. The
    /// version's download link is resolved lazily once the queue reaches this item, so clicking
    /// Install never waits on a network call. Gated on ReadModPageConfirmationWindow first; declining
    /// leaves the card alone.</summary>
    [RelayCommand]
    private void Install(ModCardViewModel? card) => QueueForDownload(card, "Install");

    /// <summary>Re-queues an already-installed mod's currently displayed version - the same pick
    /// Install would make - for a fresh download and reinstall. Shown on the card in Install's place
    /// once a mod is installed, e.g. to recover from corrupted or hand-edited files.</summary>
    [RelayCommand]
    private void Redownload(ModCardViewModel? card) => QueueForDownload(card, "Redownload");

    private void QueueForDownload(ModCardViewModel? card, string verb)
    {
        if (card is null) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        var mod = card.Mod;

        // Same rule as the Installed page: a disabled mod's install record points at folders it no
        // longer occupies, so reinstalling over it would place files where nothing loads them and
        // leave the disabled copy behind as a duplicate.
        if (card.IsDisabled)
        {
            StatusMessage = $"{mod.Name} is disabled - enable it on the Installed page before reinstalling it.";
            return;
        }

        if (!ReadModPageConfirmationWindow.Confirm(mod.Name ?? "this mod", mod.DetailUrl))
        {
            StatusMessage = $"{verb} cancelled - {mod.Name}'s page wasn't confirmed as read.";
            return;
        }

        var installedSptVersion = AppServices.SptEnvironment.InstalledVersion;

        // Same pick the card displays, so the queued version is the one it advertised.
        var chosen = ModCardViewModel.PickDisplayVersion(mod, installedSptVersion);

        if (chosen?.Version is null)
        {
            StatusMessage = $"{mod.Name} has no published version to install.";
            return;
        }

        var chosenVersion = chosen.Version;
        AppServices.DownloadQueue.Enqueue(mod, chosenVersion, installPath, () => ResolveVersionLinkAsync(mod, chosenVersion));
        StatusMessage = $"Queued {mod.Name} {chosenVersion} - see the Downloads page for progress.";
    }

    /// <summary>Resolves the full ModVersion (with its download Link) for exactly one version string.
    /// Called lazily from the queue so queueing itself never waits on a network call.</summary>
    private async Task<ModVersion?> ResolveVersionLinkAsync(Mod mod, string version)
    {
        var versions = await _spModApi.GetModVersionsAsync(
            mod.Id.ToString(), new ModVersionsQuery { FilterVersion = version, PerPage = 5 });
        return versions.Data.FirstOrDefault(v => v.Version == version) ?? versions.Data.FirstOrDefault();
    }

    public async Task LoadDetailsAsync(Mod mod)
    {
        try
        {
            var details = await _spModApi.GetModAsync(mod.Id.ToString(), include: "versions,license,category");
            AppServices.ModDetailsOverlay.Show(details);
        }
        catch (SpModApiException ex)
        {
            StatusMessage = $"Couldn't load details for {mod.Name}: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Network error loading details for {mod.Name}: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Timed out loading details for {mod.Name} - check your connection and try again.";
        }
        catch (Exception ex)
        {
            // Last-resort catch-all so a failure here doesn't silently look like a no-op click.
            StatusMessage = $"Unexpected error loading details for {mod.Name}: {ex.Message}";
        }
    }
}
