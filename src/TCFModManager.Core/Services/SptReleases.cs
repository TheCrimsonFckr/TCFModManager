using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// One published SPT release. Label is the API's own string, used verbatim for display.
public readonly record struct SptRelease(Version Value, string Label)
{
    public int Major => Value.Major;
    public int Minor => Value.Minor;
}

// 
// Turns mod version constraints into the actual SPT releases they support. Without this a
// constraint like "~4.0.4" reads as "4.0.4 - 4.0.x", naming a boundary rather than a release
// anyone runs; resolved against the published list it becomes "4.0.13".
// 
public static class SptReleases
{
    // Releases below this are dropped from the catalog entirely - nobody is running them
    // and they only add noise to search and the version filter.
    public static readonly Version Floor = new(3, 10, 0, 0);

    // Parses the API's SPT version list into releases at or above <see cref="Floor"/>,
    // newest first.
    public static List<SptRelease> FromApi(IEnumerable<SptVersion> versions) =>
        versions
            .Select(v => (Parsed: ParseVersion(v.Version), v.Version))
            .Where(v => v.Parsed is not null && !string.IsNullOrWhiteSpace(v.Version))
            .Select(v => new SptRelease(v.Parsed!, v.Version!))
            .Where(r => r.Value >= Floor)
            .OrderByDescending(r => r.Value)
            .ToList();

    // The distinct major.minor lines present in <paramref name="releases"/>, newest first.
    public static List<(int Major, int Minor)> Lines(IEnumerable<SptRelease> releases) =>
        releases
            .Select(r => (r.Major, r.Minor))
            .Distinct()
            .OrderByDescending(l => l.Major)
            .ThenByDescending(l => l.Minor)
            .ToList();

    // 
    // The newest actual release on the given line that any of <paramref name="constraints"/>
    // allows, or null when none of them reach it.
    // 
    public static SptRelease? NewestSupportedOnLine(
        IEnumerable<string?> constraints,
        IReadOnlyList<SptRelease> releases,
        int major,
        int minor)
    {
        var parsed = constraints
            .Select(c => SptVersionRange.TryParse(c, out var bounds) ? bounds : (SptVersionBounds?)null)
            .Where(b => b is not null)
            .Select(b => b!.Value)
            .ToList();
        if (parsed.Count == 0) return null;

        SptRelease? best = null;

        foreach (var release in releases)
        {
            if (release.Major != major || release.Minor != minor) continue;
            if (!parsed.Any(b => b.Contains(release.Value))) continue;
            if (best is null || release.Value > best.Value.Value) best = release;
        }

        return best;
    }

    // Every release any of <paramref name="constraints"/> allows, newest first.
    public static List<SptRelease> Supported(
        IEnumerable<string?> constraints,
        IReadOnlyList<SptRelease> releases)
    {
        var parsed = constraints
            .Select(c => SptVersionRange.TryParse(c, out var bounds) ? bounds : (SptVersionBounds?)null)
            .Where(b => b is not null)
            .Select(b => b!.Value)
            .ToList();
        if (parsed.Count == 0) return [];

        return releases
            .Where(r => parsed.Any(b => b.Contains(r.Value)))
            .OrderByDescending(r => r.Value)
            .ToList();
    }

    // 
    // Whether any constraint reaches a release at or above <see cref="Floor"/>. Used to keep
    // long-dead mods out of the catalog without needing the published release list, so it still
    // works on a cold cache. A mod with no readable constraint gets the benefit of the doubt.
    // 
    public static bool ReachesFloor(IEnumerable<string?> constraints)
    {
        var readable = false;

        foreach (var constraint in constraints)
        {
            if (!SptVersionRange.TryParse(constraint, out var bounds)) continue;

            readable = true;
            if (bounds.MaxExclusive is null || bounds.MaxExclusive > Floor) return true;
        }

        return !readable;
    }

    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var core = raw.Split('-', 2)[0].TrimStart('v', 'V');
        var parts = core.Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var major)) return null;

        int Part(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;

        return new Version(major, Part(1), Part(2), Part(3));
    }
}
