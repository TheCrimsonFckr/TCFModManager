using System.Diagnostics.CodeAnalysis;

namespace TCFModManager.Core.Services;

// What kind of change one version represents over another, read off the position of the first
// segment that differs. Drives what the self-updater tells the user an update actually is.
public enum VersionChangeKind
{
    // The candidate is the same as, or older than, what's installed.
    None,

    // x.x.CHANGE - a bug fix release.
    Patch,

    // x.CHANGE.x - new features.
    Minor,

    // CHANGE.x.x - a major update.
    Major,
}

//
// A strict major.minor.patch[-prerelease] version, used for this app's own releases.
//
// Deliberately separate from ModVersionComparer, which handles arbitrary mod authors' version
// strings and is lax on purpose (variable segment count, pre-release suffix thrown away entirely).
// This app's own releases are the one case where the format is known and both things it throws
// away matter: which segment changed is what tells the user whether an update is a bug fix or a
// major release, and the pre-release suffix is what distinguishes 1.3.0-beta from 1.3.0.
//
public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<SemanticVersion>
{
    public static bool TryParse(string? raw, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var text = raw.Trim().TrimStart('v', 'V');

        // Build metadata ("+abc123") never affects precedence, so it's dropped before anything else.
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];

        var dash = text.IndexOf('-');
        var preRelease = dash >= 0 ? text[(dash + 1)..] : null;
        if (dash >= 0) text = text[..dash];
        if (string.IsNullOrEmpty(preRelease)) preRelease = null;

        var parts = text.Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var major)) return false;

        int Segment(int index) =>
            index < parts.Length && int.TryParse(parts[index], out var value) ? value : 0;

        version = new SemanticVersion(major, Segment(1), Segment(2), preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var numeric = Major.CompareTo(other.Major);
        if (numeric != 0) return numeric;

        numeric = Minor.CompareTo(other.Minor);
        if (numeric != 0) return numeric;

        numeric = Patch.CompareTo(other.Patch);
        if (numeric != 0) return numeric;

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    // SemVer precedence: a version WITHOUT a pre-release suffix outranks the same numbers WITH one,
    // so 1.3.0 is newer than 1.3.0-beta. This is the whole reason this type exists rather than
    // reusing ModVersionComparer - the first stable release after a beta of the same number would
    // otherwise read as "no update available".
    private static int ComparePreRelease(string? left, string? right)
    {
        if (left is null && right is null) return 0;
        if (left is null) return 1;
        if (right is null) return -1;

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');

        for (var i = 0; i < Math.Max(leftParts.Length, rightParts.Length); i++)
        {
            // A shorter run of identifiers ranks lower when everything before it is equal:
            // 1.0.0-beta precedes 1.0.0-beta.2.
            if (i >= leftParts.Length) return -1;
            if (i >= rightParts.Length) return 1;

            var leftIsNumeric = int.TryParse(leftParts[i], out var leftNumber);
            var rightIsNumeric = int.TryParse(rightParts[i], out var rightNumber);

            // Numeric identifiers compare numerically (so beta.9 precedes beta.10 rather than
            // sorting after it as text would), and always rank below alphanumeric ones.
            if (leftIsNumeric && rightIsNumeric)
            {
                var numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0) return numeric;
                continue;
            }

            if (leftIsNumeric) return -1;
            if (rightIsNumeric) return 1;

            var text = string.CompareOrdinal(leftParts[i], rightParts[i]);
            if (text != 0) return text < 0 ? -1 : 1;
        }

        return 0;
    }

    public override string ToString() =>
        PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    //
    // Classifies what <paramref name="candidate"/> would be as an update over
    // <paramref name="installed"/>. Null when either string can't be parsed, which callers treat as
    // "don't claim there's an update" rather than guessing.
    //
    public static VersionChangeKind? Classify(string? installed, string? candidate)
    {
        if (!TryParse(installed, out var from) || !TryParse(candidate, out var to)) return null;

        if (to.Value.CompareTo(from.Value) <= 0) return VersionChangeKind.None;

        if (to.Value.Major != from.Value.Major) return VersionChangeKind.Major;
        if (to.Value.Minor != from.Value.Minor) return VersionChangeKind.Minor;

        // Includes the case where only the pre-release suffix moved (1.3.0-beta -> 1.3.0, or
        // -beta -> -beta.2). Same numbers, so nothing new is promised: a fix-level release.
        return VersionChangeKind.Patch;
    }
}
