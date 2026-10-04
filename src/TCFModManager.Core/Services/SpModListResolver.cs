using TCFModManager.Core.Models;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.Services;

// A version sp-mod lists for a mod or addon, with the constraint that decides whether it fits:
// an SPT constraint on a mod version, a parent-mod constraint on an addon version.
public sealed record SpModCandidateVersion(int Id, string Version, string? Constraint);

//
// The sp-mod requests the list resolver makes. An interface so the resolver is testable without a
// network; SpModListApi is the real one.
//
public interface ISpModListApi
{
    // The recent versions of each mod, in one batch. A mod missing from the answer isn't on sp-mod.
    Task<IReadOnlyDictionary<int, IReadOnlyList<SpModCandidateVersion>>> ModVersionsAsync(
        IReadOnlyList<int> modIds, CancellationToken ct);

    // Every version of one mod, for when the recent ones in the batch didn't include a fit.
    Task<IReadOnlyList<SpModCandidateVersion>> AllModVersionsAsync(int modId, CancellationToken ct);

    Task<IReadOnlyDictionary<int, IReadOnlyList<SpModCandidateVersion>>> AddonVersionsAsync(
        IReadOnlyList<int> addonIds, CancellationToken ct);

    // "id:version,..." pairs; the answer is keyed by the same pair strings.
    Task<Dictionary<string, List<DependencyNode>>> ModDependenciesAsync(string pairs, string sptVersion, CancellationToken ct);

    Task<Dictionary<string, List<DependencyNode>>> AddonDependenciesAsync(string pairs, string sptVersion, CancellationToken ct);
}

//
// ISpModListApi over the app's sp-mod client.
//
// Batched so a 250-entry list costs a handful of requests rather than 250: sp-mod allows 300 a
// minute per address, and this shares that budget with everything else the app does. A 429 is
// waited out and retried a couple of times rather than failing the whole read.
//
public sealed class SpModListApi(SpModApiClient api) : ISpModListApi
{
    // sp-mod's page size cap.
    private const int VersionBatch = 50;

    private const int MaxRetries = 3;

