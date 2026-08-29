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

    // The installed version string, as InstalledModCardViewModel reports it.
    public string? Version { get; init; }

    public string? Guid { get; init; }

    public bool IsDisabled { get; init; }

    // The mod folder names on disk this card covers - both halves of a client+server mod.
    public IReadOnlyList<string> Folders { get; init; } = [];
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

    public static ModList Build(
        string name,
        IEnumerable<ModListCandidate> candidates,
        DateTimeOffset timestamp,
        VersionLookup? versions = null,
        string? sptVersion = null,
        ModListPolicy policy = ModListPolicy.Exclusive,
        bool includeDisabled = false,
        bool isSnapshot = false)
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

        list.Entries.AddRange(BuildEntries(candidates, versions, includeDisabled));
        return list;
    }

    // The entries alone, for re-capturing into a list that already exists.
    public static List<ModListEntry> BuildEntries(
        IEnumerable<ModListCandidate> candidates,
        VersionLookup? versions = null,
        bool includeDisabled = false)
    {
        var entries = new List<ModListEntry>();
        var seenModIds = new HashSet<int>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (candidate.IsDisabled && !includeDisabled) continue;

            if (candidate.ModId is { } modId)
            {
                if (!seenModIds.Add(modId)) continue;
            }
            else if (!seenNames.Add(candidate.Name.Trim()))
            {
                continue;
            }

            entries.Add(new ModListEntry
            {
                Name = candidate.Name.Trim(),
                ModId = candidate.ModId,
                VersionId = ResolveVersionId(candidate, versions),
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

    private static int? ResolveVersionId(ModListCandidate candidate, VersionLookup? versions)
    {
        if (candidate.ModId is not { } modId || versions is null) return null;
        if (string.IsNullOrWhiteSpace(candidate.Version)) return null;

        var published = versions(modId);
        if (published is null || published.Count == 0) return null;

        var installed = candidate.Version.Trim();

        var exact = published.FirstOrDefault(v =>
            string.Equals(v.Version?.Trim(), installed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Id;

        if (!SemanticVersion.TryParse(installed, out var parsed)) return null;

        var semantic = published.FirstOrDefault(v =>
            SemanticVersion.TryParse(v.Version, out var candidateVersion)
            && candidateVersion.Value.CompareTo(parsed.Value) == 0);

        return semantic?.Id;
    }
}
