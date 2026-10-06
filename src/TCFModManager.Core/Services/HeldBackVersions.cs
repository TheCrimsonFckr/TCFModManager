using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// OPEN-12 F11: what a held-back update leaves on offer. sp-mod names the release it holds back and
// the version range each mod it would break accepts ("~2.0.24"); this reads those ranges against the
// mod's published versions, the way the Dependencies page names "the newest version that fits".
//
public static class HeldBackVersions
{
    //
    // PerBlocker is, for each blocker in order, the newest published version it accepts that runs on
    // this SPT - null where it names no range or none fits. Safe is the newest version every blocker
    // accepts: what the mod can still be updated to without breaking anything. Null when any blocker
    // names no range (nothing can be promised then) or no version fits them all.
    //
    public static HeldBackResolution Resolve(
        HeldBackUpdate held, IEnumerable<ModVersionSummary> versions, string? sptVersion)
    {
        var candidates = versions
            .Where(v => !string.IsNullOrWhiteSpace(v.Version) && !held.Holds(v.Version))
            .Where(v => string.IsNullOrWhiteSpace(sptVersion)
                || SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, sptVersion) == true)
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(v => v.Version, Comparer<string?>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0))
            .ToList();

        var perBlocker = held.Blockers
            .Select(b => string.IsNullOrWhiteSpace(b.Constraint)
                ? null
                : candidates.FirstOrDefault(v => ModVersionMatcher.IsSatisfiedBy(b.Constraint, v.Version) == true)?.Version)
            .ToList();

        var safe = held.Blockers.Count == 0 || held.Blockers.Any(b => string.IsNullOrWhiteSpace(b.Constraint))
            ? null
            : candidates.FirstOrDefault(v => held.Blockers.All(b => ModVersionMatcher.IsSatisfiedBy(b.Constraint, v.Version) == true))?.Version;

        return new HeldBackResolution(held, perBlocker, safe);
    }
}

public sealed record HeldBackResolution(HeldBackUpdate Held, IReadOnlyList<string?> PerBlocker, string? Safe);
