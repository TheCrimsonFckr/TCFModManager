using System.ComponentModel;
using System.Windows;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Services;

// One read of the install, reduced to the shape every Core mod-list piece takes.
public sealed record ModListInstall(
    string InstallPath,
    IReadOnlyList<ModListCandidate> Candidates,
    string? SptVersion);

//
// A list, what applying it would do, and the install both were worked out against.
//
// Held together on purpose: the plan shown to the user and the plan that runs have to be the same
// one, worked out against the same scan, or a rescan between the two silently changes what happens.
//
public sealed record ModListPreview(ModList List, ModListPlan Plan, ModListInstall Install);

//
// The App half of mod lists - everything Core deliberately cannot reach.
//
// Core owns the model, the capture, the diff and the ordering of an apply; it can't own the scan
// (which produces App view models) or the downloads (the queue is an App type). This turns the
// Installed cards into ModListCandidates and the plan's fetches into queued downloads, and hands
// both to Core.
//
public sealed class ModListService
{
    //
    // Scans the install and builds the candidate list, off the UI thread - the same scan-and-match
    // pass InstalledViewModel runs, for the same reason it runs it in Task.Run.
    //
    // Returns null when no SPT install folder is set.
    //
    public async Task<ModListInstall?> ReadInstallAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return null;

        await AppServices.ModCache.EnsureLoadedAsync();
        await AppServices.Addons.EnsureLoadedAsync();

        var records = AppServices.InstallManifest.Load().Mods;
        var catalog = AppServices.ModCache.AllMods;
        var sptVersion = AppServices.SptEnvironment.InstalledVersion;

        var candidates = await Task.Run(() =>
        {
            var found = InstalledModScanner.Scan(installPath);

            var cards = InstalledModCardViewModel.BuildFrom(
                found, catalog, sptVersion, records, AppServices.Addons.AllAddons);

            return ModListCandidates.From(cards, records);
        });