    // sp-mod's Retry-After is honoured up to this; the window's Cancel is the way out of a long one.
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(20);

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<SpModCandidateVersion>>> ModVersionsAsync(
        IReadOnlyList<int> modIds, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyList<SpModCandidateVersion>>();

        foreach (var batch in modIds.Distinct().Chunk(VersionBatch))
        {
            var page = await Retrying(() => api.GetModsAsync(new ModsQuery
            {
                FilterId = string.Join(",", batch),
                FilterIncludeLegacy = true,
                Include = "versions",
                PerPage = VersionBatch,
            }, ct), ct).ConfigureAwait(false);

            foreach (var mod in page.Data)
            {
                result[mod.Id] = [.. (mod.Versions ?? [])
                    .Where(v => !string.IsNullOrWhiteSpace(v.Version))
                    .Select(v => new SpModCandidateVersion(v.Id, v.Version!, v.SptVersionConstraint))];
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<SpModCandidateVersion>> AllModVersionsAsync(int modId, CancellationToken ct)
    {
        var page = await Retrying(() => api.GetModVersionsAsync(
            modId.ToString(), new ModVersionsQuery { Sort = "-version", PerPage = VersionBatch }, ct), ct).ConfigureAwait(false);

        return [.. page.Data
            .Where(v => !string.IsNullOrWhiteSpace(v.Version))
            .Select(v => new SpModCandidateVersion(v.Id, v.Version!, v.SptVersionConstraint))];
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<SpModCandidateVersion>>> AddonVersionsAsync(
        IReadOnlyList<int> addonIds, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyList<SpModCandidateVersion>>();

        foreach (var batch in addonIds.Distinct().Chunk(VersionBatch))
        {
            var page = await Retrying(() => api.GetAddonsAsync(new AddonsQuery
            {
                FilterId = string.Join(",", batch),
                Include = "versions",
                PerPage = VersionBatch,
            }, ct), ct).ConfigureAwait(false);

            foreach (var addon in page.Data)
            {
                result[addon.Id] = [.. (addon.Versions ?? [])
                    .Where(v => !string.IsNullOrWhiteSpace(v.Version))
                    .Select(v => new SpModCandidateVersion(v.Id, v.Version!, v.ModVersionConstraint))];
            }
        }

        return result;
    }

    public Task<Dictionary<string, List<DependencyNode>>> ModDependenciesAsync(string pairs, string sptVersion, CancellationToken ct) =>
        Retrying(() => api.GetModDependenciesAsync(pairs, sptVersion, ct), ct);

    public Task<Dictionary<string, List<DependencyNode>>> AddonDependenciesAsync(string pairs, string sptVersion, CancellationToken ct) =>
        Retrying(() => api.GetAddonDependenciesAsync(pairs, sptVersion, ct), ct);

    private static async Task<T> Retrying<T>(Func<Task<T>> call, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (SpModApiRateLimitedException ex) when (attempt < MaxRetries)
            {
                var wait = ex.RetryAfter is { } after && after > TimeSpan.Zero
                    ? (after < MaxWait ? after : MaxWait)
                    : DefaultWait;
                AppLog.Warn("SpModListResolver", $"rate limited by sp-mod, waiting {wait.TotalSeconds:0}s");
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }
        }
    }
}

public enum SpModResolveFailure
{
    // No answer, or an error status, from sp-mod's API.
    Api,

    // The SPT version given isn't one sp-mod knows. Dependencies are resolved against a published
    // release only.
    UnknownSptVersion,
}

// ---- retargeting ----

public enum SpModRetargetOutcome
{
    // Re-pinned to a different version.
    Changed,

    // Already at the version the target SPT gets.
    Unchanged,

    // Nothing published runs on the target SPT. Kept at its old version.
    NoVersion,

    // An addon whose parent mod isn't on the list, so there's no parent version to fit it to. Kept.
    ParentUnknown,

    // No longer on sp-mod. Kept.
    NotFound,

    // No mod or addon id, or no version, to work from. Kept.
    Skipped,
}

public sealed record SpModRetargetRow(ModListEntry From, ModListEntry To, SpModRetargetOutcome Outcome);

public sealed record SpModRetarget(
    string TargetSptVersion,
    IReadOnlyList<SpModRetargetRow> Rows,
    SpModResolveFailure? Failure = null,
    string? Detail = null)
{
    public bool Succeeded => Failure is null;

    // The list's entries after the retarget, in their original order.
    public IReadOnlyList<ModListEntry> Entries => [.. Rows.Select(r => r.To)];

    public IEnumerable<SpModRetargetRow> Changed => Rows.Where(r => r.Outcome == SpModRetargetOutcome.Changed);

    public IEnumerable<SpModRetargetRow> NoVersion => Rows.Where(r => r.Outcome == SpModRetargetOutcome.NoVersion);
}

// ---- dependencies ----

//
// A mod something on the list needs that the list doesn't have.
//
// NeededBy names what requires it, the entry's name for a direct dependency and the dependency's
// own name further down. IsParent marks a mod that is an addon's parent: sp-mod reports an
// addon's parent among its dependencies, and the import window shows those separately (R4).
//
public sealed record SpModDependency(
    int ModId,
    string Name,
    string? Version,
    int? VersionId,
    IReadOnlyList<string> NeededBy,
    bool IsParent,
    bool Conflict)
{
    // Nothing published satisfies the requirement on this SPT, so it can't be added.
    public bool HasVersion => Version is not null;

    public ModListEntry ToEntry() => new() { Name = Name, ModId = ModId, Version = Version, VersionId = VersionId };
}

public sealed record SpModDependencies(
    string SptVersion,
    IReadOnlyList<SpModDependency> Missing,
    IReadOnlyList<ModListEntry> NotChecked,
    SpModResolveFailure? Failure = null,
    string? Detail = null)
{
    public bool Succeeded => Failure is null;

    public IEnumerable<SpModDependency> Addable => Missing.Where(d => d.HasVersion);
}

public readonly record struct SpModResolveProgress(int Done, int Total);

//
// What the import window asks sp-mod about a list before it is stored: which versions run on
// this install's SPT (retarget), and which mods the list needs but doesn't name (dependencies).
//
// Nothing here writes anything. Both return what they found as data and leave the deciding to the
// window.
//
public static class SpModListResolver
{
    private const string Area = "SpModListResolver";

    // Pairs per dependency request. The answer is keyed by the pair, so the URL is the only limit;
    // the Dependencies page uses the same size.
    private const int DependencyBatch = 25;

    //
    // Re-pins every entry to its newest version for targetSpt.
    //
    // A mod takes its newest version whose SPT constraint the target satisfies - the same matcher
    // Browse and Apply use, so a retargeted list agrees with what the rest of the app calls
    // compatible. An addon takes its newest version whose parent-mod constraint the parent's NEW
    // version satisfies, so addons are done after mods. parents maps addon id to parent mod id, from
    // the page.
    //
    // Entries are never dropped. One with no fitting version keeps the version it had and is marked
    // NoVersion, for the window to leave unticked.
    //
    public static async Task<SpModRetarget> RetargetAsync(
        IReadOnlyList<ModListEntry> entries,
        IReadOnlyDictionary<int, int> parents,
        string targetSpt,
        ISpModListApi api,
        IProgress<SpModResolveProgress>? progress = null,
        CancellationToken ct = default)
    {
        var rows = new SpModRetargetRow[entries.Count];
        var mods = new List<int>();
        var addons = new List<int>();

        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.ModId is not { } id || string.IsNullOrWhiteSpace(e.Version))
            {
                rows[i] = new SpModRetargetRow(e, e, SpModRetargetOutcome.Skipped);
                continue;
            }

            (e.IsAddon ? addons : mods).Add(id);
        }

        var total = mods.Count + addons.Count;
        var done = 0;
        progress?.Report(new SpModResolveProgress(0, total));

        try
        {
            var modVersions = mods.Count == 0
                ? new Dictionary<int, IReadOnlyList<SpModCandidateVersion>>()
                : await api.ModVersionsAsync(mods, ct).ConfigureAwait(false);

            var newModVersion = new Dictionary<int, string>();

            //
            // The batch embeds only each mod's most recent versions, up to a cap sp-mod sets. A mod
            // with fewer than the most any mod came back with has had its whole history returned, so
            // asking for "all" of it again would only spend requests - on a 250-entry list that is
            // most of the lookups, and sp-mod allows 300 a minute.
            //
            var embedCap = modVersions.Count == 0 ? 0 : modVersions.Values.Max(v => v.Count);

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (rows[i] is not null || e.IsAddon) continue;

                var id = e.ModId!.Value;

                if (!modVersions.TryGetValue(id, out var candidates))
                {
                    rows[i] = new SpModRetargetRow(e, e, SpModRetargetOutcome.NotFound);
                }
                else
                {
                    var pick = Newest(candidates, c => SptVersionMatcher.IsSatisfiedBy(c.Constraint, targetSpt) == true);

                    // The batch carries only recent versions. An older one may still fit the target.
                    if (pick is null && candidates.Count > 0 && candidates.Count >= embedCap)
                    {
                        pick = Newest(await api.AllModVersionsAsync(id, ct).ConfigureAwait(false),
                            c => SptVersionMatcher.IsSatisfiedBy(c.Constraint, targetSpt) == true);
                    }

                    rows[i] = Row(e, pick);
                }

                newModVersion[id] = rows[i].To.Version!;
                progress?.Report(new SpModResolveProgress(++done, total));
            }

            var addonVersions = addons.Count == 0
                ? new Dictionary<int, IReadOnlyList<SpModCandidateVersion>>()
                : await api.AddonVersionsAsync(addons, ct).ConfigureAwait(false);

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (rows[i] is not null) continue;

                var id = e.ModId!.Value;

                if (!parents.TryGetValue(id, out var parentId) || !newModVersion.TryGetValue(parentId, out var parentVersion))
                {
                    rows[i] = new SpModRetargetRow(e, e, SpModRetargetOutcome.ParentUnknown);
                }
                else if (!addonVersions.TryGetValue(id, out var candidates))
                {
                    rows[i] = new SpModRetargetRow(e, e, SpModRetargetOutcome.NotFound);
                }
                else
                {
                    rows[i] = Row(e, Newest(candidates, c => ModVersionMatcher.IsSatisfiedBy(c.Constraint, parentVersion) == true));
                }

                progress?.Report(new SpModResolveProgress(++done, total));
            }
        }
        catch (Exception ex) when (ex is SpModApiException or HttpRequestException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            AppLog.Warn(Area, $"retarget to SPT {targetSpt} failed: {ex.Message}");
            return new SpModRetarget(targetSpt, [.. entries.Select(e => new SpModRetargetRow(e, e, SpModRetargetOutcome.Skipped))],
                SpModResolveFailure.Api, ex.Message);
        }

