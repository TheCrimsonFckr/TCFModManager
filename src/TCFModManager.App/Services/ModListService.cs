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

        var records = AppServices.InstallManifest.Load().Mods;
        var catalog = AppServices.ModCache.AllMods;
        var sptVersion = AppServices.SptEnvironment.InstalledVersion;

        var candidates = await Task.Run(() =>
        {
            var found = InstalledModScanner.Scan(installPath);

            return ModListCandidates.From(
                InstalledModCardViewModel.BuildFrom(found, catalog, sptVersion, records));
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
            includeDisabled: includeDisabled);

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
    public async Task<ModListApplyResult> ApplyAsync(
        ModListPreview preview,
        bool takeSnapshot = true,
        CancellationToken ct = default)
    {
        var result = await ModListApplier.ApplyAsync(
            preview.Plan,
            preview.Install.Candidates,
            (fetches, token) => FetchAsync(preview.Install.InstallPath, fetches, token),
            new ModListApplyOptions
            {
                SnapshotName = takeSnapshot ? $"Before {preview.List.Name}" : null,
                SnapshotVersions = CatalogVersions(),
                SptVersion = preview.Install.SptVersion,
            },
            ct: ct);

        if (result.Snapshot is not null) AppServices.ModLists.Add(result.Snapshot);
        if (result.Completed) AppServices.ModLists.SetActive(preview.List.Id);

        return result;
    }

    //
    // Queues every fetch the plan asked for and waits for all of them.
    //
    // Everything is enqueued first and awaited afterwards, so the queue's own worker decides the
    // order and the user sees the whole batch on the Downloads page at once rather than one card
    // appearing at a time.
    //
    private async Task<ModListFetchOutcome> FetchAsync(
        string installPath,
        IReadOnlyList<ModListAction> fetches,
        CancellationToken ct)
    {
        var catalog = AppServices.ModCache.AllMods
            .GroupBy(m => m.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var fetched = new List<ModListAction>();
        var failed = new List<ModListFetchFailure>();
        var waiting = new List<(ModListAction Action, DownloadQueueItemViewModel Item)>();

        foreach (var action in fetches)
        {
            if (action.ModId is not { } modId || !catalog.TryGetValue(modId, out var mod))
            {
                failed.Add(new ModListFetchFailure(action.Name, "no sp-mod.com listing to download from"));
                continue;
            }

            var item = await EnqueueAsync(
                mod,
                action.TargetVersion ?? "latest",
                installPath,
                () => ResolveVersionAsync(modId, action));

            waiting.Add((action, item));
        }

        using var cancelling = ct.Register(() => CancelAll(waiting.Select(w => w.Item)));

        var cancelled = false;

        foreach (var (action, item) in waiting)
        {
            switch (await WaitForAsync(item))
            {
                case DownloadQueueItemStatus.Completed:
                    fetched.Add(action);
                    break;

                case DownloadQueueItemStatus.Cancelled:
                    cancelled = true;
                    break;

                default:
                    failed.Add(new ModListFetchFailure(action.Name, item.StatusMessage));
                    break;
            }
        }

        return new ModListFetchOutcome(fetched, failed, cancelled);
    }

    //
    // Resolves the version the list actually names, and only that one.
    //
    // A pinned version that is no longer published deliberately fails rather than quietly
    // installing the newest instead - substituting a different build than the list asked for is a
    // decision for the user, not for this. A list entry with no version string at all is the one
    // case where newest is the only thing it can mean.
    //
    private static async Task<ModVersion?> ResolveVersionAsync(int modId, ModListAction action)
    {
        var id = modId.ToString();

        if (string.IsNullOrWhiteSpace(action.TargetVersion))
        {
            var newest = await AppServices.SpModApi.GetModVersionsAsync(id, new ModVersionsQuery { PerPage = 5 });
            return newest.Data.FirstOrDefault();
        }

        var wanted = action.TargetVersion.Trim();

        var page = await AppServices.SpModApi.GetModVersionsAsync(
            id,
            new ModVersionsQuery { FilterVersion = wanted, PerPage = 5 });

        return page.Data.FirstOrDefault(v => action.VersionId is not null && v.Id == action.VersionId)
            ?? page.Data.FirstOrDefault(v => string.Equals(v.Version?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

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
        Mod mod,
        string versionLabel,
        string installPath,
        Func<Task<ModVersion?>> resolveVersion)
    {
        DownloadQueueItemViewModel Enqueue() =>
            AppServices.DownloadQueue.Enqueue(mod, versionLabel, installPath, resolveVersion);

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
}
