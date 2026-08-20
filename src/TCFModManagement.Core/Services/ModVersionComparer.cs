namespace TCFModManager.Core.Services;

// 
// Compares two loosely-formatted version strings (an installed mod's version vs. the latest one
// published on sp-mod.com) to determine whether an update is available. Not a full SemVer
// implementation: variable segment count, pre-release suffix dropped, tolerant of a leading "v"/"V".
// 
public static class ModVersionComparer
{
    // True if <paramref name="latest"/> is a newer version than <paramref name="installed"/>.
    // Null when either is missing or unparsable.
    public static bool? IsUpdateAvailable(string? installed, string? latest)
    {
        var installedVersion = ParseVersion(installed);
        var latestVersion = ParseVersion(latest);
        if (installedVersion is null || latestVersion is null) return null;

        return latestVersion > installedVersion;
    }

    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var core = raw.Trim().TrimStart('v', 'V').Split('-', 2)[0];
        var parts = core.Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var major)) return null;

        int Part(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;

        return new Version(major, Part(1), Part(2), Part(3));
    }
}
