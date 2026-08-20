using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Channels;
using TCFModManagement.App.Views;
using TCFModManagement.Core.Models;
using TCFModManagement.Core.Services;
using TCFModManagement.Core.SpModApi;

namespace TCFModManagement.App.ViewModels;

// App-lifetime download queue that processes one download/install at a time and resolves each item's dependencies before installing it.
public sealed class DownloadQueueViewModel
{
    private readonly Channel<DownloadQueueItemViewModel> _channel = Channel.CreateUnbounded<DownloadQueueItemViewModel>();

    public ObservableCollection<DownloadQueueItemViewModel> Items { get; } = [];

    // Raised after an item finishes installing successfully. BrowseViewModel subscribes to refresh its cards' install/update status dots.
    public event EventHandler? ItemInstalled;

    public DownloadQueueViewModel()
    {
        _ = ProcessQueueAsync();
    }

    // Adds a request to the end of the queue and returns immediately; the download/install
    // happens later when the worker reaches it. When <paramref name="dependencyOf"/> is set, the
    // new item is registered against it so cancelling that item cancels this one too.
    public DownloadQueueItemViewModel Enqueue(
        Mod mod,
        string versionLabel,
        string installPath,
        Func<Task<ModVersion?>> resolveVersion,
        bool checkDependencies = true,
        DownloadQueueItemViewModel? dependencyOf = null)
    {
        var item = new DownloadQueueItemViewModel(mod, versionLabel, installPath, resolveVersion, checkDependencies);
        dependencyOf?.AddDependency(item);
        Items.Add(item);
        _channel.Writer.TryWrite(item);
        return item;
    }