        var result = new SpModRetarget(targetSpt, rows);
        AppLog.Info(Area,
            $"retarget to SPT {targetSpt}: {result.Changed.Count()} changed, {result.NoVersion.Count()} with no version, " +
            $"{rows.Count(r => r.Outcome == SpModRetargetOutcome.ParentUnknown)} addons with no parent on the list, " +
            $"{rows.Count(r => r.Outcome == SpModRetargetOutcome.NotFound)} not on sp-mod");

        return result;
    }

    private static SpModRetargetRow Row(ModListEntry entry, SpModCandidateVersion? pick)
    {
        if (pick is null) return new SpModRetargetRow(entry, entry, SpModRetargetOutcome.NoVersion);

        if (string.Equals(pick.Version, entry.Version, StringComparison.OrdinalIgnoreCase))
        {
            return new SpModRetargetRow(entry, entry, SpModRetargetOutcome.Unchanged);
        }

        var to = new ModListEntry
        {
            Name = entry.Name,
            ModId = entry.ModId,
            IsAddon = entry.IsAddon,
            VersionId = pick.Id,
            Version = pick.Version,
            Guid = entry.Guid,
            Folders = entry.Folders,
            Scope = entry.Scope,
        };

        return new SpModRetargetRow(entry, to, SpModRetargetOutcome.Changed);
    }

    // Newest by version number, not by the order sp-mod lists them in. Unparsable versions lose.
    private static SpModCandidateVersion? Newest(IEnumerable<SpModCandidateVersion> candidates, Func<SpModCandidateVersion, bool> fits) =>
        candidates
            .Where(fits)
            .Select(c => (Candidate: c, Parsed: SemanticVersion.TryParse(c.Version, out var p) ? p : null))
            .OrderByDescending(x => x.Parsed is not null)
            .ThenByDescending(x => x.Parsed ?? default)
            .Select(x => x.Candidate)
            .FirstOrDefault();

    private static string CoreVersion(string version) => version.Split('+', 2)[0].Split('-', 2)[0];

    //
    // The mods the list's entries need that the list doesn't name, resolved against sptVersion.
    //
    // sp-mod resolves the whole tree, so one request covers dependencies of dependencies. An entry
    // with no version, or whose version sp-mod doesn't recognise, can't be asked about and comes
    // back in NotChecked. A dependency with no version for sptVersion is still reported - the user
    // should know - but can't be added.
    //
    // parents (addon id to parent mod id) is only used to mark which missing mods are an addon's
    // parent, so the window can show those first (R4).
    //
    public static async Task<SpModDependencies> MissingDependenciesAsync(
        IReadOnlyList<ModListEntry> entries,
        IReadOnlyDictionary<int, int> parents,
        string sptVersion,
        ISpModListApi api,
        IProgress<SpModResolveProgress>? progress = null,
        CancellationToken ct = default)
    {
        var onList = new HashSet<int>(entries.Where(e => !e.IsAddon && e.ModId is not null).Select(e => e.ModId!.Value));
        var parentIds = new HashSet<int>(entries
            .Where(e => e.IsAddon && e.ModId is { } id && parents.ContainsKey(id))
            .Select(e => parents[e.ModId!.Value]));

        var askable = entries.Where(e => e.ModId is not null && !string.IsNullOrWhiteSpace(e.Version)).ToList();
        var notChecked = entries.Where(e => e.ModId is null || string.IsNullOrWhiteSpace(e.Version)).ToList();

        var found = new Dictionary<int, (DependencyNode Node, List<string> NeededBy, bool Conflict)>();

        var batches = askable.Where(e => !e.IsAddon).Chunk(DependencyBatch).Select(b => (Addons: false, Entries: b))
            .Concat(askable.Where(e => e.IsAddon).Chunk(DependencyBatch).Select(b => (Addons: true, Entries: b)))
            .ToList();

        var done = 0;
        progress?.Report(new SpModResolveProgress(0, askable.Count));

        try
        {
            foreach (var (isAddons, batch) in batches)
            {
                var unanswered = await AskAsync(batch, isAddons, e => e.Version!).ConfigureAwait(false);

                //
                // sp-mod matches the version column exactly, and that column holds the bare number:
                // "1.1.0+spt4.0" is shown, "1.1.0" is stored. Anything that missed is asked again
                // without its suffix.
                //
                var retry = unanswered.Where(e => CoreVersion(e.Version!) != e.Version).ToArray();
                if (retry.Length > 0)
                {
                    unanswered = [.. unanswered.Except(retry),
                        .. await AskAsync(retry, isAddons, e => CoreVersion(e.Version!)).ConfigureAwait(false)];
                }

                notChecked.AddRange(unanswered);

                done += batch.Length;
                progress?.Report(new SpModResolveProgress(done, askable.Count));
            }
        }
        catch (SpModApiException ex) when (ex.Code == "VALIDATION_FAILED" && ex.Message.Contains("SPT", StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Warn(Area, $"dependencies: sp-mod doesn't know SPT {sptVersion}");
            return new SpModDependencies(sptVersion, [], entries, SpModResolveFailure.UnknownSptVersion, ex.Message);
        }
        catch (Exception ex) when (ex is SpModApiException or HttpRequestException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            AppLog.Warn(Area, $"dependencies against SPT {sptVersion} failed: {ex.Message}");
            return new SpModDependencies(sptVersion, [], entries, SpModResolveFailure.Api, ex.Message);
        }

        var missing = found.Values
            .Select(f => new SpModDependency(
                f.Node.Id,
                string.IsNullOrWhiteSpace(f.Node.Name) ? $"mod {f.Node.Id}" : f.Node.Name!,
                f.Node.LatestCompatibleVersion?.Version,
                f.Node.LatestCompatibleVersion?.Id,
                [.. f.NeededBy.Distinct(StringComparer.OrdinalIgnoreCase)],
                parentIds.Contains(f.Node.Id),
                f.Conflict))
            .OrderByDescending(d => d.IsParent)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AppLog.Info(Area,
            $"dependencies against SPT {sptVersion}: {missing.Count} missing ({missing.Count(d => !d.HasVersion)} with no version, " +
            $"{missing.Count(d => d.IsParent)} addon parents), {notChecked.Count} entries not checked");

        return new SpModDependencies(sptVersion, missing, notChecked);

        async Task<List<ModListEntry>> AskAsync(IReadOnlyList<ModListEntry> batch, bool isAddons, Func<ModListEntry, string> version)
        {
            var keys = batch
                .GroupBy(e => $"{e.ModId}:{version(e)}")
                .ToDictionary(g => g.Key, g => g.First());
            var pairs = string.Join(",", keys.Keys);

            var answer = isAddons
                ? await api.AddonDependenciesAsync(pairs, sptVersion, ct).ConfigureAwait(false)
                : await api.ModDependenciesAsync(pairs, sptVersion, ct).ConfigureAwait(false);

            var missed = new List<ModListEntry>();

            foreach (var (key, entry) in keys)
            {
                if (answer.TryGetValue(key, out var nodes)) Walk(nodes, entry.Name, []);
                else missed.Add(entry);
            }

            return missed;
        }

        void Walk(IEnumerable<DependencyNode> nodes, string requiredBy, HashSet<int> path)
        {
            foreach (var node in nodes)
            {
                if (!path.Add(node.Id)) continue;

                if (!onList.Contains(node.Id))
                {
                    if (found.TryGetValue(node.Id, out var seen))
                    {
                        seen.NeededBy.Add(requiredBy);
                        found[node.Id] = seen with { Conflict = seen.Conflict || node.Conflict };
                    }
                    else
                    {
                        found[node.Id] = (node, [requiredBy], node.Conflict);
                    }
                }

                Walk(node.Dependencies, string.IsNullOrWhiteSpace(node.Name) ? requiredBy : node.Name!, path);
                path.Remove(node.Id);
            }
        }
    }
}
