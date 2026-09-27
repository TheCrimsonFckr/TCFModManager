using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

// 
// Shared, app-lifetime cache of every addon published on sp-mod.com, backed by AddonCacheStore on
// disk with a background refresh. There are under a hundred addons in total against thousands of
// mods, so the whole set is held in memory and every lookup below is served from it - no addon
// screen ever waits on the network.
// 
public partial class AddonCacheViewModel : LocalizedViewModel
{
    private readonly AddonCacheService _cacheService = new(AppServices.SpModApi);
    private readonly AddonCacheStore _store = new();
    private Task<List<Addon>>? _loadTask;

    // Addon ids grouped by the mod they attach to, rebuilt whenever AllAddons is replaced.
    private IReadOnlyDictionary<int, List<Addon>> _byParent = new Dictionary<int, List<Addon>>();

    public IReadOnlyList<Addon> AllAddons { get; private set; } = [];

    public bool IsLoaded => _loadTask?.IsCompletedSuccessfully == true;

    // Loads the addon catalog (from disk if cached, otherwise a live fetch) on first call; later calls await the same task.
    public Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        _loadTask ??= LoadAsync(ct);
        return _loadTask;
    }

    // 
    // The addons attached to a mod, most downloaded first. An addon whose parent isn't in the mod
    // catalog is never returned simply because nothing asks for it - which is also how detached
    // addons and addons to mods below the SPT cache floor stay out of the UI without a filter of
    // their own.
    // 
    public IReadOnlyList<Addon> ForMod(int modId) =>
        _byParent.TryGetValue(modId, out var addons) ? addons : [];

    public int CountFor(int modId) => ForMod(modId).Count;

    public Addon? ById(int addonId) => AllAddons.FirstOrDefault(a => a.Id == addonId);

    // Forces a fresh live fetch, replacing both the in-memory set and the disk cache.
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var addons = await _cacheService.FetchAllAsync(null, ct);
        Publish(addons);
        _ = Task.Run(() => _store.Save(addons), CancellationToken.None);
        _loadTask = Task.FromResult(addons);
    }

    //
    // Swaps in fresh listings for the installed addons, found by the update watcher (D3). Same
    // rule as ModCacheViewModel.Patch: an addon the cache doesn't hold is not added.
    //
    // Every installed addon is re-read on every check, so a listing is only swapped in when its
    // releases changed. Otherwise each check would raise AddonsChanged and redraw Browse under
    // whoever is reading it, hourly, for nothing.
    //
    public bool Patch(IReadOnlyList<Addon> fresh)
    {
        if (fresh.Count == 0 || AllAddons.Count == 0) return false;

        var byId = fresh.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.Last());
        var replaced = 0;

        var patched = AllAddons
            .Select(a =>
            {
                if (!byId.TryGetValue(a.Id, out var listing) || ReleasesOf(listing) == ReleasesOf(a)) return a;
                replaced++;
                return listing;
            })
            .ToList();

        if (replaced == 0) return false;

        Publish(patched);
        _loadTask = Task.FromResult(patched);
        _ = Task.Run(() => _store.Save(patched), CancellationToken.None);
        return true;
    }

    private static string ReleasesOf(Addon addon) =>
        string.Join(',', (addon.Versions ?? []).Select(v => $"{v.Id}:{v.Version}"));

    private async Task<List<Addon>> LoadAsync(CancellationToken ct)
    {
        var cached = await Task.Run(_store.Load, ct);
        if (cached is not null)
        {
            AppLog.Debug("Addons", $"disk cache hit, {cached.Addons.Count} addons");
            Publish(cached.Addons);
            _ = RefreshInBackgroundAsync(ct);
            return cached.Addons;
        }

        AppLog.Debug("Addons", "disk cache miss, starting live fetch");
        var addons = await _cacheService.FetchAllAsync(null, ct);
        Publish(addons);
        _ = Task.Run(() => _store.Save(addons), CancellationToken.None);
        return addons;
    }

    private async Task RefreshInBackgroundAsync(CancellationToken ct)
    {
        try
        {
            var fresh = await _cacheService.FetchAllAsync(null, ct).ConfigureAwait(false);
            Publish(fresh);
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

    private void Publish(List<Addon> addons)
    {
        AllAddons = addons;
        _byParent = addons
            .Where(a => a.ModId is not null)
            .GroupBy(a => a.ModId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.Downloads ?? 0).ToList());

        if (AddonsChanged is null) return;

        // The background refresh raises this off the UI thread, and every subscriber redraws
        // something bound to it.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) AddonsChanged.Invoke(this, EventArgs.Empty);
        else dispatcher.BeginInvoke(() => AddonsChanged?.Invoke(this, EventArgs.Empty));
    }

    // Raised after AllAddons is replaced, so Browse can refresh its cards' addon badges.
    public event EventHandler? AddonsChanged;
}