    // Removes every Completed/Failed/Cancelled card from the list; queued/in-progress items are left alone.
    public void ClearFinished()
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].IsFinished) Items.RemoveAt(i);
        }
    }

    // FIFO single-reader loop that processes one item at a time for the entire app session. A failed item doesn't stop the loop.
    private async Task ProcessQueueAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            await ProcessItemAsync(item);
        }
    }

    private async Task ProcessItemAsync(DownloadQueueItemViewModel item)
    {
        // Cancelled while it sat in the queue behind something else.
        if (item.Status == DownloadQueueItemStatus.Cancelled || item.Token.IsCancellationRequested)
        {
            item.Status = DownloadQueueItemStatus.Cancelled;
            item.StatusMessage = "Cancelled before it started.";
            return;
        }

        try
        {
            item.Status = DownloadQueueItemStatus.Downloading;
            item.StatusMessage = "Resolving download link...";

            var version = await item.ResolveVersionAsync();
            if (version?.Link is null)
            {
                item.Status = DownloadQueueItemStatus.Failed;
                item.StatusMessage = $"Couldn't find a download link for {item.ModName} {item.VersionLabel}.";
                return;
            }

            item.Token.ThrowIfCancellationRequested();

            // Checked before this item's own download starts, so an accepted missing dependency
            // lands right behind it in the queue.
            if (item.CheckDependencies)
            {
                item.StatusMessage = "Checking dependencies...";
                await CheckDependenciesAsync(item, version);
            }

            item.Token.ThrowIfCancellationRequested();

            // Maps the "Downloading ..." status text to the Downloading stage; every other phase
            // (removing previous version, extracting, copying files) is bucketed under Installing.
            var status = new Progress<string>(s =>
            {
                item.StatusMessage = s;
                item.Status = s.Contains("Downloading", StringComparison.OrdinalIgnoreCase)
                    ? DownloadQueueItemStatus.Downloading
                    : DownloadQueueItemStatus.Installing;
            });
            var downloadProgress = new Progress<double>(p => item.Progress = p);

            await AppServices.ModInstall.InstallAsync(item.Mod, version, item.InstallPath, status, downloadProgress, item.Token);

            item.Status = DownloadQueueItemStatus.Completed;
            item.Progress = 1.0;
            item.StatusMessage = $"Installed {item.ModName} {item.VersionLabel}.";
            ItemInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (item.Token.IsCancellationRequested)
        {
            item.Status = DownloadQueueItemStatus.Cancelled;
            item.Progress = 0;
            item.StatusMessage = $"Cancelled {item.ModName} {item.VersionLabel}.";
        }
        catch (SpModApiRateLimitedException ex)
        {
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = $"Rate limited by sp-mod.com - try again in {ex.RetryAfter?.TotalSeconds ?? 30:N0}s.";
        }
        catch (SpModApiException ex)
        {
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = $"sp-mod.com error: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = $"Network error: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            // ModInstallService's own validation error message, already user-facing.
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = $"Unexpected error: {ex.Message}";
        }
    }

    // Resolves item's full dependency tree for the version being installed, cross-references it against
    // a fresh disk scan + catalog match, and offers to queue anything missing via one
    // ReadModPageConfirmationWindow listing every missing mod. Anything queued is registered as a
    // dependency of <paramref name="item"/>, so cancelling it cancels them too. Best-effort: a
    // failed lookup silently skips the check rather than failing the queued item.
    private async Task CheckDependenciesAsync(DownloadQueueItemViewModel item, ModVersion version)
    {
        var mod = item.Mod;
        var installPath = item.InstallPath;

        var sptVersion = AppServices.SptEnvironment.InstalledVersion;
        if (string.IsNullOrWhiteSpace(sptVersion) || string.IsNullOrWhiteSpace(version.Version)) return;

        List<DependencyNode> nodes;
        try
        {
            var identifier = string.IsNullOrWhiteSpace(mod.Guid) ? mod.Id.ToString() : mod.Guid;
            var result = await AppServices.SpModApi.GetModDependenciesAsync($"{identifier}:{version.Version}", sptVersion);
            nodes = result.Values.FirstOrDefault() ?? [];
        }
        catch (Exception)
        {
            // Rate limited, network error, or an unrecognized SPT version - skip the check.
            return;
        }

        if (nodes.Count == 0) return;

        item.Token.ThrowIfCancellationRequested();

        // Fresh disk scan each time so it reflects whatever was just installed in this same batch.
        await AppServices.ModCache.EnsureLoadedAsync();
        var scanned = await Task.Run(() => InstalledModScanner.Scan(installPath));
        var installedMatches = InstalledModCardViewModel.BuildFrom(
            scanned, AppServices.ModCache.AllMods, sptVersion, AppServices.InstallManifest.Load().Mods);
        var installedIds = installedMatches.Where(m => m.ModId is not null).Select(m => m.ModId!.Value).ToHashSet();
        var installedGuids = installedMatches.Where(m => m.Guid is not null)
            .Select(m => m.Guid!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Guards against two mods sharing a dependency both queuing the same download twice. A
        // cancelled card doesn't count, so a dependency dropped earlier can be picked up again.
        var queuedIds = Items
            .Where(i => i.Status != DownloadQueueItemStatus.Cancelled)
            .Select(i => i.Mod.Id)
            .ToHashSet();

        var missing = Flatten(nodes)
            .Where(n => n.LatestCompatibleVersion is not null)
            .Where(n => !installedIds.Contains(n.Id) && !queuedIds.Contains(n.Id))
            .Where(n => n.Guid is null || !installedGuids.Contains(n.Guid))
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .ToList();

        if (missing.Count == 0) return;

        item.Token.ThrowIfCancellationRequested();

        // Prefer each cached catalog Mod when available; fall back to a minimal Mod built from
        // the dependency node's own fields.
        var depDetails = missing
            .Select(dep => (
                Dep: dep,
                Mod: AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == dep.Id)
                    ?? new Mod { Id = dep.Id, Guid = dep.Guid, Name = dep.Name, Slug = dep.Slug }))
            .ToList();

        // One gate covering every missing dependency at once: each mod's page must be opened
        // before Continue unlocks, replacing what was previously a separate Yes/No prompt plus a
        // per-dependency read-page confirmation.
        var links = depDetails
            .Select(d => new ModPageLink(d.Mod.Name ?? d.Dep.Name ?? $"mod {d.Dep.Id}", d.Mod.DetailUrl))
            .ToList();
        if (!ReadModPageConfirmationWindow.ConfirmAll(links)) return;

        item.Token.ThrowIfCancellationRequested();

        foreach (var (dep, depMod) in depDetails)
        {
            // LatestCompatibleVersion already has the Link/ContentLength needed; no further lookup required.
            var depVersion = new ModVersion
            {
                Id = dep.LatestCompatibleVersion!.Id,
                Version = dep.LatestCompatibleVersion.Version,
                Link = dep.LatestCompatibleVersion.Link,
                ContentLength = dep.LatestCompatibleVersion.ContentLength,
                FikaCompatibility = dep.LatestCompatibleVersion.FikaCompatibility,
            };

            Enqueue(
                depMod,
                depVersion.Version ?? "unknown",
                installPath,
                () => Task.FromResult<ModVersion?>(depVersion),
                checkDependencies: false,
                dependencyOf: item);
        }
    }

    // Walks a resolved dependency tree depth-first, flattening every nested level into one sequence.
    private static IEnumerable<DependencyNode> Flatten(IEnumerable<DependencyNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Dependencies)) yield return child;
        }
    }
}
