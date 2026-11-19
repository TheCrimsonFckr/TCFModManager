using System.Globalization;

namespace TCFModManager.Core.Services;

public static class SkippedUpdates
{
    public static string Key(bool isAddon, int id) =>
        (isAddon ? "addon:" : "mod:") + id.ToString(CultureInfo.InvariantCulture);

    public static bool Covers(string? skipped, string? update)
    {
        if (string.IsNullOrWhiteSpace(skipped) || string.IsNullOrWhiteSpace(update)) return false;
        if (string.Equals(skipped, update, StringComparison.OrdinalIgnoreCase)) return true;
        return ModVersionComparer.Compare(update, skipped) is <= 0;
    }
}
