using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Services;

// One read of the install, reduced to the shape every Core mod-list piece takes.
public sealed record ModListInstall(
    string InstallPath,
    IReadOnlyList<ModListCandidate> Candidates,
    string? SptVersion,
    IReadOnlyDictionary<string, IReadOnlySet<string>>? FolderNeeds = null);

//
// A list, what applying it would do, and the install both were worked out against.
//
// Held together on purpose: the plan shown to the user and the plan that runs have to be the same
// one, worked out against the same scan, or a rescan between the two silently changes what happens.
//
public sealed record ModListPreview(ModList List, ModListPlan Plan, ModListInstall Install)
{
    // OPEN-22: the dependencies the list's mods need that it doesn't name. Null when not asked.
    public ModListDependencyCheck? Dependencies { get; init; }

    // OPEN-44 Q18 (e): the served list planned here the way a player receives it.
    public bool AsPlayer { get; init; }
}

//
// What sp-mod said the list's mods need (OPEN-22), and which of those the user has unticked.
// Problem is set when sp-mod couldn't be asked: the apply is still allowed (Q5), and says so.
// AddToList: also store the ticked ones on the list (Q1) - only ever offered for your own lists.
//
public sealed record ModListDependencyCheck(
    IReadOnlyList<ModListDependencyOffer> Offers,
    IReadOnlyList<SpModDependency> NoVersion,
    IReadOnlySet<int> Unticked,
    string? Problem,
    bool AddToList = false)
{
    public static ModListDependencyCheck Failed(string problem) => new([], [], new HashSet<int>(), problem);

    public IEnumerable<ModListDependencyOffer> Ticked => Offers.Where(o => !Unticked.Contains(o.Dependency.ModId));

    public IReadOnlyList<ModListDependencyPick> Picks => Ticked.Select(o => o.Pick).ToList();
}

// OPEN-69 Q4: how far checking a list's versions with sp-mod has got, or how long sp-mod asked to wait.
public sealed record ModListCheckProgress(int Done, int Total, TimeSpan? Wait = null);

//
// Monitor mode's say in a list apply (§9, R6): whether its installs and updates are saved for the
// user to install instead, and the subfolder a list's downloads go into. Enables and disables run
// either way - they move folders inside the install and place nothing new.
//
public sealed record ModListDownloadMode(bool DownloadOnly, string? Subfolder)
{
    public static ModListDownloadMode Install { get; } = new(false, null);

    // Read at the moment it is needed, so a list applied just after the Options setting changed
    // follows the new setting.
    public static ModListDownloadMode For(ModList list)
    {
        var monitor = new SettingsService().Load().Monitor;
        if (monitor.InstallMode != InstallMode.DownloadOnly) return Install;

        return new ModListDownloadMode(true, monitor.DownloadListSubfolders ? list.Name : null);
    }
}

//
// One mod the add-a-mod picker can put on a list: the entry a list would store for it, plus the
// couple of facts the picker labels it with that the entry itself doesn't carry.
//
public sealed record ModListAddOption
{
    public required ModListEntry Entry { get; init; }

    // Installed here, but currently sitting in the disabled folder.
    public bool IsDisabled { get; init; }

    // For a catalog listing, whether this install already has it. Always true for an installed one.
    public bool IsInstalled { get; init; }

    // The catalog listing's author, for telling two mods with similar names apart.
    public string? Author { get; init; }

    public DateTimeOffset? InstalledAt { get; init; }

    public Guid? GroupId { get; init; }

    public int? CategoryId { get; init; }

    public int? Downloads { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public bool FitsInstalledSpt { get; init; } = true;
}

// What the picker can offer, split by where it came from.
public sealed record ModListAddOptions(
    IReadOnlyList<ModListAddOption> Installed,
    IReadOnlyList<ModListAddOption> Catalog);

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
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

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

