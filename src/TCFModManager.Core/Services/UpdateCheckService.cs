using System.Globalization;
using TCFModManager.Core.Models;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.Services;

//
// The network half of an update check (D1-D3): ask The Forge only about what is installed, then
// fetch fresh listings for just the mods it says moved, and for every installed addon.
//
// It decides nothing about what counts as an update. The listings it returns are patched into the
// catalog and addon caches, and the Installed page's own rule (BuildFrom) makes the call - so a
// toast can never claim an update the Installed page doesn't show (D2).
//
public sealed class UpdateCheckService(SpModApiClient api)
{
    //
    // /mods/updates takes every installed mod in one "id:version" list. The spike sent 80 of them in
    // 877 characters; longer lists are split well before any server or proxy would object.
    //
    internal const int MaxModsQueryLength = 1500;

    // The catalog's page size for a filter[id] lookup.
    private const int PageSize = 50;

    // OPEN-58: above this many held-back updates, none are grouped.
    internal const int MaxGroupCandidates = 12;

    //
    // What /mods/updates says about <paramref name="installed"/> on <paramref name="sptVersion"/>.
    //
    // Updated is the ids it lists under "updates". Blocked and incompatible entries are not updates:
    // incompatible is about what is installed not fitting this SPT, which the Installed page already
    // shows. Blocked ones come back as HeldBack (OPEN-12 F11) - an update that would break another
    // installed mod, which the Installed page then doesn't offer.
    //
    // NotRecognised is every id it left out of all four lists. It does that, silently, for a version
    // that isn't one of the mod's releases - found live on 2026-09-27: 1.0.1.0, v1.0.1 or banana for
    // a mod with a 1.0.1 release all vanish, HTTP 200. That is exactly a hand install whose version
    // came from its files (R7), so those mods are re-read directly rather than dropped.
    //
    public async Task<InstalledUpdateCheck> FindUpdatedModsAsync(
        IReadOnlyList<(int ModId, string Version)> installed, string sptVersion, CancellationToken ct = default)
    {
        var updated = new List<int>();
        var answered = new HashSet<int>();
        var heldBack = new List<HeldBackUpdate>();
        var notForSpt = new List<int>();
        var upToDate = new List<int>();

        foreach (var chunk in ModsQueryChunks(installed))
        {
            var result = await api.GetModUpdatesAsync(chunk, sptVersion, ct).ConfigureAwait(false);

            var ids = result.Updates.Select(u => u.CurrentVersion?.ModId).OfType<int>().ToList();
            updated.AddRange(ids);

            answered.UnionWith(ids);
            answered.UnionWith(result.BlockedUpdates.Select(u => u.CurrentVersion?.ModId).OfType<int>());
            heldBack.AddRange(result.BlockedUpdates.Select(HeldBackUpdate.From).OfType<HeldBackUpdate>());
            answered.UnionWith(result.UpToDate.Select(u => u.ModId));
            answered.UnionWith(result.IncompatibleWithSpt.Select(u => u.ModId));
            upToDate.AddRange(result.UpToDate.Select(u => u.ModId));
            notForSpt.AddRange(result.IncompatibleWithSpt.Select(u => u.ModId));
        }

        var asked = installed.Where(m => !string.IsNullOrWhiteSpace(m.Version)).Select(m => m.ModId);

        var held = await GroupHeldBackAsync(installed, [.. heldBack.DistinctBy(h => h.ModId)], sptVersion, ct).ConfigureAwait(false);

        return new InstalledUpdateCheck(
            [.. updated.Distinct()],
            [.. asked.Distinct().Where(id => !answered.Contains(id))])
        {
            HeldBack = held,
            NotForSpt = [.. notForSpt.Distinct()],
            UpToDate = [.. upToDate.Distinct()],
        };
    }

