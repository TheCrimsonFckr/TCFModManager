namespace TCFModManagement.Core.Services;

// 
// Answers whether an installed SPT version satisfies the constraint strings the sp-mod.com API
// puts on mod versions (spt_version_constraint) - e.g. "^3.9.0", "~3.9.0", "&lt;4.0.0", "&gt;=3.0.0",
// "4.0.*", or a bare "3.9.0" for an exact match. Parsing lives in SptVersionRange so this and the
// range display can't disagree about what a constraint means.
// 
public static class SptVersionMatcher
{
    // 
    // True if <paramref name="sptVersion"/> satisfies every clause in <paramref name="constraint"/>.
    // Returns null when either input is missing or unparsable.
    // 
    public static bool? IsSatisfiedBy(string? constraint, string? sptVersion)
    {
        if (string.IsNullOrWhiteSpace(constraint) || string.IsNullOrWhiteSpace(sptVersion)) return null;

        var version = ParseVersion(sptVersion);
        if (version is null) return null;

        return SptVersionRange.TryParse(constraint, out var bounds) ? bounds.Contains(version) : null;
    }

    private static Version? ParseVersion(string raw)
    {
        // Drop any pre-release/build suffix (e.g. "3.11.4-dev" -> "3.11.4").
        var core = raw.Split('-', 2)[0];
        var parts = core.Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var major)) return null;

        int Part(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;

        return new Version(major, Part(1), Part(2), Part(3));
    }
}
