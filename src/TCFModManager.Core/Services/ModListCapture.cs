using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// One installed mod as the Installed page sees it, reduced to what a list entry needs.
//
// The Installed cards are the source, not Data\installed-mods.json: the manifest only holds mods
// this app installed or the user confirmed by hand, which on a real install is a small fraction of
// what's there. The cards cover everything the scanner found and carry the catalog match where the
// matcher could make one.
//
public sealed record ModListCandidate
{
    public required string Name { get; init; }

    public int? ModId { get; init; }

    // True when ModId is an addon id. Carried through capture, planning and membership so an addon
    // is never compared against a mod that happens to share its number.
    public bool IsAddon { get; init; }

    // The installed version string, as InstalledModCardViewModel reports it.
    public string? Version { get; init; }

    public string? Guid { get; init; }

    public bool IsDisabled { get; init; }

    //
    // False for something a list can install and update but must never disable on its own: an addon
    // whose files live inside its parent mod's folder has no folder of its own to move, so a
    // Disable would silently do nothing. Disabling the parent takes it with it, which is the only
    // thing that was ever going to happen anyway.
    //
    public bool CanBeDisabled { get; init; } = true;

    // The mod folder names on disk this card covers - both halves of a client+server mod.
    public IReadOnlyList<string> Folders { get; init; } = [];

    //
    // Every scanned mod this card merged - what ModListApplier hands to ModDisableService when a
    // list turns into moves. Left empty for capture and planning, which only read the fields above;
    // an apply needs it, because a client+server mod has to move as one thing.
    //
    public IReadOnlyList<InstalledMod> Entries { get; init; } = [];
}

//
// Builds a ModList from what's installed.
//
// The version id is the part that can fail: a card knows the version string it has, and a list
// entry wants the Forge version id so the receiver fetches exactly that build. The lookup handed in
// here answers from whatever version data is already cached, so capture stays offline. A version
// that isn't in the cache (the catalog embeds only the six most recent) still produces an entry -
// with the mod id and the version string, and no pin.
//
public static class ModListCapture
{
    // Looks up the known published versions of a mod, or null when there are none cached.
    public delegate IReadOnlyList<ModVersionSummary>? VersionLookup(int modId);

    // The same for an addon, whose versions live in their own cache under their own ids.
    public delegate IReadOnlyList<AddonVersionSummary>? AddonVersionLookup(int addonId);

    public static ModList Build(
        string name,
        IEnumerable<ModListCandidate> candidates,
        DateTimeOffset timestamp,
        VersionLookup? versions = null,
        string? sptVersion = null,
        ModListPolicy policy = ModListPolicy.Exclusive,
        bool includeDisabled = false,
        bool isSnapshot = false,
        AddonVersionLookup? addonVersions = null)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = name,
            Origin = ModListOrigin.Local,
            Policy = policy,
            SptVersion = sptVersion,
            IsSnapshot = isSnapshot,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };

        list.Entries.AddRange(BuildEntries(candidates, versions, includeDisabled, addonVersions));
        return list;
    }

    // The entries alone, for re-capturing into a list that already exists.
    public static List<ModListEntry> BuildEntries(
        IEnumerable<ModListCandidate> candidates,
        VersionLookup? versions = null,
        bool includeDisabled = false,
        AddonVersionLookup? addonVersions = null)
    {
        var entries = new List<ModListEntry>();

        // Keyed on the pair, not the id: an addon and a mod can share a number and are still two
        // different things to record.
        var seenModIds = new HashSet<(int Id, bool IsAddon)>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (candidate.IsDisabled && !includeDisabled) continue;

            if (candidate.ModId is { } modId)
            {
                if (!seenModIds.Add((modId, candidate.IsAddon))) continue;
            }
            else if (!seenNames.Add(candidate.Name.Trim()))
            {
                continue;
            }

            entries.Add(new ModListEntry
            {
                Name = candidate.Name.Trim(),
                ModId = candidate.ModId,
                IsAddon = candidate.IsAddon,
                VersionId = ResolveVersionId(candidate, versions, addonVersions),
                Version = string.IsNullOrWhiteSpace(candidate.Version) ? null : candidate.Version.Trim(),
                Guid = string.IsNullOrWhiteSpace(candidate.Guid) ? null : candidate.Guid.Trim(),
                Folders = [.. candidate.Folders
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(f => f.Trim().ToLowerInvariant())
                    .Distinct()],
            });
        }

        return [.. entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static int? ResolveVersionId(
        ModListCandidate candidate, VersionLookup? versions, AddonVersionLookup? addonVersions)
    {
        if (candidate.ModId is not { } modId) return null;
        if (string.IsNullOrWhiteSpace(candidate.Version)) return null;

        var published = candidate.IsAddon
            ? addonVersions?.Invoke(modId)?.Select(v => (v.Id, v.Version)).ToList()
            : versions?.Invoke(modId)?.Select(v => (v.Id, v.Version)).ToList();

        return published is null ? null : MatchVersionId(published, candidate.Version.Trim());
    }

    // Exact string match first, then a semantic comparison, so "1.2.1.0" still pins "1.2.1".
    private static int? MatchVersionId(IReadOnlyList<(int Id, string? Version)> published, string installed)
    {
        if (published.Count == 0) return null;

        foreach (var (id, version) in published)
            if (string.Equals(version?.Trim(), installed, StringComparison.OrdinalIgnoreCase))
                return id;

        if (!SemanticVersion.TryParse(installed, out var parsed)) return null;

        foreach (var (id, version) in published)
            if (SemanticVersion.TryParse(version, out var candidateVersion)
                && candidateVersion.Value.CompareTo(parsed.Value) == 0)
                return id;

        return null;
    }
}