    //
    // OPEN-58 D1: held-back updates that can go together - two mods that pin each other's exact
    // release, say, so neither can move alone. Candidates are joined by the blockers sp-mod names
    // and, for a chained one, by the held-back mods in its dependency tree. Each group is then
    // confirmed by sp-mod: every member must be a plain update once the other members are at their
    // held releases. Any failure leaves the answer as sp-mod gave it.
    //
    private async Task<IReadOnlyList<HeldBackUpdate>> GroupHeldBackAsync(
        IReadOnlyList<(int ModId, string Version)> installed, IReadOnlyList<HeldBackUpdate> heldBack,
        string sptVersion, CancellationToken ct)
    {
        var byId = heldBack.ToDictionary(h => h.ModId);
        var candidates = heldBack
            .Where(h => h.Blockers.All(b => byId.ContainsKey(b.ModId)))
            .Select(h => h.ModId)
            .ToList();

        if (candidates.Count < 2) return heldBack;

        if (candidates.Count > MaxGroupCandidates)
        {
            AppLog.Info("Updates", $"{candidates.Count} held-back updates - too many to try grouping");
            return heldBack;
        }

        try
        {
            var links = candidates.ToDictionary(id => id, _ => new HashSet<int>());
            void Link(int a, int b)
            {
                if (a == b || !links.ContainsKey(a) || !links.ContainsKey(b)) return;
                links[a].Add(b);
                links[b].Add(a);
            }

            foreach (var id in candidates)
            foreach (var blocker in byId[id].Blockers)
                Link(id, blocker.ModId);

            var chained = candidates.Where(id => byId[id].Blockers.Count == 0).ToList();
            if (chained.Count > 0)
            {
                var trees = await api.GetModDependenciesAsync(
                    string.Join(',', chained.Select(id => $"{id.ToString(CultureInfo.InvariantCulture)}:{byId[id].Version.Trim()}")),
                    sptVersion, ct).ConfigureAwait(false);

                foreach (var (key, nodes) in trees)
                {
                    if (!int.TryParse(key.Split(':')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) continue;
                    foreach (var node in Flatten(nodes)) Link(id, node.Id);
                }
            }

            var together = new Dictionary<int, HashSet<int>>();

            foreach (var component in Components(candidates, links))
            {
                var members = component;
                while (members.Count >= 2)
                {
                    var failed = new List<int>();
                    foreach (var id in members)
                    {
                        if (!await GoesWithAsync(installed, members, id, byId, sptVersion, ct).ConfigureAwait(false)) failed.Add(id);
                    }

                    if (failed.Count == 0) break;
                    members.ExceptWith(failed);
                }

                if (members.Count < 2) continue;

                foreach (var id in members) together[id] = members;
                AppLog.Info("Updates", $"held-back updates go together: {string.Join(", ", members.Select(id => $"{id} {byId[id].Version}"))}");
            }

            if (together.Count == 0) return heldBack;

            return [.. heldBack.Select(h => together.TryGetValue(h.ModId, out var group)
                ? h with
                {
                    Together = [.. group.Where(id => id != h.ModId).Select(id => new HeldBackPartner(id, byId[id].Name, byId[id].Version))],
                }
                : h)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Warn("Updates", $"couldn't check which held-back updates go together: {ex.Message}");
            return heldBack;
        }
    }

    // Whether sp-mod offers this member its held release once every other member is at its own.
    private async Task<bool> GoesWithAsync(
        IReadOnlyList<(int ModId, string Version)> installed, IReadOnlySet<int> members, int modId,
        IReadOnlyDictionary<int, HeldBackUpdate> byId, string sptVersion, CancellationToken ct)
    {
        var state = installed
            .OrderByDescending(m => members.Contains(m.ModId))
            .Select(m => members.Contains(m.ModId) && m.ModId != modId ? (m.ModId, byId[m.ModId].Version) : m)
            .ToList();

        foreach (var chunk in ModsQueryChunks(state))
        {
            var result = await api.GetModUpdatesAsync(chunk, sptVersion, ct).ConfigureAwait(false);
            if (result.Updates.Any(u => u.CurrentVersion?.ModId == modId && byId[modId].Holds(u.RecommendedVersion?.Version)))
                return true;
        }

        return false;
    }

    private static List<HashSet<int>> Components(IReadOnlyList<int> ids, IReadOnlyDictionary<int, HashSet<int>> links)
    {
        var seen = new HashSet<int>();
        var components = new List<HashSet<int>>();

        foreach (var start in ids)
        {
            if (!seen.Add(start)) continue;

            var component = new HashSet<int> { start };
            var pending = new Stack<int>();
            pending.Push(start);
            while (pending.TryPop(out var id))
            {
                foreach (var next in links[id].Where(seen.Add))
                {
                    component.Add(next);
                    pending.Push(next);
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static IEnumerable<DependencyNode> Flatten(IEnumerable<DependencyNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Dependencies)) yield return child;
        }
    }

    // Fresh listings, with categories and versions, shaped like the catalog cache's own.
    public async Task<IReadOnlyList<Mod>> FetchModsAsync(IEnumerable<int> modIds, CancellationToken ct = default)
    {
        var mods = new List<Mod>();

        foreach (var ids in modIds.Distinct().Chunk(PageSize))
        {
            var page = await api.GetModsAsync(new ModsQuery
            {
                FilterId = string.Join(',', ids.Select(i => i.ToString(CultureInfo.InvariantCulture))),
                Include = "category,versions",
                PerPage = PageSize,
            }, ct).ConfigureAwait(false);

            mods.AddRange(page.Data);
        }

        return mods;
    }

    //
    // Addons are not covered by /mods/updates (D3). Few are ever installed, so each check simply
    // re-reads their listings, versions included, a page at a time.
    //
    public async Task<IReadOnlyList<Addon>> FetchAddonsAsync(IEnumerable<int> addonIds, CancellationToken ct = default)
    {
        var addons = new List<Addon>();

        foreach (var ids in addonIds.Distinct().Chunk(PageSize))
        {
            var page = await api.GetAddonsAsync(new AddonsQuery
            {
                FilterId = string.Join(',', ids.Select(i => i.ToString(CultureInfo.InvariantCulture))),
                Include = "versions",
                PerPage = PageSize,
            }, ct).ConfigureAwait(false);

            addons.AddRange(page.Data);
        }

        return addons;
    }

    // "id:version,id:version..." lists no longer than MaxModsQueryLength, in the order given.
    internal static IEnumerable<string> ModsQueryChunks(IEnumerable<(int ModId, string Version)> installed)
    {
        var current = new List<string>();
        var length = 0;

        foreach (var (modId, version) in installed)
        {
            if (string.IsNullOrWhiteSpace(version)) continue;

            var item = $"{modId.ToString(CultureInfo.InvariantCulture)}:{version.Trim()}";
            var added = item.Length + (current.Count > 0 ? 1 : 0);

            if (current.Count > 0 && length + added > MaxModsQueryLength)
            {
                yield return string.Join(',', current);
                current.Clear();
                length = 0;
                added = item.Length;
            }

            current.Add(item);
            length += added;
        }

        if (current.Count > 0) yield return string.Join(',', current);
    }
}

// What one /mods/updates round said. ToRefetch is every mod whose listing a check should re-read.
public sealed record InstalledUpdateCheck(IReadOnlyList<int> Updated, IReadOnlyList<int> NotRecognised)
{
    // Updates sp-mod holds back because they would break another installed mod (OPEN-12 F11).
    public IReadOnlyList<HeldBackUpdate> HeldBack { get; init; } = [];

    //
    // OPEN-13: the installed version isn't for this SPT ("incompatible_with_spt"), and the installed
    // version is for it and is the newest that is ("up_to_date"). Mods in neither list - an update is
    // on offer - say nothing about the installed version itself.
    //
    public IReadOnlyList<int> NotForSpt { get; init; } = [];

    public IReadOnlyList<int> UpToDate { get; init; } = [];

    public IReadOnlyList<int> ToRefetch => [.. Updated.Concat(NotRecognised).Concat(HeldBack.Select(h => h.ModId)).Distinct()];
}
