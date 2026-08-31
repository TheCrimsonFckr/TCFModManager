using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

// The version window a constraint allows. MaxExclusive is the first version it rejects.
public readonly record struct SptVersionBounds(Version? Min, bool MinExclusive, Version? MaxExclusive, Version? Exact)
{
    // True when <paramref name="version"/> falls inside this window.
    //
    // SPT doesn't break mod compatibility between patches within one major.minor line (confirmed
    // by Chris against real examples - SAIN "~4.1.3", NoMenuFPSLimit bare ">=4.1.3", both showing
    // incompatible against an earlier-4.1 install that should count). So a constraint's LOWER bound
    // only tells us which release line a mod targets, never which patch of that line - "4.1.2",
    // "~4.1.3" and a bare ">=4.1.3" all have to run on SPT 4.1.1 too. An UPPER bound is still a real,
    // deliberate signal though: several mods in Chris's catalog (HandsAreNotBusy/QuickSellFlea/
    // LetMeRightClick capped "&gt;=4.1.0 &lt;4.1.3", "Temporary Fixes"/LoadBundleFaster capped
    // "&gt;=4.0.0 &lt;=4.0.13") explicitly stop supporting a line partway through, and that has to keep
    // failing on the later patches it names.
    public bool Contains(Version version)
    {
        // A bare exact constraint ("4.1.2", no operator) matches any patch of its own line, in
        // both directions - it's just the one release the author happened to test with.
        if (Exact is { } exact)
        {
            return version.Major == exact.Major && version.Minor == exact.Minor;
        }

        // Every other operator: if this version is on the same line as the constraint's floor,
        // the floor itself is ignored (that's the soft-pin relaxation) and only a real, explicit
        // upper-bound cutoff can still exclude it. A version on a different line falls through to
        // the ordinary min/max check below.
        if (Min is { } min && !MinExclusive && version.Major == min.Major && version.Minor == min.Minor)
        {
            return MaxExclusive is null || version < MaxExclusive;
        }

        if (Min is { } min2 && (MinExclusive ? version <= min2 : version < min2)) return false;
        if (MaxExclusive is { } max && version >= max) return false;
        return true;
    }

    //
    // The same window read literally, with none of the SPT release-line relaxation Contains applies.
    // For a constraint written against another MOD's version - an addon's mod_version_constraint -
    // where a bare "1.7.0" means that release and ">=1.5.3" means 1.5.3 or newer, full stop. Mod
    // authors do break things between patch releases; SPT is the special case, not the rule.
    //
    public bool Allows(Version version)
    {
        if (Min is { } min && (MinExclusive ? version <= min : version < min)) return false;
        if (MaxExclusive is { } max && version >= max) return false;
        return true;
    }
}

// 
// Parses the SemVer range strings sp-mod.com puts on mod versions (spt_version_constraint) into
// the window of versions they allow, and answers questions about that window. Handles the
// documented operators plus wildcard forms ("4.0.*", "4.x"), which the Forge does publish.
// 
public static class SptVersionRange
{
    private static readonly Regex ClausePattern = new(
        @"^\s*(\^|~|>=|<=|>|<|=)?\s*([0-9]+(?:\.(?:[0-9]+|\*|[xX])){0,3}|\*)\s*$",
        RegexOptions.Compiled);

    // Parses every clause and intersects them. False when the constraint is missing or has
    // a clause this doesn't understand.
    public static bool TryParse(string? constraint, out SptVersionBounds bounds)
    {
        bounds = default;
        if (string.IsNullOrWhiteSpace(constraint)) return false;

        Version? min = null;
        Version? maxExclusive = null;
        Version? exact = null;
        var minExclusive = false;

        var clauses = constraint.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (clauses.Length == 0) return false;

        foreach (var clause in clauses)
        {
            // A bare wildcard means any version at all.
            if (clause is "*" or "x" or "X")
            {
                RaiseMin(ref min, ref minExclusive, new Version(0, 0, 0, 0), exclusive: false);
                continue;
            }

            var match = ClausePattern.Match(clause);
            if (!match.Success) return false;

            var op = match.Groups[1].Success ? match.Groups[1].Value : "=";
            if (!TryParseOperand(match.Groups[2].Value, out var value, out var wildcardCeiling)) return false;

            // A wildcard operand is a range in its own right ("4.0.*" is the whole 4.0 line), so it
            // overrides the operator's usual meaning when no explicit operator was given.
            if (wildcardCeiling is not null && !match.Groups[1].Success)
            {
                RaiseMin(ref min, ref minExclusive, value, exclusive: false);
                LowerMax(ref maxExclusive, wildcardCeiling);
                continue;
            }

            switch (op)
            {
                case "^":
                    RaiseMin(ref min, ref minExclusive, value, exclusive: false);
                    LowerMax(ref maxExclusive, wildcardCeiling ?? NextMajor(value));
                    break;
                case "~":
                    RaiseMin(ref min, ref minExclusive, value, exclusive: false);
                    LowerMax(ref maxExclusive, wildcardCeiling ?? NextMinor(value));
                    break;
                case ">=":
                    RaiseMin(ref min, ref minExclusive, value, exclusive: false);
                    break;
                case ">":
                    RaiseMin(ref min, ref minExclusive, value, exclusive: true);
                    break;
                case "<":
                    LowerMax(ref maxExclusive, value);
                    break;
                case "<=":
                    LowerMax(ref maxExclusive, wildcardCeiling ?? NextPatch(value));
                    break;
                default:
                    exact = value;
                    RaiseMin(ref min, ref minExclusive, value, exclusive: false);
                    LowerMax(ref maxExclusive, NextPatch(value));
                    break;
            }
        }

        if (min is null && maxExclusive is null) return false;

        bounds = new SptVersionBounds(min, minExclusive, maxExclusive, exact);
        return true;
    }

