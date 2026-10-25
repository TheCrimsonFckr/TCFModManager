using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly SpModApiClient _spModApi;
    private List<Mod> _filtered = [];

    // OPEN-36: the addons the current search matched, by the mod they belong to.
    private Dictionary<int, IReadOnlyList<Addon>> _addonMatchesByMod = [];

    /// <summary>The SPT release lines ticked in the version filter, kept so each card can describe
    /// what the mod supports on exactly those lines.</summary>
    private List<(int Major, int Minor)> _selectedLines = [];

    // Populated by RefreshInstalledIndexAsync, consumed by GoToPage/FindInstalledMatch to drive
    // each card's install/update status dot.
    private Dictionary<string, InstalledModCardViewModel> _installedByGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, InstalledModCardViewModel> _installedByName = new(StringComparer.OrdinalIgnoreCase);

    // The Filters panel sections pinned under the search bar (OPEN-24).
    public PinnedFilterSections<BrowseFilterSection> FilterPins { get; }

    // Named filter presets (OPEN-25), the first section of the Filters panel.
    public FilterPresetList<BrowseFilterPreset> Presets { get; }

    public BrowseViewModel() : this(AppServices.SpModApi)
    {
    }

    public BrowseViewModel(SpModApiClient spModApi)
    {
        _spModApi = spModApi;

        var settings = new SettingsService().Load();
        _defaults = settings.BrowseDefaults;
        _showAddonMatches = settings.ShowAddonMatches;

        //
        // Backing fields rather than the properties: this is the page opening at its default, not
        // someone changing a filter, so nothing here should run a filter pass over a catalog that
        // has not loaded yet.
        //
        // With nothing saved these are the app's own defaults - Newest sort, no Featured
        // restriction, twelve per page.
        //
        _selectedSortOption = DefaultSortOption();
        _selectedFeaturedFilter = DefaultFeaturedFilter();
        _pageSize = DefaultPageSize();
        _selectedPublishedRange = DefaultDateRange(_defaults?.Published);
        _selectedUpdatedRange = DefaultDateRange(_defaults?.Updated);
        _selectedSearchScope = DefaultSearchScope();

        // Category and SPT version are resolved later: both lists are built from the catalog, and
        // neither exists yet - see EnsureCategoryOptionsBuilt/EnsureSptVersionOptionsBuilt.

        // Refreshes each card's install/update status dot once a queued install completes.
        AppServices.DownloadQueue.ItemInstalled += async (_, _) =>
        {
            await RefreshInstalledIndexAsync();
            ShowInstalledChange();
        };

        // Refreshes the status dots when a mod is removed from the Installed page.
        InstalledViewModel.ModRemoved += async (_, _) =>
        {
            await RefreshInstalledIndexAsync();
            ShowInstalledChange();
        };

        //
        // The update watcher swapped fresh listings into the catalog. The cards on screen still
        // hold the old ones, so the filter runs again over the patched list - on the same page -
        // and the status dots pick up the new versions.
        //
        AppServices.UpdateWatcher.UpdatesFound += async (_, _) => await RefreshAfterUpdateCheckAsync();

        // Held-back updates (OPEN-12 F11) change which installed mods show an update, the same way.
        AppServices.HeldBack.Changed += async (_, _) => await RefreshAfterUpdateCheckAsync();

        // Before the subscription below, so applying a saved default doesn't count as a change.
        SavedFilterDefaults.ApplyAttributes(AttributeOptions, _defaults?.Attributes);

        // Each tick box drives the same re-filter a dropdown selection does. Subscribed rather
        // than bound through a property apiece, so adding an option is one line in the list above.
        foreach (var option in AttributeOptions)
        {
            option.PropertyChanged += (_, _) =>
            {
                UpdateAttributeFilterSummary();
                AutoApplyFilter();
            };
        }

        UpdateAttributeFilterSummary();

        // OPEN-12 A4: a new-mods notification opens Browse on the followed authors' mods, and
        // following someone changes what that filter matches.
        if (AppNavigation.TakeShowFollowed()) ShowFollowed();
        AppNavigation.ShowFollowedRequested += (_, _) =>
        {
            if (AppNavigation.TakeShowFollowed()) ShowFollowed();
        };
        AppServices.Followed.Changed += (_, _) =>
        {
            if (IsOn(ModAttributeFilter.ByFollowedAuthors)) AutoApplyFilter();
        };
        AppServices.Ignored.Changed += (_, _) => ShowIgnoredChange();

        // The addon catalog usually settles after the first page has already rendered, so the
        // "N addons" badges are redrawn once it does rather than waiting for a page change.
        AppServices.Addons.AddonsChanged += (_, _) =>
        {
            if (!HasLoadedResults) return;
            if (SearchText.Trim().Length > 0) ApplyFilter();
            GoToPage(CurrentPage);
        };

        // Last, so every filter the dropdown labels read is in place.
        FilterPins = new PinnedFilterSections<BrowseFilterSection>(
            "Browse", settings, x => x.BrowsePinnedFilters, (x, v) => x.BrowsePinnedFilters = v, this, PinnedLabel);

        Presets = new FilterPresetList<BrowseFilterPreset>(
            "Browse", settings, x => x.BrowseFilterPresets, CapturePreset, ApplyPreset, text => StatusMessage = text);
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

    [ObservableProperty]
    private bool _isBusy;

    //
    // Drives the startup panel over the results area, and only on the first load of a session -
    // a later search keeps the results that are already there rather than blanking them.
    //
    // The catalog is cached on disk, so most launches spend well under a second here; a first run
    // with no cache fetches ~3000 mods a page at a time and can take considerably longer, which is
    // the case this exists for. Everything it needed to say was already being tracked and simply
    // had nothing bound to it.
    //
    [ObservableProperty]
    private bool _isStartingUp;

    [ObservableProperty]
    private string _startupStage = Strings.Browse_StageStarting;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<ModCardViewModel> Results { get; } = [];

    // The first page at startup only - see GoToPage. Two cards a frame keeps the startup panel's
    // spinner turning while they are built.
    private readonly GradualFill<ModCardViewModel> _startupFill = new(batchSize: 2);

    /// <summary>True once a search has actually completed. Lets BrowsePage skip redundantly re-running the initial search on re-navigation.</summary>
    public bool HasLoadedResults { get; private set; }

    /// <summary>Ensures the full mod catalog is cached (fetches only on the first call each session), then filters/sorts it locally.</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        AppLog.Debug("Browse", "SearchAsync: start");
        IsBusy = true;
        IsStartingUp = !HasLoadedResults;
        try
        {
            StartupStage = Strings.Browse_StageSptVersions;
            await AppServices.SptCatalog.EnsureLoadedAsync();

            StartupStage = Strings.Browse_StageCatalog;
            await AppServices.ModCache.EnsureLoadedAsync();

            StartupStage = Strings.Browse_StageAddons;
            await AppServices.Addons.EnsureLoadedAsync();

            StartupStage = Strings.Browse_StageInstalled;
            await RefreshInstalledIndexAsync();

            AppLog.Debug("Browse", "SearchAsync: ModCache ready, applying filter");
            StartupStage = Strings.Browse_StageSorting;
            EnsureSptVersionOptionsBuilt();
            EnsureCategoryOptionsBuilt();
            ApplyFilter();
            HasLoadedResults = true;

            //
            // Keeps the startup panel up until the first page of cards has been built and drawn
            // underneath it. Building them is ~300ms of WPF work; done after the panel had gone, that
            // was a frozen, empty results area. Built a couple per frame behind the panel instead,
            // the spinner keeps turning and the cards are simply there when it lifts.
            //
            if (IsStartingUp)
            {
                await _startupFill.WhenFilledAsync();
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            }
        }
        catch (SpModApiException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (OperationCanceledException)
        {
            // HttpClient throws this (not HttpRequestException) on a request timeout.
            StatusMessage = Strings.Browse_TimedOutLoading;
        }
        catch (Exception ex)
        {
            // Last-resort catch-all so the command never gets stuck without an error message.
            StatusMessage = Text(Strings.Browse_UnexpectedLoadFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsStartingUp = false;
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
            await AppServices.Addons.RefreshAsync();
            await RefreshInstalledIndexAsync();
            AppLog.Debug("Browse", "RefreshCacheAsync: ModCache refreshed, applying filter");
            ApplyFilter();
            HasLoadedResults = true;
        }
        catch (SpModApiException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Strings.Browse_TimedOutRefreshing;
        }
        catch (Exception ex)
        {
            StatusMessage = Text(Strings.Browse_UnexpectedRefreshFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
            AppLog.Debug("Browse", "RefreshCacheAsync: end");
        }
    }
}
