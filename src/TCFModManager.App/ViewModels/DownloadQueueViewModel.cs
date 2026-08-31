using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Threading.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

// App-lifetime download queue that processes one download/install at a time and resolves each item's dependencies before installing it.
public sealed partial class DownloadQueueViewModel : ObservableObject
{
    private readonly Channel<DownloadQueueItemViewModel> _channel = Channel.CreateUnbounded<DownloadQueueItemViewModel>();

    public ObservableCollection<DownloadQueueItemViewModel> Items { get; } = [];

    //
    // One line for the whole queue: how far through it is and how much is left. Worth having at all
    // because a mod list can queue forty items at once - per-card progress answers "how is this one
    // doing", not "how long until I can play".
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    private string? _summary;

    public bool HasSummary => Summary is not null;

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
        InstallTarget target,
        string versionLabel,
        string installPath,
        Func<Task<ModVersion?>> resolveVersion,
        bool checkDependencies = true,
        DownloadQueueItemViewModel? dependencyOf = null,
        long? totalBytes = null)
    {
        var item = new DownloadQueueItemViewModel(target, versionLabel, installPath, resolveVersion, checkDependencies, totalBytes);
        dependencyOf?.AddDependency(item);
        item.PropertyChanged += OnItemChanged;
        Items.Add(item);
        _channel.Writer.TryWrite(item);
        UpdateSummary();
        return item;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadQueueItemViewModel.Status)
            or nameof(DownloadQueueItemViewModel.Progress)
            or nameof(DownloadQueueItemViewModel.TotalBytes))
        {
            UpdateSummary();
        }
    }

    //
    // Sizes come from the catalog's content_length, so they are known for a mod list apply (which
    // resolves every version before queueing) and fill in one at a time for anything else. The
    // estimate is deliberately built from what is known rather than extrapolated over what isn't -
    // it says how much is left to fetch, and only adds a time once a real rate has been observed.
    //
    private void UpdateSummary()
    {
        var unfinished = Items.Where(i => !i.IsFinished).ToList();

        if (unfinished.Count == 0)
        {
            Summary = null;
            return;
        }

        var done = Items.Count - unfinished.Count;
        var parts = new List<string> { $"{done} of {Items.Count} done" };

        var remaining = unfinished.Sum(i => i.RemainingBytes ?? 0);
        var unknown = unfinished.Count(i => i.RemainingBytes is null);

        if (remaining > 0)
        {
            parts.Add(DownloadQueueItemViewModel.SizeLabel(remaining)
                + (unknown > 0 ? $" left (+{unknown} not sized yet)" : " left"));

            if (unfinished.FirstOrDefault(i => i.BytesPerSecond is > 0)?.BytesPerSecond is { } rate)
                parts.Add($"about {DownloadQueueItemViewModel.RemainingLabel(TimeSpan.FromSeconds(remaining / rate))} to go");
        }

        Summary = string.Join(" · ", parts);
    }

    // Removes every Completed/Failed/Cancelled card from the list; queued/in-progress items are left alone.
    public void ClearFinished()
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!Items[i].IsFinished) continue;

            Items[i].PropertyChanged -= OnItemChanged;
            Items.RemoveAt(i);
        }

        UpdateSummary();
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

            item.TotalBytes ??= version.ContentLength;

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

            await AppServices.ModInstall.InstallAsync(item.Target, version, item.InstallPath, status, downloadProgress, item.Token);

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
        var target = item.Target;
        var installPath = item.InstallPath;

        var sptVersion = AppServices.SptEnvironment.InstalledVersion;
        if (string.IsNullOrWhiteSpace(sptVersion) || string.IsNullOrWhiteSpace(version.Version)) return;

        List<DependencyNode> nodes;
        try
        {
            // Both endpoints resolve to ordinary mods, so everything below this point is identical
            // for an addon - what an addon requires is mods, not other addons. An addon's parent is
            // deliberately not part of this: it isn't returned here, and it's handled where the
            // addon is offered instead.
            var identifier = string.IsNullOrWhiteSpace(target.Guid) ? target.Id.ToString() : target.Guid;
            var result = target.IsAddon
                ? await AppServices.SpModApi.GetAddonDependenciesAsync($"{identifier}:{version.Version}", sptVersion)
                : await AppServices.SpModApi.GetModDependenciesAsync($"{identifier}:{version.Version}", sptVersion);
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
            scanned, AppServices.ModCache.AllMods, sptVersion, AppServices.InstallManifest.Load().Mods,
            AppServices.Addons.AllAddons);

        // Every dependency node is a mod, so addon cards - whose ModId is an addon id - are left
        // out of both sets rather than being compared against mod ids.
        var installedIds = installedMatches.Where(m => m is { IsAddon: false, ModId: not null })
            .Select(m => m.ModId!.Value).ToHashSet();
        var installedGuids = installedMatches.Where(m => m.Guid is not null)
            .Select(m => m.Guid!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Guards against two mods sharing a dependency both queuing the same download twice. A
        // cancelled card doesn't count, so a dependency dropped earlier can be picked up again.
        var queuedIds = Items
            .Where(i => i.Status != DownloadQueueItemStatus.Cancelled && !i.Target.IsAddon)
            .Select(i => i.Target.Id)
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
                InstallTarget.For(depMod),
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