    // 
    // True when the constraint allows any version in the given major.minor release line - e.g.
    // "^4.0.13" spans the 4.0 line and every line above it, while "~4.1.1" spans only 4.1.
    // A constraint this can't parse returns false; callers decide what an unreadable mod means.
    // 
    public static bool IntersectsReleaseLine(string? constraint, int major, int minor) =>
        TryParse(constraint, out var bounds) && Intersects(bounds, major, minor);

    // 
    // The combined window of every constraint that touches the given release line, clamped to that
    // line. Null when none of them do. Used to describe what a mod supports on one SPT line.
    // 
    public static SptVersionBounds? UnionForLine(IEnumerable<string?> constraints, int major, int minor)
    {
        var lineStart = new Version(major, minor, 0, 0);
        var lineEnd = new Version(major, minor + 1, 0, 0);

        Version? min = null;
        Version? maxExclusive = null;
        var found = false;

        foreach (var constraint in constraints)
        {
            if (!TryParse(constraint, out var bounds) || !Intersects(bounds, major, minor)) continue;

            found = true;

            var clampedMin = bounds.Min is { } m && m > lineStart ? m : lineStart;
            var clampedMax = bounds.MaxExclusive is { } x && x < lineEnd ? x : lineEnd;

            if (min is null || clampedMin < min) min = clampedMin;
            if (maxExclusive is null || clampedMax > maxExclusive) maxExclusive = clampedMax;
        }

        return found ? new SptVersionBounds(min, false, maxExclusive, null) : null;
    }

    private static bool Intersects(SptVersionBounds bounds, int major, int minor)
    {
        var lineStart = new Version(major, minor, 0, 0);
        var lineEnd = new Version(major, minor + 1, 0, 0);

        if (bounds.MaxExclusive is { } max && max <= lineStart) return false;
        if (bounds.Min is { } min && min >= lineEnd) return false;

        return true;
    }

    private static void RaiseMin(ref Version? min, ref bool minExclusive, Version value, bool exclusive)
    {
        if (min is not null && value <= min) return;

        min = value;
        minExclusive = exclusive;
    }

    private static void LowerMax(ref Version? maxExclusive, Version value)
    {
        if (maxExclusive is null || value < maxExclusive) maxExclusive = value;
    }

    internal static Version NextMajor(Version v) => new(v.Major + 1, 0, 0, 0);

    internal static Version NextMinor(Version v) => new(v.Major, v.Minor + 1, 0, 0);

    internal static Version NextPatch(Version v) => new(v.Major, v.Minor, v.Build + 1, 0);

    // 
    // Parses a clause's version operand. <paramref name="wildcardCeiling"/> is the first version
    // above the wildcard's range ("4.0.*" yields 4.0.0 with a ceiling of 4.1.0) and is null for a
    // fully specified version.
    // 
    private static bool TryParseOperand(string raw, out Version value, out Version? wildcardCeiling)
    {
        value = new Version(0, 0, 0, 0);
        wildcardCeiling = null;

        // Drop any pre-release/build suffix (e.g. "3.11.4-dev" -> "3.11.4").
        var core = raw.Split('-', 2)[0];
        var parts = core.Split('.');

        var numbers = new int[4];
        var wildcardAt = -1;

        for (var i = 0; i < parts.Length && i < 4; i++)
        {
            if (parts[i] is "*" or "x" or "X")
            {
                wildcardAt = i;
                break;
            }

            if (!int.TryParse(parts[i], out numbers[i])) return false;
        }

        value = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);

        if (wildcardAt < 0) return true;

        // A bare "*" is handled by the caller before it gets here.
        if (wildcardAt == 0) return false;

        wildcardCeiling = wildcardAt switch
        {
            1 => NextMajor(value),
            2 => NextMinor(value),
            _ => NextPatch(value),
        };

        return true;
    }
}
