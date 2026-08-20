using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// Shared, app-lifetime cache of the sp-mod.com catalog, backed by ModCacheStore on disk with a background refresh. IsLoading/LoadedCount/TotalCount track live fetch progress.
public partial class ModCacheViewModel : ObservableObject
{
    private readonly ModCacheService _cacheService = new(AppServices.SpModApi);
    private readonly ModCacheStore _store = new();
    private Task<List<Mod>>? _loadTask;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _loadedCount;

    [ObservableProperty]
    private int _totalCount;

    public IReadOnlyList<Mod> AllMods { get; private set; } = [];

    public bool IsLoaded => _loadTask?.IsCompletedSuccessfully == true;

    // Loads the catalog (from disk if cached, otherwise a live fetch) on first call; later calls await the same task.
    public Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        _loadTask ??= LoadAsync(ct);
        return _loadTask;
    }

    // Forces a fresh live fetch of the catalog, driving IsLoading/LoadedCount/TotalCount for the loading overlay.
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        AppLog.Debug("Catalog", "RefreshAsync: start");
        IsLoading = true;
        LoadedCount = 0;
        TotalCount = 0;
        try
        {
            var progress = new Progress<(int Loaded, int? Total)>(p =>
            {
                LoadedCount = p.Loaded;
                TotalCount = p.Total ?? 0;
            });

            var mods = await _cacheService.FetchAllAsync(progress, ct);
            AppLog.Debug("Catalog", $"RefreshAsync: live fetch done, {mods.Count} mods");
            AllMods = mods;
            _ = Task.Run(() => _store.Save(mods));

            // Replace the memoized task so future EnsureLoadedAsync calls see this fresh fetch.
            _loadTask = Task.FromResult(mods);
        }
        finally
        {
            IsLoading = false;
            AppLog.Debug("Catalog", "RefreshAsync: end (IsLoading = false)");
        }
    }

    private async Task<List<Mod>> LoadAsync(CancellationToken ct)
    {
        AppLog.Debug("Catalog", "LoadAsync: start");
        var cached = _store.Load();
        if (cached is not null)
        {
            // Use the disk cache immediately, then refresh in the background.
            AppLog.Debug("Catalog", $"LoadAsync: disk cache hit, {cached.Mods.Count} mods");
            AllMods = cached.Mods;
            _ = RefreshInBackgroundAsync(ct);
            return cached.Mods;
        }

        AppLog.Debug("Catalog", "LoadAsync: disk cache miss, starting live fetch");
        IsLoading = true;
        LoadedCount = 0;
        TotalCount = 0;
        try
        {
            var progress = new Progress<(int Loaded, int? Total)>(p =>
            {
                AppLog.Debug("Catalog", $"LoadAsync: progress {p.Loaded}/{p.Total}");
                LoadedCount = p.Loaded;
                TotalCount = p.Total ?? 0;
            });

            // Stays on the UI thread since IsLoading below is a WPF-bound property.
            var mods = await _cacheService.FetchAllAsync(progress, ct);
            AppLog.Debug("Catalog", $"LoadAsync: live fetch done, {mods.Count} mods");
            AllMods = mods;

            // Save off the UI thread.
            _ = Task.Run(() => _store.Save(mods));

            return mods;
        }
        finally
        {
            IsLoading = false;
            AppLog.Debug("Catalog", "LoadAsync: end (IsLoading = false)");
        }
    }

    private async Task RefreshInBackgroundAsync(CancellationToken ct)
    {
        try
        {
            var fresh = await _cacheService.FetchAllAsync(null, ct).ConfigureAwait(false);
            AllMods = fresh;
            _store.Save(fresh);
        }
        catch (OperationCanceledException)
        {
            // Cancelled or shutting down.
        }
        catch (SpModApiException)
        {
            // Best-effort refresh; ignore API errors.
        }
        catch (HttpRequestException)
        {
            // Best-effort refresh; ignore network errors.
        }
    }
}