        var (candidates, needs) = await Task.Run(() =>
        {
            var found = InstalledScan.Run(installPath, catalog, records);

            var cards = InstalledModCardViewModel.BuildFrom(
                found, catalog, sptVersion, records, AppServices.Addons.AllAddons);

            var candidates = ModListCandidates.From(
                cards, records, onDisk: r => r.Files.Any(f => DisabledModPaths.OnDisk(installPath, f)));

            return (candidates, FolderNeedsOf(found));
        });

        return new ModListInstall(installPath, candidates, sptVersion, needs);
    }

    // Each installed mod folder's name, with the folder names of what it declares it needs.
    private static Dictionary<string, IReadOnlySet<string>> FolderNeedsOf(IReadOnlyList<InstalledMod> found)
    {
        var graph = ModDependencyGraph.Build(found);
        var needs = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in found)
        {
            var folders = graph.DependenciesOf(mod)
                .Select(l => Path.GetFileName(l.Dependency.FolderPath))
                .Where(f => !string.IsNullOrEmpty(f) && !f.Equals(Path.GetFileName(mod.FolderPath), StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (folders.Count == 0) continue;

            var name = Path.GetFileName(mod.FolderPath);
            if (needs.TryGetValue(name, out var already)) folders.UnionWith(already);
            needs[name] = folders;
        }

        return needs;
    }

    // Downloads put so each installs after what it needs among them: a dependency the plan added
    // before what needs it, and an installed mod after the installed mods it declares.
    private static List<ModListDownload> InDependencyOrder(IReadOnlyList<ModListDownload> downloads, ModListInstall install)
    {
        var folderNeeds = install.FolderNeeds ?? new Dictionary<string, IReadOnlySet<string>>();

        IEnumerable<int> Needs(int i)
        {
            var action = downloads[i].Action;
            var mine = action.Installed?.Folders ?? [];
            var wanted = mine.SelectMany(f => folderNeeds.TryGetValue(f, out var n) ? n : Enumerable.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var j = 0; j < downloads.Count; j++)
            {
                if (j == i) continue;
                var other = downloads[j].Action;
                if (other.IsDependency && other.NeededBy.Contains(action.Name, StringComparer.OrdinalIgnoreCase)) yield return j;
                else if (wanted.Count > 0 && other.Installed?.Folders.Any(wanted.Contains) == true) yield return j;
            }
        }

        return [.. DependencyOrdering.Order(downloads, Needs)];
    }

    //
    // Re-reads the installed version of every mod on a list.
    //
    // Returns null when there is no SPT install folder to read. The entries come back changed but
    // NOT stored - the caller puts them in the edit buffer, so a refresh is reviewed and saved like
    // any other edit rather than rewriting a list behind the user's back.
    //
    public async Task<ModListRefreshResult?> RefreshVersionsAsync(IEnumerable<ModListEntry> entries, bool onlyNewer = false)
    {
        var install = await ReadInstallAsync();
        if (install is null) return null;

        return ModListRefresh.Build(entries, install.Candidates, CatalogVersions(), CatalogAddonVersions(), onlyNewer);
    }

    // OPEN-44 Q4: the server-only mods the Server Map host reports enabled. Empty when it can't be asked.
    private static async Task<IReadOnlySet<int>> ServerOnlyOnHostAsync()
    {
        var map = new SettingsService().Load().ServerMap;
        if (string.IsNullOrWhiteSpace(map.Host)) return new HashSet<int>();

        try
        {
            using var client = ServerMapClient.TryCreate(map.ToEndpoint());
            if (client is null) return new HashSet<int>();

            var result = await client.ClientsAsync(map.ClientId);
            return result.Succeeded ? ServerMapMachines.ServerOnlyOnHost(result.Machines) : new HashSet<int>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            AppLog.Warn("ModLists", $"couldn't read the Server Map host's mods: {ex.Message}");
            return new HashSet<int>();
        }
    }

    // OPEN-44 Q10 (b): on a dedicated headless, the entries of the lists it follows (its own active list, the served one).
    private static IReadOnlyList<ModListEntry>? FollowedOnDedicatedHeadless()
    {
        if (!InstallRole.IsDedicatedHeadless(MachineScope)) return null;

        return [.. (AppServices.ModLists.GetActive()?.Entries ?? []).Concat(AppServices.ModLists.GetActiveServer()?.Entries ?? [])];
    }

    // OPEN-44 P8: an own list this install follows - its active list, or the one it publishes.
    public static bool KeepsNewer(ModList list) =>
        list.IsEditable
        && (AppServices.ModLists.GetActive()?.Id == list.Id || AppServices.ModLists.GetPublished()?.Id == list.Id);

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
            addonVersions: CatalogAddonVersions(),
            followed: FollowedOnDedicatedHeadless());

        return AppServices.ModLists.Add(list);
    }

    //
    // What this machine is, as the scopes a served list gets filtered to.
    //
    // Read here rather than passed in, because every caller would read the same setting and one that
    // forgot would plan a headless box as an ordinary player - stripping it of the bot and item mods
    // it is hosting the raid with, which is a failure everyone in that raid feels and nobody can see
    // the cause of.
    //
    // Only ever narrows a list a SERVER served. Your own lists describe your own install.
    //
    public ModList CreateEmpty(string name) =>
        AppServices.ModLists.Add(ModListCapture.Build(
            name, [], DateTimeOffset.UtcNow, sptVersion: AppServices.SptEnvironment.InstalledVersion));

    // OPEN-44 Q13: hosting rides on the scope as the Server flag, as the Map's ScopeFor does.
    public static ModListEntryScope MachineScope =>
        InstallRole.ScopeFor(new SettingsService().Load().Roles)
        | (ServerMapReporting.HostsServerMap(AppServices.SptEnvironment.InstallPath) ? ModListEntryScope.Server : 0);

    // OPEN-44 Q13 (b): on a headless that does not host, the mods a served or narrowed list leaves
    // out whose author's page says the headless needs them - from the advice cache, never fetched here.
    private static IReadOnlySet<int>? AlsoTakes(ModList list, ModListEntryScope machine)
    {
        if (!InstallRole.IsDedicatedHeadless(machine) || machine.HasFlag(ModListEntryScope.Server)) return null;

        var taken = new HashSet<int>();

        foreach (var entry in list.Entries.Where(e => e is { IsAddon: false, ModId: not null }))
        {
            if (ModList.ScopeApplies(entry.EffectiveScope, machine)) continue;
            if (AppServices.HeadlessAdvice.Known(HeadlessSubject.ForMod(entry.ModId!.Value))?.Verdict == HeadlessVerdict.Needs)
                taken.Add(entry.ModId!.Value);
        }

        return taken.Count == 0 ? null : taken;
    }

    //
    // Works out what applying this list would do. Nothing moves and nothing downloads.
    //
    // withDependencies (OPEN-22) also asks sp-mod what the list's mods need that it doesn't name, and
    // plans those in, all ticked - the Mod lists page asks; the pre-launch check, which only compares,
    // doesn't.
    //
    public async Task<ModListPreview?> PreviewAsync(ModList list, bool withDependencies = false, bool asPlayer = false)
    {
        var install = await ReadInstallAsync();
        if (install is null) return null;

        var preview = Plan(list, install, null, asPlayer);
        if (!withDependencies) return preview;

        var check = await DependenciesAsync(asPlayer ? ServerMapMachines.AsServed(list) : list, install, preview.Plan);
        return Plan(list, install, check, asPlayer);
    }

    // OPEN-44 Q18: the list this install publishes, on the machine that runs its server.
    public static bool IsServedHere(ModList list) =>
        list.IsEditable
        && AppServices.ModLists.GetPublished()?.Id == list.Id
        && ServerMapReporting.HostsServerMap(AppServices.SptEnvironment.InstallPath);

    // OPEN-44 Q18 (b): entries the install holds newer than the list names.
    public static ModListRefreshResult NewerHere(IEnumerable<ModListEntry> entries, ModListInstall install) =>
        ModListRefresh.Build(entries, install.Candidates, CatalogVersions(), CatalogAddonVersions(), onlyNewer: true);

    //
    // The same preview planned again with other dependencies ticked, or the add-to-list choice
    // changed - no rescan and no second question to sp-mod, so the rows don't move under the user.
    //
    public static ModListPreview Replan(ModListPreview preview, ModListDependencyCheck check) =>
        Plan(preview.List, preview.Install, check, preview.AsPlayer);

    private static ModListPreview Plan(ModList list, ModListInstall install, ModListDependencyCheck? check, bool asPlayer = false)
    {
        //
        // A personal list is planned knowing what the followed server also requires, so its
        // Exclusive sweep spares those mods. Passed as null when planning the server list itself -
        // it does not need protecting from its own plan, and a served list never disables anyway.
        //
        var planned = asPlayer ? ServerMapMachines.AsServed(list) : list;
        var serverList = planned.Origin == ModListOrigin.Server ? null : AppServices.ModLists.GetActiveServer();

        var machine = MachineScope;

        return new ModListPreview(
            list,
            ModListPlanner.Build(planned, install.Candidates, AppServices.ModLists.GetPins(), serverList, machine, check?.Picks,
                AlsoTakes(planned, machine), asPlayer || KeepsNewer(list)),
            install)
        {
            Dependencies = check,
            AsPlayer = asPlayer,
        };
    }

    //
    // Asks sp-mod for the dependencies of every entry the list applies here, at the install's SPT,
    // and offers the ones applying the list would leave out (ModListDependencies.Offer). A failure is
    // a Problem on the check, never an exception: the list can still be applied (Q5).
    //
    private static async Task<ModListDependencyCheck> DependenciesAsync(ModList list, ModListInstall install, ModListPlan planWithout)
    {
        if (install.SptVersion is not { Length: > 0 } spt) return ModListDependencyCheck.Failed(Strings.ModLists_DepsUnreachable);

        var machine = MachineScope;
        var entries = ModListDependencies.Askable(list, machine, AlsoTakes(list, machine));

        var applyingHere = list.EntriesApplyingTo(machine, AlsoTakes(list, machine))
            .Where(e => e is { IsAddon: false, ModId: not null })
            .Select(e => e.ModId!.Value)
            .ToHashSet();
        var placedElsewhere = list.Entries
            .Where(e => e is { IsAddon: false, ModId: not null } && !applyingHere.Contains(e.ModId!.Value))
            .Select(e => e.ModId!.Value)
            .ToHashSet();

        SpModDependencies answer;
        try
        {
            answer = entries.Count == 0
                ? new SpModDependencies(spt, [], [])
                : await SpModListResolver.MissingDependenciesAsync(
                    entries,
                    AppServices.Addons.AllAddons.Where(a => a.ModId is not null).GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First().ModId!.Value),
                    spt,
                    new SpModListApi(AppServices.SpModApi),
                    placedElsewhere: placedElsewhere);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SpModApiException)
        {
            AppLog.Warn("ModLists", $"dependency check for '{list.Name}' failed: {ex.Message}");
            return ModListDependencyCheck.Failed(Strings.ModLists_DepsUnreachable);
        }

        if (!answer.Succeeded)
        {
            AppLog.Warn("ModLists", $"dependency check for '{list.Name}' failed: {answer.Failure} {answer.Detail}");
            return ModListDependencyCheck.Failed(Strings.ModLists_DepsUnreachable);
        }

        if (list.Origin == ModListOrigin.Server && answer.Missing.Count > 0)
        {
            var metOnHost = await ServerOnlyOnHostAsync();
            var kept = ModListDependencies.WithoutMetOnHost(answer.Missing, metOnHost);

            if (kept.Count != answer.Missing.Count)
            {
                AppLog.Info("ModLists", $"dependency check for '{list.Name}': {answer.Missing.Count - kept.Count} met by server-only mods on the host");
                answer = answer with { Missing = kept };
            }
        }

        var parents = ModListDependencies.MissingParents(
            [.. list.EntriesApplyingTo(machine, AlsoTakes(list, machine))],
            AppServices.Addons.AllAddons.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First()),
            AppServices.ModCache.AllMods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First()),
            spt,
            answer.Missing.Select(d => d.ModId).ToHashSet(),
            list.Entries);

        if (parents.Count > 0) answer = answer with { Missing = [.. parents, .. answer.Missing] };

        var (offers, noVersion) = ModListDependencies.Offer(answer, install.Candidates, planWithout);
        AppLog.Info("ModLists", $"dependency check for '{list.Name}': {offers.Count} offered, {noVersion.Count} with no version for SPT {spt}");

        return new ModListDependencyCheck(offers, noVersion, new HashSet<int>(), null);
    }

    //
    // A copy of the SPT profiles before a list changes the install, when they changed since the last
    // one (OPEN-12 F4) - one copy for the whole apply. Not while SPT runs from the install: the apply
    // is refused then anyway.
    //
    private static Task BackUpProfilesAsync(string installPath) =>
        ModInstallService.RunningBlockers(installPath).Count > 0
            ? Task.CompletedTask
            : Task.Run(() => AppServices.ProfileBackups.BackupIfChanged(installPath, ProfileBackups.BeforeList));

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
        CancellationToken ct = default,
        IProgress<ModListCheckProgress>? checking = null)
    {
        prompts ??= ModListPrompts.Reject;

        var mode = ModListDownloadMode.For(preview.List);

        await BackUpProfilesAsync(preview.Install.InstallPath);

        var result = await ModListApplier.ApplyAsync(
            preview.Plan,
            preview.Install.Candidates,
            (fetches, token) => FetchAsync(preview.List, preview.Install, fetches, prompts, mode, checking, token),
            new ModListApplyOptions
            {
                // Named after the list being applied, not "Before X" - the snapshot is only ever
                // shown on the revert button, which words it, and a stored "Before ..." was what
                // grew another "Before " every time one got applied.
                SnapshotName = takeSnapshot ? preview.List.Name : null,
                SnapshotVersions = CatalogVersions(),
                SnapshotAddonVersions = CatalogAddonVersions(),
                SptVersion = preview.Install.SptVersion,
                InstallPath = preview.Install.InstallPath,
            },
            ct: ct);

        if (result.Snapshot is not null) AppServices.ModLists.SetSnapshot(result.Snapshot);

        if (result.Completed && !preview.AsPlayer)
        {
            // Q1: the ticked dependencies written onto the list too, when asked - your own lists only.
            if (preview.Dependencies is { AddToList: true } deps && preview.List.IsEditable && deps.Picks.Count > 0)
            {
                AppServices.ModLists.ReplaceEntries(preview.List.Id, ModListDependencies.WithAdded(preview.List, deps.Picks, DateTimeOffset.UtcNow));
                AppLog.Info("ModLists", $"added {deps.Picks.Count} dependencies to '{preview.List.Name}'");
            }

            //
            // Into whichever slot it belongs to. Following a server must not cost the user the
            // personal list they were already following, and vice versa - see ModListData.
            //
            if (preview.List.Origin == ModListOrigin.Server)
                AppServices.ModLists.SetActiveServer(preview.List.Id);
            else
                AppServices.ModLists.SetActive(preview.List.Id);

            // The revision counts applies, not edits - see ModListStore.BumpRevision. An apply that
            // stopped part way is not one: it is unwound, and the install ends up where it started.
            AppServices.ModLists.BumpRevision(preview.List.Id);

            await LearnScopesAsync(preview.List);
        }

        return result;
    }

    //
    // An sp-mod list can't say which machines each mod is for until its files are on disk, so once an
    // apply has put them there the install is read again and what it shows is stored on the list (see
    // ModListScopeLearning). A failure here costs nothing but the chip staying as it was, so it is
    // logged rather than reported.
    //
    private async Task LearnScopesAsync(ModList list)
    {
        if (!ModListScopeLearning.Learns(list)) return;

        try
        {
            var install = await ReadInstallAsync();
            if (install is null) return;

            if (AppServices.ModLists.LearnScopes(list.Id, install.Candidates) is { } learned)
                AppLog.Info("ModLists", $"learned scopes for {learned.Entries.Count(e => e.Scope is not null)} entries of {list.Name}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("ModLists", $"couldn't learn scopes for {list.Name}: {ex.Message}");
        }
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

        await BackUpProfilesAsync(preview.Install.InstallPath);

        var result = await ModListApplier.ApplyAsync(
            preview.Plan,
            preview.Install.Candidates,
            (fetches, token) => FetchAsync(
                snapshot, preview.Install, fetches, prompts ?? ModListPrompts.Reject, ModListDownloadMode.For(snapshot), null, token),
            new ModListApplyOptions
            {
                SptVersion = preview.Install.SptVersion,
                InstallPath = preview.Install.InstallPath,
            },
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
        ModList list,
        ModListInstall install,
        IReadOnlyList<ModListAction> fetches,
        ModListPrompts prompts,
        ModListDownloadMode mode,
        IProgress<ModListCheckProgress>? checking,
        CancellationToken ct)
    {
        var resolution = await ResolveAsync(list, fetches, install.Candidates, checking, ct);

        var accepted = prompts.ApproveVersionChanges(resolution.Changes);
        var acceptedActions = accepted.Select(c => c.Action).ToHashSet();

        var failed = new List<ModListFetchFailure>(resolution.Unavailable);

        foreach (var change in resolution.Changes.Where(c => !acceptedActions.Contains(c.Action)))
        {
            failed.Add(new ModListFetchFailure(
                change.Action.Name,
                Text(Strings.ModList_ReasonVersionNotTakenFormat, change.Wanted)));
        }

        var downloads = new List<ModListDownload>(resolution.Ready);
        downloads.AddRange(accepted.Select(c => new ModListDownload(c.Action, c.Target, c.Available, IsSubstitute: true)));

        if (downloads.Count == 0) return new ModListFetchOutcome([], failed, false);

        //
        // Versions that don't support the installed SPT are asked about once for the batch, like a
        // single install. Skipping leaves those out, each named as a failure, and fetches the rest;
        // Cancel stops the batch like declining the mod pages does.
        //
        var incompatible = downloads.Where(d => SptCompatibility.IsIncompatible(d.Version.SptVersionConstraint)).ToList();
        var answer = incompatible.Count > 0 ? prompts.InstallIncompatible(incompatible) : IncompatibleAnswer.InstallAll;

        if (answer == IncompatibleAnswer.Cancel)
            return new ModListFetchOutcome([], failed, Cancelled: true);

        if (answer == IncompatibleAnswer.SkipThese)
        {
            foreach (var d in incompatible)
            {
                failed.Add(new ModListFetchFailure(
                    d.Action.Name,
                    Text(Strings.ModList_ReasonIncompatibleFormat, d.Version.Version, AppServices.SptEnvironment.InstalledVersion)));
            }

            downloads.RemoveAll(incompatible.Contains);
            if (downloads.Count == 0) return new ModListFetchOutcome([], failed, false);
        }

        //
        // The same gate a manual install goes through, asked once for the whole batch rather than
        // once per mod - ConfirmAll is the existing path for exactly this, and it honours the
        // Options switch that turns the gate off.
        //
        if (!prompts.ConfirmModPages(downloads))
            return new ModListFetchOutcome([], failed, Cancelled: true);

        return await QueueAsync(install.InstallPath, InDependencyOrder(downloads, install), failed, mode, ct);
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
        ModListDownloadMode mode,
        CancellationToken ct)
    {
        var waiting = new List<(ModListDownload Download, DownloadQueueItemViewModel Item)>();
        var fetched = new List<ModListAction>();

        foreach (var download in downloads)
        {
            var version = download.Version;

            if (mode.DownloadOnly && AlreadySaved(download.Target, version))
            {
                fetched.Add(download.Action);
                continue;
            }

            var item = await EnqueueAsync(
                download.Target,
                version.Version ?? download.Action.TargetVersion ?? "latest",
                installPath,
                () => Task.FromResult<ModVersion?>(version),
                version.ContentLength,
                mode);

            waiting.Add((download, item));
        }

        using var cancelling = ct.Register(() => CancelAll(waiting.Select(w => w.Item)));

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
    // written. A pinned version that is gone becomes a question, never a silent substitution.
    //
    private static async Task<ModListResolution> ResolveAsync(
        ModList list,
        IReadOnlyList<ModListAction> fetches,
        IReadOnlyList<ModListCandidate> installed,
        IProgress<ModListCheckProgress>? checking,
        CancellationToken ct)
    {
        var context = new ModListVersionContext
        {
            Installed = installed,
            ListEntries = list.Entries,
            Mods = AppServices.ModCache.AllMods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First()),
            Addons = AppServices.Addons.AllAddons.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First()),
            SptVersion = AppServices.SptEnvironment.InstalledVersion,
        };

        var waiting = checking is null ? null : new Relay<TimeSpan>(wait => checking.Report(new ModListCheckProgress(0, 0, wait)));
        var progress = checking is null ? null : new Relay<SpModResolveProgress>(p => checking.Report(new ModListCheckProgress(p.Done, p.Total)));

        var resolved = await ModListVersionResolver.ResolveAsync(
            fetches, context, new SpModListApi(AppServices.SpModApi, waiting), progress, ct);

        return new ModListResolution(
            [.. resolved.Ready.Select(r => new ModListDownload(r.Action, r.Target, r.Version, IsSubstitute: false))],
            [.. resolved.Offers.Select(o => o.DoesNotFitParent
                ? new ModListVersionChange(o.Action, o.Target, o.Wanted, o.Available, o.ParentName ?? Strings.Addon_ParentFallback, o.ParentVersion)
                : new ModListVersionChange(o.Action, o.Target, o.Wanted, o.Available))],
            [.. resolved.Unavailable.Select(u => new ModListFetchFailure(u.Action.Name, ModListProblems.Reason(u)))]);
    }

    private sealed class Relay<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
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

    //
    // A download-only apply of a list the user hasn't finished installing by hand yet: the same
    // version is still pending in the ledger and its archive is still where it was saved. Fetching
    // it again would only leave a "Foo (2).zip" beside the first, every time the list is applied.
    //
    private static bool AlreadySaved(InstallTarget target, ModVersion version)
    {
        if (AppServices.DownloadLedger.Find(target.Id, target.IsAddon) is not
            { State: DownloadState.Pending } saved) return false;

        var same = (version.Id != 0 && saved.VersionId == version.Id)
            || string.Equals(saved.Version, version.Version, StringComparison.Ordinal);

        if (!same || !File.Exists(saved.ArchivePath)) return false;

        AppLog.Info("ModLists", $"{target.Name} {saved.Version} already saved at {saved.ArchivePath}; not fetched again");
        return true;
    }

    // The queue's list is bound to the Downloads page, so it is only ever touched on the UI thread.
    private static Task<DownloadQueueItemViewModel> EnqueueAsync(
        InstallTarget target,
        string versionLabel,
        string installPath,
        Func<Task<ModVersion?>> resolveVersion,
        long? totalBytes,
        ModListDownloadMode mode)
    {
        // The size is passed in rather than left for the worker to discover: every version is
        // already resolved by this point, so the whole apply can be sized before it starts.
        DownloadQueueItemViewModel Enqueue() =>
            AppServices.DownloadQueue.Enqueue(
                target, versionLabel, installPath, resolveVersion, totalBytes: totalBytes,
                downloadOnly: mode.DownloadOnly, downloadSubfolder: mode.Subfolder);

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
    // Everything a list could have added to it by hand: the mods this install has, and the ones
    // sp-mod.com publishes.
    //
    // Both halves are answered from data already here - the scan and the catalog cache - so opening
    // the picker never waits on the network. The catalog half is offered even with no SPT folder
    // set: naming mods for a list you are building for someone else doesn't need an install.
    //
    public async Task<ModListAddOptions> AddOptionsAsync()
    {
        var install = await ReadInstallAsync();
        var installed = install is null ? new List<ModListAddOption>() : InstalledOptions(install);

        return new ModListAddOptions(installed, CatalogOptions(installed));
    }

    //
    // One option per installed mod, pinned to the version that is here - the same entry capturing
    // the whole install would have written for it, produced by the same code, so a mod added by
    // hand and a mod captured are indistinguishable in the list afterwards.
    //
    // Disabled mods are included. Capture leaves them out because it records what is running; this
    // is someone naming a mod deliberately, and a mod they have but have set aside is a perfectly
    // reasonable thing to name.
    //
    private static List<ModListAddOption> InstalledOptions(ModListInstall install)
    {
        var versions = CatalogVersions();
        var addonVersions = CatalogAddonVersions();
        var options = new List<ModListAddOption>();
        var groups = AppServices.ModGroups.Load().Assignments;

        foreach (var candidate in install.Candidates)
        {
            var entry = ModListCapture
                .BuildEntries([candidate], versions, includeDisabled: true, addonVersions)
                .FirstOrDefault();

            if (entry is null) continue;
            if (options.Any(o => ModListEntries.SameMod(o.Entry, entry))) continue;

            options.Add(new ModListAddOption
            {
                Entry = entry,
                IsDisabled = candidate.IsDisabled,
                IsInstalled = true,
                InstalledAt = candidate.InstalledAt,
                GroupId = candidate.Folders
                    .Select(ModGroupStore.KeyFor)
                    .Select(key => groups.TryGetValue(key, out var id) ? id : (Guid?)null)
                    .FirstOrDefault(id => id is not null),
            });
        }

        return [.. options.OrderBy(o => o.Entry.Name, StringComparer.OrdinalIgnoreCase)];
    }

    //
    // One option per catalog listing, unpinned - see ModListEntries.ForCatalogMod for why a mod
    // this install has never had is named without a version.
    //
    // Left in the order the cache holds them, which is most downloaded first, so the picker has
    // something worth showing before anything is typed.
    //
    // This app's own listing is left out for the same reason Browse hides it: it isn't a mod, and a
    // list naming it would try to drop a second copy of the manager into BepInEx\plugins.
    //
    private static List<ModListAddOption> CatalogOptions(IReadOnlyList<ModListAddOption> installed)
    {
        var options = new List<ModListAddOption>();
        var line = InstalledSptLine();

        foreach (var mod in AppServices.ModCache.AllMods)
        {
            if (string.IsNullOrWhiteSpace(mod.Name)) continue;
            if (string.Equals(mod.Id.ToString(), SelfMod.ModId, StringComparison.Ordinal)) continue;

            var entry = ModListEntries.ForCatalogMod(mod.Id, mod.Name!, mod.Guid);

            options.Add(new ModListAddOption
            {
                Entry = entry,
                IsInstalled = installed.Any(o => ModListEntries.SameMod(o.Entry, entry)),
                Author = mod.Owner?.Name,
                CategoryId = mod.CategoryId ?? mod.Category?.Id,
                Downloads = mod.Downloads,
                UpdatedAt = mod.UpdatedAt,
                FitsInstalledSpt = FitsLine(mod, line),
            });
        }

        return options;
    }

    public static (int Major, int Minor)? InstalledSptLine()
    {
        var parts = AppServices.SptEnvironment.InstalledVersion?.Split('.') ?? [];

        return parts.Length >= 2 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor)
            ? (major, minor)
            : null;
    }

    private static bool FitsLine(Mod mod, (int Major, int Minor)? line)
    {
        if (line is not { } wanted) return true;

        var readable = (mod.Versions ?? [])
            .Select(v => v.SptVersionConstraint)
            .Where(c => SptVersionRange.TryParse(c, out _))
            .ToList();

        return readable.Count == 0 || readable.Any(c => SptVersionRange.IntersectsReleaseLine(c, wanted.Major, wanted.Minor));
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