        return new ModListInstall(installPath, candidates, sptVersion);
    }

    // Captures what's installed now as a new list and stores it.
    public async Task<ModList?> CaptureAsync(string name, bool includeDisabled = false)
    {
        var install = await ReadInstallAsync();
        if (install is null) return null;

        var list = ModListCapture.Build(
            name,
            install.Candidates,
            DateTimeOffset.UtcNow,
            CatalogVersions(),
            install.SptVersion,
            includeDisabled: includeDisabled,
            addonVersions: CatalogAddonVersions());

        return AppServices.ModLists.Add(list);
    }

    // Works out what applying this list would do. Nothing moves and nothing downloads.
    public async Task<ModListPreview?> PreviewAsync(ModList list, IReadOnlySet<string>? neverAutoDisable = null)
    {
        var install = await ReadInstallAsync();
        if (install is null) return null;

        return new ModListPreview(list, ModListPlanner.Build(list, install.Candidates, neverAutoDisable), install);
    }

    //
    // Applies a preview. Core decides the order and when to stop; everything here is the download
    // half plus storing what came back.
    //
    // <param name="prompts">Who answers the two questions a fetch has to ask. Defaults to answering
    // no to both, so an unwired caller downloads nothing rather than everything.</param>
    //
    public async Task<ModListApplyResult> ApplyAsync(
        ModListPreview preview,
        ModListPrompts? prompts = null,
        bool takeSnapshot = true,
        CancellationToken ct = default)
    {
        prompts ??= ModListPrompts.Reject;

        var result = await ModListApplier.ApplyAsync(
            preview.Plan,
            preview.Install.Candidates,
            (fetches, token) => FetchAsync(preview.Install, fetches, prompts, token),
            new ModListApplyOptions
            {
                // Named after the list being applied, not "Before X" - the snapshot is only ever
                // shown on the revert button, which words it, and a stored "Before ..." was what
                // grew another "Before " every time one got applied.
                SnapshotName = takeSnapshot ? preview.List.Name : null,
                SnapshotVersions = CatalogVersions(),
                SnapshotAddonVersions = CatalogAddonVersions(),
                SptVersion = preview.Install.SptVersion,
            },
            ct: ct);

        if (result.Snapshot is not null) AppServices.ModLists.SetSnapshot(result.Snapshot);
        if (result.Completed) AppServices.ModLists.SetActive(preview.List.Id);

        return result;
    }

    //
    // Puts the install back the way it was before the last list was applied.
    //
    // Takes no snapshot of its own and clears the one it used: there is a single undo point, so a
    // revert is the end of the chain rather than something else to undo. Without that, applying a
    // snapshot snapshotted the snapshot and every name grew another "Before ".
    //
    // Normally downloads nothing - the snapshot only ever names mods that were installed at the
    // time, so the plan comes out as enables and disables.
    //
    public async Task<ModListApplyResult?> RevertAsync(ModListPrompts? prompts = null, CancellationToken ct = default)
    {
        if (AppServices.ModLists.GetSnapshot() is not { } snapshot) return null;

        var preview = await PreviewAsync(snapshot);
        if (preview is null) return null;

        var result = await ModListApplier.ApplyAsync(
            preview.Plan,
            preview.Install.Candidates,
            (fetches, token) => FetchAsync(preview.Install, fetches, prompts ?? ModListPrompts.Reject, token),
            new ModListApplyOptions { SptVersion = preview.Install.SptVersion },
            ct: ct);

        if (result.Completed)
        {
            AppServices.ModLists.SetSnapshot(null);
            AppServices.ModLists.SetActive(null);
        }

        return result;
    }

    // How the install stood before the last apply, or null when nothing has been applied yet.
    public ModList? PendingRevert() => AppServices.ModLists.GetSnapshot();

    //
    // Resolves what each fetch would actually download, asks about anything the list can't have as
    // written, then queues the lot.
    //
    // Every version is resolved before anything is enqueued, rather than lazily inside the queue
    // worker the way a single Install does. A list is a batch: the user should be told up front
    // that three of forty mods can't be had at the pinned version, not find out one at a time while
    // downloads are already running.
    //
    private async Task<ModListFetchOutcome> FetchAsync(
        ModListInstall install,
        IReadOnlyList<ModListAction> fetches,
        ModListPrompts prompts,
        CancellationToken ct)
    {
        var resolution = await ResolveAsync(fetches, install.Candidates, ct);

        var accepted = prompts.ApproveVersionChanges(resolution.Changes);
        var acceptedActions = accepted.Select(c => c.Action).ToHashSet();

        var failed = new List<ModListFetchFailure>(resolution.Unavailable);

        foreach (var change in resolution.Changes.Where(c => !acceptedActions.Contains(c.Action)))
        {
            failed.Add(new ModListFetchFailure(
                change.Action.Name,
                $"version {change.Wanted} is no longer published and the newer one wasn't taken"));
        }

        var downloads = new List<ModListDownload>(resolution.Ready);
        downloads.AddRange(accepted.Select(c => new ModListDownload(c.Action, c.Target, c.Available, IsSubstitute: true)));

        if (downloads.Count == 0) return new ModListFetchOutcome([], failed, false);

        //
        // The same gate a manual install goes through, asked once for the whole batch rather than
        // once per mod - ConfirmAll is the existing path for exactly this, and it honours the
        // Options switch that turns the gate off.
        //
        if (!prompts.ConfirmModPages(downloads))
            return new ModListFetchOutcome([], failed, Cancelled: true);

        return await QueueAsync(install.InstallPath, downloads, failed, ct);
    }

    //
    // Everything is enqueued first and awaited afterwards, so the queue's own worker decides the
    // order and the user sees the whole batch on the Downloads page at once rather than one card
    // appearing at a time.
    //
    private static async Task<ModListFetchOutcome> QueueAsync(
        string installPath,
        IReadOnlyList<ModListDownload> downloads,
        List<ModListFetchFailure> failed,
        CancellationToken ct)
    {
        var waiting = new List<(ModListDownload Download, DownloadQueueItemViewModel Item)>();

        foreach (var download in downloads)
        {
            var version = download.Version;

            var item = await EnqueueAsync(
                download.Target,
                version.Version ?? download.Action.TargetVersion ?? "latest",
                installPath,
                () => Task.FromResult<ModVersion?>(version),
                version.ContentLength);

            waiting.Add((download, item));
        }

        using var cancelling = ct.Register(() => CancelAll(waiting.Select(w => w.Item)));

        var fetched = new List<ModListAction>();
        var cancelled = false;

        foreach (var (download, item) in waiting)
        {
            switch (await WaitForAsync(item))
            {
                case DownloadQueueItemStatus.Completed:
                    fetched.Add(download.Action);
                    break;

                case DownloadQueueItemStatus.Cancelled:
                    cancelled = true;
                    break;

                default:
                    failed.Add(new ModListFetchFailure(download.Action.Name, item.StatusMessage));
                    break;
            }
        }

        return new ModListFetchOutcome(fetched, failed, cancelled);
    }

    //
    // Works out the exact version behind each fetch, and separates the ones the list can't have as
    // written. A pinned version that is gone becomes a question, never a silent substitution - the
    // list named a specific build, and quietly installing a different one is what desyncs a group.
    //
    private static async Task<ModListResolution> ResolveAsync(
        IReadOnlyList<ModListAction> fetches,
        IReadOnlyList<ModListCandidate> installed,
        CancellationToken ct)
    {
        var catalog = AppServices.ModCache.AllMods
            .GroupBy(m => m.Id)
            .ToDictionary(g => g.Key, g => g.First());

        //
        // Which parent mods an addon in this batch can rely on: everything already installed, plus
        // everything this same apply is about to install. An addon whose parent is in neither is
        // refused here rather than downloaded - it would install files nothing loads, and the list
        // would look applied when it wasn't.
        //
        var parents = installed
            .Where(c => c is { IsAddon: false, ModId: not null })
            .Select(c => c.ModId!.Value)
            .ToHashSet();

        foreach (var action in fetches.Where(a => a is { IsAddon: false, ModId: not null }))
            parents.Add(action.ModId!.Value);

        var ready = new List<ModListDownload>();
        var changes = new List<ModListVersionChange>();
        var unavailable = new List<ModListFetchFailure>();

        foreach (var action in fetches)
        {
            if (action.ModId is not { } modId)
            {
                unavailable.Add(new ModListFetchFailure(action.Name, "no sp-mod.com listing to download from"));
                continue;
            }

            InstallTarget target;

            if (action.IsAddon)
            {
                if (AppServices.Addons.ById(modId) is not { } addon)
                {
                    unavailable.Add(new ModListFetchFailure(action.Name, "no sp-mod.com addon listing to download from"));
                    continue;
                }

                if (addon.ModId is { } parentId && !parents.Contains(parentId))
                {
                    var parentName = catalog.GetValueOrDefault(parentId)?.Name ?? $"mod {parentId}";
                    unavailable.Add(new ModListFetchFailure(
                        action.Name, $"it is an addon for {parentName}, which isn't installed and isn't on this list"));
                    continue;
                }

                target = InstallTarget.For(addon);
            }
            else
            {
                if (!catalog.TryGetValue(modId, out var mod))
                {
                    unavailable.Add(new ModListFetchFailure(action.Name, "no sp-mod.com listing to download from"));
                    continue;
                }

                target = InstallTarget.For(mod);
            }

            var id = modId.ToString();
            var wanted = action.TargetVersion?.Trim();

            // A list entry with no version string at all is the one case where newest is the only
            // thing it can mean, so it needs no asking.
            if (string.IsNullOrWhiteSpace(wanted))
            {
                var newest = await NewestAsync(id, action.IsAddon, ct);

                if (newest is null) unavailable.Add(new ModListFetchFailure(action.Name, "it has no published versions"));
                else ready.Add(new ModListDownload(action, target, newest, IsSubstitute: false));

                continue;
            }

            var published = await VersionsAsync(id, action.IsAddon, wanted, ct);

            var exact = published.FirstOrDefault(v => action.VersionId is not null && v.Id == action.VersionId)
                ?? published.FirstOrDefault(v => string.Equals(v.Version?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
            {
                ready.Add(new ModListDownload(action, target, exact, IsSubstitute: false));
                continue;
            }

            var replacement = await NewestAsync(id, action.IsAddon, ct);

            if (replacement is null)
                unavailable.Add(new ModListFetchFailure(action.Name, $"version {wanted} is gone and nothing else is published"));
            else
                changes.Add(new ModListVersionChange(action, target, wanted, replacement));
        }

        return new ModListResolution(ready, changes, unavailable);
    }

    //
    // The published versions matching a wanted version string. Addons are asked live like mods
    // rather than read from their cache: the cache only carries each addon's six most recent
    // versions, and a list can pin one older than that.
    //
    private static async Task<IReadOnlyList<ModVersion>> VersionsAsync(
        string id, bool isAddon, string wanted, CancellationToken ct)
    {
        if (!isAddon)
        {
            var mods = await AppServices.SpModApi.GetModVersionsAsync(
                id, new ModVersionsQuery { FilterVersion = wanted, PerPage = 5 }, ct);
            return mods.Data;
        }

        var addons = await AppServices.SpModApi.GetAddonVersionsAsync(
            id, new AddonVersionsQuery { FilterVersion = wanted, PerPage = 5 }, ct);
        return [.. addons.Data.Select(AsModVersion)];
    }

    private static async Task<ModVersion?> NewestAsync(string id, bool isAddon, CancellationToken ct)
    {
        if (!isAddon)
        {
            var mods = await AppServices.SpModApi.GetModVersionsAsync(id, new ModVersionsQuery { PerPage = 5 }, ct);
            return mods.Data.FirstOrDefault();
        }

        var addons = await AppServices.SpModApi.GetAddonVersionsAsync(
            id, new AddonVersionsQuery { Sort = "-published_at", PerPage = 5 }, ct);
        return addons.Data.Select(AsModVersion).FirstOrDefault();
    }

    //
    // An addon version carries everything the download pipeline reads. ModVersionConstraint is
    // dropped deliberately: it decides which version to take, and by here that is already decided.
    //
    private static ModVersion AsModVersion(AddonVersion version) => new()
    {
        Id = version.Id,
        Version = version.Version,
        Description = version.Description,
        Link = version.Link,
        ContentLength = version.ContentLength,
        Downloads = version.Downloads,
        PublishedAt = version.PublishedAt,
    };

    //
    // Completes once the queue has finished with this item, whatever the outcome.
    //
    // The item can finish between the first check and the handler being attached, so the check is
    // repeated afterwards - a queued mod that installs quickly would otherwise wait forever.
    //
    private static Task<DownloadQueueItemStatus> WaitForAsync(DownloadQueueItemViewModel item)
    {
        if (item.IsFinished) return Task.FromResult(item.Status);

        var completion = new TaskCompletionSource<DownloadQueueItemStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(item.Status) || !item.IsFinished) return;

            item.PropertyChanged -= OnChanged;
            completion.TrySetResult(item.Status);
        }

        item.PropertyChanged += OnChanged;

        if (item.IsFinished)
        {
            item.PropertyChanged -= OnChanged;
            completion.TrySetResult(item.Status);
        }

        return completion.Task;
    }

    // The queue's list is bound to the Downloads page, so it is only ever touched on the UI thread.
    private static Task<DownloadQueueItemViewModel> EnqueueAsync(
        InstallTarget target,
        string versionLabel,
        string installPath,
        Func<Task<ModVersion?>> resolveVersion,
        long? totalBytes)
    {
        // The size is passed in rather than left for the worker to discover: every version is
        // already resolved by this point, so the whole apply can be sized before it starts.
        DownloadQueueItemViewModel Enqueue() =>
            AppServices.DownloadQueue.Enqueue(target, versionLabel, installPath, resolveVersion, totalBytes: totalBytes);

        var dispatcher = Application.Current?.Dispatcher;

        return dispatcher is null || dispatcher.CheckAccess()
            ? Task.FromResult(Enqueue())
            : dispatcher.InvokeAsync(Enqueue).Task;
    }

    private static void CancelAll(IEnumerable<DownloadQueueItemViewModel> items)
    {
        void Cancel()
        {
            foreach (var item in items.Where(i => i.CanCancel)) item.CancelCommand.Execute(null);
        }

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess()) Cancel();
        else dispatcher.InvokeAsync(Cancel);
    }

    //
    // Pins a version id from the catalog's own embedded version list, so capture never touches the
    // network. A catalog Mod carries only its six most recent versions, so anything older resolves
    // to a mod id and a version string without an id - which the planner and applier both handle.
    //
    private static ModListCapture.VersionLookup CatalogVersions()
    {
        var byId = AppServices.ModCache.AllMods
            .Where(m => m.Versions is { Count: > 0 })
            .GroupBy(m => m.Id)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ModVersionSummary>?)g.First().Versions);

        return modId => byId.TryGetValue(modId, out var versions) ? versions : null;
    }

    // The same for addons, off the addon cache. Also offline - the cache carries each addon's
    // versions, so a capture of an installed addon pins its version id without a lookup.
    private static ModListCapture.AddonVersionLookup CatalogAddonVersions()
    {
        var byId = AppServices.Addons.AllAddons
            .Where(a => a.Versions is { Count: > 0 })
            .GroupBy(a => a.Id)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AddonVersionSummary>?)g.First().Versions);

        return addonId => byId.TryGetValue(addonId, out var versions) ? versions : null;
    }
}
