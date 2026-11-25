using TCFModManager.Core.Models;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.Services;

//
// The sp-mod requests ModListVersionResolver makes. SpModListApi is the real one.
//
public interface IModListVersionApi
{
    // The most recent versions of each mod, in full. A mod missing from the answer wasn't returned.
    Task<IReadOnlyDictionary<int, IReadOnlyList<ModVersion>>> RecentModVersionsAsync(IReadOnlyList<int> modIds, CancellationToken ct);

    Task<IReadOnlyDictionary<int, IReadOnlyList<AddonVersion>>> RecentAddonVersionsAsync(IReadOnlyList<int> addonIds, CancellationToken ct);

    // The published versions matching one version string. Empty when sp-mod's version filter refuses it.
    Task<IReadOnlyList<ModVersion>> MatchingModVersionsAsync(int modId, string version, CancellationToken ct);

    Task<IReadOnlyList<AddonVersion>> MatchingAddonVersionsAsync(int addonId, string version, CancellationToken ct);

    // One mod's newest versions, further back than the recent ones.
    Task<IReadOnlyList<ModVersion>> NewestModVersionsAsync(int modId, CancellationToken ct);

    Task<IReadOnlyList<AddonVersion>> NewestAddonVersionsAsync(int addonId, CancellationToken ct);
}

public enum ModListUnavailableReason
{
    // No sp-mod listing for the entry, or the mod isn't in the catalog.
    NoListing,

    NoAddonListing,

    // An addon whose parent is neither installed, fetched by this apply, nor on the list. ParentId, ParentName.
    OrphanedAddon,

    // Nothing published at all.
    NoVersions,

    // Published, but nothing fits this SPT. Version is the SPT version.
    NoVersionForSpt,

    // Published, but nothing fits the parent's version. Version is the parent's version.
    NoVersionForParent,

    // The version the list names is no longer published and nothing replaces it. Version is the wanted one.
    VersionGone,

    // sp-mod refused or didn't answer. Detail is the error message.
    SpModFailed,
}

public sealed record ModListReady(ModListAction Action, InstallTarget Target, ModVersion Version);

// A fetch the list can't have as written, and the version on offer instead. ParentVersion is set when
// the wanted version is published but doesn't fit its parent (OPEN-31 F2).
public sealed record ModListVersionOffer(
    ModListAction Action,
    InstallTarget Target,
    string Wanted,
    ModVersion Available,
    string? ParentName = null,
    string? ParentVersion = null)
{
    public bool DoesNotFitParent => ParentVersion is not null;
}

public sealed record ModListUnavailable(
    ModListAction Action,
    ModListUnavailableReason Reason,
    string? Version = null,
    int? ParentId = null,
    string? ParentName = null,
    string? Detail = null);

public sealed record ModListVersionResolution(
    IReadOnlyList<ModListReady> Ready,
    IReadOnlyList<ModListVersionOffer> Offers,
    IReadOnlyList<ModListUnavailable> Unavailable,
    int Requests);

public sealed record ModListVersionContext
{
    public required IReadOnlyList<ModListCandidate> Installed { get; init; }

    public required IReadOnlyList<ModListEntry> ListEntries { get; init; }

    public required IReadOnlyDictionary<int, Mod> Mods { get; init; }

    public required IReadOnlyDictionary<int, Addon> Addons { get; init; }

    public string? SptVersion { get; init; }
}

//
// Works out the exact version behind each fetch of a list apply (OPEN-69). Every mod and addon is
// read in batches of BatchSize; one is asked about on its own only when its batch answer stopped at
// the embed cap and didn't settle it, or it was missing from the answer.
//
public static class ModListVersionResolver
{
    private const string Area = "ModListVersionResolver";

    public const int BatchSize = 50;

    private sealed record Lookup(
        int Index, ModListAction Action, InstallTarget Target, int Id, bool IsAddon, string? ParentVersion, string? ParentName);

