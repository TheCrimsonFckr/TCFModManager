namespace TCFModManager.Core.Services;

//
// Compares two loosely-formatted version strings (an installed mod's version vs. the latest one
// published on sp-mod.com) to determine whether an update is available. Not a full SemVer
// implementation: variable segment count, tolerant of a leading "v"/"V", build metadata ("+...")
// ignored.
//
// Labels after a "-" count. A pre-release sorts below its release (1.2.0-beta then 1.2.0 is an
// update) and two labels on the same numbers compare part by part, numbers as numbers (beta.2 before
// beta.10, rc2 before rc10). The words authors use for a fix AFTER a release - 1.2.0-hotfix,
// 1.2.0-fix2, 1.2.0-patch1 - sort above the release instead, which is what every author using them
// means.
//
// A version read off a DLL can't carry a label, so comparing one against a published version uses
// the Numbers variants, which look at the numbers alone.
//
public static class ModVersionComparer
{
    // True if <paramref name="latest"/> is a newer version than <paramref name="installed"/>.
    // Null when either is missing or unparsable.
    public static bool? IsUpdateAvailable(string? installed, string? latest) =>
        Compare(latest, installed) is { } order ? order > 0 : null;

    // Negative when a is older than b, zero when the same version, positive when newer; null when
    // either can't be read.
    public static int? Compare(string? a, string? b)
    {
        if (Parse(a) is not { } left || Parse(b) is not { } right) return null;

        var numbers = left.Numbers.CompareTo(right.Numbers);
        return numbers != 0 ? Math.Sign(numbers) : CompareLabels(left.Label, right.Label);
    }

    // As IsUpdateAvailable, on the numbers alone - for a version read off a file.
    public static bool? IsUpdateAvailableByNumbers(string? installed, string? latest) =>
        Parse(installed) is { } left && Parse(latest) is { } right ? right.Numbers > left.Numbers : null;

    // True when both carry the same numbers, whatever their labels: "1.2.0.0" and "1.2.0-beta".
    public static bool SameNumbers(string? a, string? b) =>
        Parse(a) is { } left && Parse(b) is { } right && left.Numbers == right.Numbers;

    // True when a is a later breaking line than b: a higher major, or for 0.x a higher minor. Null
    // when either can't be read.
    public static bool? IsLaterMajor(string? a, string? b)
    {
        if (Parse(a) is not { } left || Parse(b) is not { } right) return null;

        var (l, r) = (left.Numbers, right.Numbers);
        if (l.Major != r.Major) return l.Major > r.Major;
        return l.Major == 0 && l.Minor > r.Minor;
    }

    private static readonly string[] PostReleaseWords = ["hotfix", "fix", "patch", "hf", "post"];

    // -1 below the plain release, 0 the release itself, 1 a fix after it.
    private static int Rank(string[] label) =>
        label.Length == 0 ? 0
        : PostReleaseWords.Any(w => SplitTrailingNumber(label[0]).Word.Equals(w, StringComparison.OrdinalIgnoreCase)) ? 1
        : -1;

    private static int CompareLabels(string[] a, string[] b)
    {
        var rank = Rank(a).CompareTo(Rank(b));
        if (rank != 0 || a.Length == 0) return rank;

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var order = ComparePart(a[i], b[i]);
            if (order != 0) return order;
        }

        return a.Length.CompareTo(b.Length);
    }

    // Numbers below words; a word with a number on the end ("rc2", "hotfix10") by the word, then the
    // number as a number.
    private static int ComparePart(string a, string b)
    {
        var aIsNumber = int.TryParse(a, out var an);
        var bIsNumber = int.TryParse(b, out var bn);
        if (aIsNumber || bIsNumber)
            return aIsNumber && bIsNumber ? Math.Sign(an.CompareTo(bn)) : aIsNumber ? -1 : 1;

        var (aWord, aNumber) = SplitTrailingNumber(a);
        var (bWord, bNumber) = SplitTrailingNumber(b);

        var words = string.Compare(aWord, bWord, StringComparison.OrdinalIgnoreCase);
        return words != 0 ? Math.Sign(words) : Math.Sign((aNumber ?? -1).CompareTo(bNumber ?? -1));
    }

    private static (string Word, int? Number) SplitTrailingNumber(string part)
    {
        var end = part.Length;
        while (end > 0 && char.IsDigit(part[end - 1])) end--;
        var word = part[..end].TrimEnd('-', '_');

        return end < part.Length && int.TryParse(part[end..], out var n) ? (word, n) : (word, null);
    }

    private static (Version Numbers, string[] Label)? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = raw.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        var halves = text.Split('-', 2);
        var parts = halves[0].Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var major)) return null;

        int Part(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;

        var label = halves.Length > 1 ? halves[1].Split('.', StringSplitOptions.RemoveEmptyEntries) : [];

        return (new Version(major, Part(1), Part(2), Part(3)), label);
    }
}
