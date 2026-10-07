using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

// How a range reads against another mod's published versions.
public enum RangeWordingKind
{
    // The version given is above everything the range accepts: "up to <Version>".
    UpTo,

    // The version given is below everything it accepts: "<Version> or later".
    AtLeast,

    // Neither: Version is the range in words.
    Plain,
}

//
// OPEN-23: a dependency's version range in words, never with operators (feedback: no ^ ~ >= shown) -
// the newest published version it accepts when <c>against</c> is above them all, the oldest when it is
// below them all, a bare version as itself, otherwise the formatter's reading.
//
public static class RangeWording
{
    public static (string Version, RangeWordingKind Kind) Describe(string range, string? against, IEnumerable<ModVersionSummary>? published)
    {
        var accepted = (published ?? [])
            .Select(v => v.Version)
            .Where(v => ModVersionMatcher.IsSatisfiedBy(range, v) == true)
            .Select(v => v!)
            .Order(Comparer<string>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0))
            .ToList();

        if (accepted.Count > 0 && against is not null)
        {
            if (ModVersionComparer.Compare(against, accepted[^1]) > 0) return (accepted[^1], RangeWordingKind.UpTo);
            if (ModVersionComparer.Compare(against, accepted[0]) < 0) return (accepted[0], RangeWordingKind.AtLeast);
        }

        // A bare version is that version exactly here (ModVersionMatcher), not the SPT reading of it
        // the formatter gives ("3.0.3 - 3.0.x").
        if (Version.TryParse(range.Trim(), out _)) return (range.Trim(), RangeWordingKind.Plain);

        return (SptVersionRangeFormatter.Format(range) ?? range, RangeWordingKind.Plain);
    }
}