    private sealed class Tally
    {
        public int Requests;
    }

    public static async Task<ModListVersionResolution> ResolveAsync(
        IReadOnlyList<ModListAction> fetches,
        ModListVersionContext context,
        IModListVersionApi api,
        IProgress<SpModResolveProgress>? progress = null,
        CancellationToken ct = default)
    {
        var slots = new object?[fetches.Count];
        var lookups = Classify(fetches, context, slots);

        var done = fetches.Count - lookups.Count;
        progress?.Report(new SpModResolveProgress(done, fetches.Count));

        var tally = new Tally();
        var failed = new Dictionary<(bool IsAddon, int Id), string>();

        var mods = await BatchesAsync<ModVersion>(
            lookups.Where(l => !l.IsAddon).Select(l => l.Id), false, api.RecentModVersionsAsync, failed, tally, ct).ConfigureAwait(false);
        var addons = await BatchesAsync<AddonVersion>(
            lookups.Where(l => l.IsAddon).Select(l => l.Id), true, api.RecentAddonVersionsAsync, failed, tally, ct).ConfigureAwait(false);

        var modCap = mods.Count == 0 ? 0 : mods.Values.Max(v => v.Count);
        var addonCap = addons.Count == 0 ? 0 : addons.Values.Max(v => v.Count);

        foreach (var l in lookups)
        {
            if (failed.TryGetValue((l.IsAddon, l.Id), out var message))
            {
                slots[l.Index] = new ModListUnavailable(l.Action, ModListUnavailableReason.SpModFailed, Detail: message);
            }
            else
            {
                try
                {
                    slots[l.Index] = l.IsAddon
                        ? await AddonAsync(l, addons.GetValueOrDefault(l.Id), addonCap, context.SptVersion, api, tally, ct).ConfigureAwait(false)
                        : await ModAsync(l, mods.GetValueOrDefault(l.Id), modCap, context.SptVersion, api, tally, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsSpModFailure(ex, ct))
                {
                    slots[l.Index] = new ModListUnavailable(l.Action, ModListUnavailableReason.SpModFailed, Detail: ex.Message);
                }
            }

            progress?.Report(new SpModResolveProgress(++done, fetches.Count));
        }

        AppLog.Info(Area, $"{fetches.Count} fetches resolved with {tally.Requests} sp-mod requests");

        return new ModListVersionResolution(
            [.. slots.OfType<ModListReady>()],
            [.. slots.OfType<ModListVersionOffer>()],
            [.. slots.OfType<ModListUnavailable>()],
            tally.Requests);
    }

    private static List<Lookup> Classify(IReadOnlyList<ModListAction> fetches, ModListVersionContext context, object?[] slots)
    {
        var parents = context.Installed
            .Where(c => c is { IsAddon: false, ModId: not null })
            .Select(c => c.ModId!.Value)
            .ToHashSet();

        foreach (var action in fetches.Where(a => a is { IsAddon: false, ModId: not null }))
            parents.Add(action.ModId!.Value);

        var placed = context.ListEntries
            .Where(e => e is { IsAddon: false, ModId: not null })
            .Select(e => e.ModId!.Value)
            .ToHashSet();

        var parentVersions = context.Installed
            .Where(c => c is { IsAddon: false, ModId: not null })
            .GroupBy(c => c.ModId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Version);

        foreach (var action in fetches.Where(a => a is { IsAddon: false, ModId: not null }))
            parentVersions[action.ModId!.Value] = action.TargetVersion;

        var lookups = new List<Lookup>();

        for (var i = 0; i < fetches.Count; i++)
        {
            var action = fetches[i];

            if (action.ModId is not { } id)
            {
                slots[i] = new ModListUnavailable(action, ModListUnavailableReason.NoListing);
                continue;
            }

            if (!action.IsAddon)
            {
                if (context.Mods.GetValueOrDefault(id) is { } mod)
                    lookups.Add(new Lookup(i, action, InstallTarget.For(mod), id, false, null, null));
                else
                    slots[i] = new ModListUnavailable(action, ModListUnavailableReason.NoListing);

                continue;
            }

            if (context.Addons.GetValueOrDefault(id) is not { } addon)
            {
                slots[i] = new ModListUnavailable(action, ModListUnavailableReason.NoAddonListing);
                continue;
            }

            if (addon.ModId is { } parentId && !parents.Contains(parentId) && !placed.Contains(parentId))
            {
                slots[i] = new ModListUnavailable(
                    action, ModListUnavailableReason.OrphanedAddon,
                    ParentId: parentId, ParentName: context.Mods.GetValueOrDefault(parentId)?.Name);
                continue;
            }

            lookups.Add(new Lookup(
                i,
                action,
                InstallTarget.For(addon),
                id,
                true,
                addon.ModId is { } parent ? parentVersions.GetValueOrDefault(parent) : null,
                addon.ModId is { } named ? context.Mods.GetValueOrDefault(named)?.Name : null));
        }

        return lookups;
    }

    private static async Task<Dictionary<int, IReadOnlyList<T>>> BatchesAsync<T>(
        IEnumerable<int> ids,
        bool isAddon,
        Func<IReadOnlyList<int>, CancellationToken, Task<IReadOnlyDictionary<int, IReadOnlyList<T>>>> read,
        Dictionary<(bool IsAddon, int Id), string> failed,
        Tally tally,
        CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyList<T>>();

        foreach (var batch in ids.Distinct().Chunk(BatchSize))
        {
            tally.Requests++;

            try
            {
                foreach (var (id, versions) in await read(batch, ct).ConfigureAwait(false))
                    result[id] = versions;
            }
            catch (Exception ex) when (IsSpModFailure(ex, ct))
            {
                AppLog.Warn(Area, $"version batch of {batch.Length} {(isAddon ? "addons" : "mods")} failed: {ex.Message}");
                foreach (var id in batch) failed[(isAddon, id)] = ex.Message;
            }
        }

        return result;
    }

    private static async Task<object> ModAsync(
        Lookup l, IReadOnlyList<ModVersion>? embedded, int cap, string? spt, IModListVersionApi api, Tally tally, CancellationToken ct)
    {
        IReadOnlyList<ModVersion> known = embedded ?? [];
        var whole = embedded is not null && embedded.Count < cap;
        var wanted = l.Action.TargetVersion?.Trim();

        async Task<(ModVersion? Fit, bool Any)> NewestAsync()
        {
            var fit = NewestFittingVersion.ForSpt(known, spt);
            if (fit is not null || whole) return (fit, known.Count > 0);

            tally.Requests++;
            var further = await api.NewestModVersionsAsync(l.Id, ct).ConfigureAwait(false);
            return (NewestFittingVersion.ForSpt(further, spt), known.Count > 0 || further.Count > 0);
        }

        if (string.IsNullOrWhiteSpace(wanted))
        {
            var (newest, any) = await NewestAsync().ConfigureAwait(false);

            if (newest is not null) return new ModListReady(l.Action, l.Target, newest);

            return any
                ? new ModListUnavailable(l.Action, ModListUnavailableReason.NoVersionForSpt, Version: spt)
                : new ModListUnavailable(l.Action, ModListUnavailableReason.NoVersions);
        }

        var exact = Pick(known, l.Action.VersionId, wanted, v => v.Id, v => v.Version);

        if (exact is null && !whole)
        {
            tally.Requests++;
            exact = Pick(await api.MatchingModVersionsAsync(l.Id, wanted, ct).ConfigureAwait(false),
                l.Action.VersionId, wanted, v => v.Id, v => v.Version);
        }

        if (exact is not null) return new ModListReady(l.Action, l.Target, exact);

        var (replacement, _) = await NewestAsync().ConfigureAwait(false);

        return replacement is null
            ? new ModListUnavailable(l.Action, ModListUnavailableReason.VersionGone, Version: wanted)
            : new ModListVersionOffer(l.Action, l.Target, wanted, replacement);
    }

    private static async Task<object> AddonAsync(
        Lookup l, IReadOnlyList<AddonVersion>? embedded, int cap, string? spt, IModListVersionApi api, Tally tally, CancellationToken ct)
    {
        IReadOnlyList<AddonVersion> known = embedded ?? [];
        var whole = embedded is not null && embedded.Count < cap;
        var wanted = l.Action.TargetVersion?.Trim();
        var parentVersion = l.ParentVersion;

        async Task<(AddonVersion? Fit, bool Any)> NewestAsync()
        {
            var fit = NewestFittingVersion.ForParent(known, parentVersion, spt);
            if (fit is not null || whole) return (fit, known.Count > 0);

            tally.Requests++;
            var further = await api.NewestAddonVersionsAsync(l.Id, ct).ConfigureAwait(false);
            return (NewestFittingVersion.ForParent(further, parentVersion, spt), known.Count > 0 || further.Count > 0);
        }

        if (string.IsNullOrWhiteSpace(wanted))
        {
            var (newest, any) = await NewestAsync().ConfigureAwait(false);

            if (newest is not null) return new ModListReady(l.Action, l.Target, AsModVersion(newest));

            return any
                ? new ModListUnavailable(l.Action, ModListUnavailableReason.NoVersionForParent, Version: parentVersion)
                : new ModListUnavailable(l.Action, ModListUnavailableReason.NoVersions);
        }

        var exact = Pick(known, l.Action.VersionId, wanted, v => v.Id, v => v.Version);

        if (exact is null && !whole)
        {
            tally.Requests++;
            exact = Pick(await api.MatchingAddonVersionsAsync(l.Id, wanted, ct).ConfigureAwait(false),
                l.Action.VersionId, wanted, v => v.Id, v => v.Version);
        }

        if (exact is not null && !string.IsNullOrWhiteSpace(parentVersion)
            && AddonFit.Check(exact.ModVersionConstraint, parentVersion, spt) == false)
        {
            if (l.Action.Entry?.FitOverride == true)
                return new ModListReady(l.Action, l.Target with { FitOverride = parentVersion }, AsModVersion(exact));

            var (fits, _) = await NewestAsync().ConfigureAwait(false);

            return fits is null
                ? new ModListUnavailable(l.Action, ModListUnavailableReason.NoVersionForParent, Version: parentVersion)
                : new ModListVersionOffer(l.Action, l.Target, wanted, AsModVersion(fits), l.ParentName, parentVersion);
        }

        if (exact is not null) return new ModListReady(l.Action, l.Target, AsModVersion(exact));

        var (replacement, _) = await NewestAsync().ConfigureAwait(false);

        return replacement is null
            ? new ModListUnavailable(l.Action, ModListUnavailableReason.VersionGone, Version: wanted)
            : new ModListVersionOffer(l.Action, l.Target, wanted, AsModVersion(replacement));
    }

    private static T? Pick<T>(IReadOnlyList<T> versions, int? versionId, string wanted, Func<T, int> id, Func<T, string?> version)
        where T : class =>
        (versionId is { } wantedId ? versions.FirstOrDefault(v => id(v) == wantedId) : null)
        ?? versions.FirstOrDefault(v => string.Equals(version(v)?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));

    private static bool IsSpModFailure(Exception ex, CancellationToken ct) =>
        ex is SpModApiException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested;

    public static ModVersion AsModVersion(AddonVersion version) => new()
    {
        Id = version.Id,
        Version = version.Version,
        Description = version.Description,
        Link = version.Link,
        ContentLength = version.ContentLength,
        Downloads = version.Downloads,
        PublishedAt = version.PublishedAt,
    };
}
