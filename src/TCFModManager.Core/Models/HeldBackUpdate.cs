namespace TCFModManager.Core.Models;

//
// OPEN-12 F11: an update sp-mod holds back because installing it would break another installed mod -
// CommonLib 3.0.6, say, while Black and Blue needs CommonLib ~2.0.24. Read from the blocked_updates
// list of GET /mods/updates. The App words it (HeldBackUpdates.Describe); Core only carries the facts.
//
// Version is the release held back. Only that release is: when the card would offer a different
// one, nothing is held.
//
public sealed record HeldBackUpdate(int ModId, string Version, string? Reason, IReadOnlyList<HeldBackBlocker> Blockers)
{
    // sp-mod's block_reason when the conflict is further down a dependency chain, with no blocking mod
    // of its own to name.
    public const string ChainReason = "chain_dependency_conflict";

    public bool Holds(string? version) =>
        version is not null && string.Equals(version.Trim(), Version.Trim(), StringComparison.OrdinalIgnoreCase);

    // Null for an entry that names no mod or no version - there is nothing to hold back then.
    public static HeldBackUpdate? From(ModBlockedUpdateEntry entry)
    {
        if (entry.CurrentVersion is not { ModId: > 0 } current) return null;
        if (entry.LatestVersion?.Version is not { Length: > 0 } version) return null;

        var blockers = entry.BlockingMods
            .Select(b => new HeldBackBlocker(b.ModId, b.ModName, b.Constraint))
            .ToList();

        return new HeldBackUpdate(current.ModId, version, entry.BlockReason, blockers);
    }
}

// An installed mod the update would break, and the version range of the held mod it needs.
public sealed record HeldBackBlocker(int ModId, string? Name, string? Constraint);
