using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// OPEN-12 F17: whether an install runs Fika - asked before installing a version sp-mod marks as not
// working with Fika. Any enabled half counts: Fika's client plugin (com.fika.core), its SPT 4.x server
// mod (com.fika.server), or an SPT 3.x server folder named like "fika-server". A headless install runs
// Fika too. A mod that only mentions Fika in its name doesn't count.
//
public static class FikaInstall
{
    public const string GuidPrefix = "com.fika.";

    // sp-mod's fika_compatibility for a version that doesn't work with Fika.
    public const string Incompatible = "incompatible";

    public static bool IsPresent(IEnumerable<InstalledMod> scanned) =>
        scanned.Any(m => !m.IsDisabled
            && (m.AllGuids.Any(g => g.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase))
                || (m.Target == InstalledModTarget.Server && IsFikaServerFolder(m.Name))));

    public static bool IsIncompatible(string? fikaCompatibility) =>
        string.Equals(fikaCompatibility?.Trim(), Incompatible, StringComparison.OrdinalIgnoreCase);

    private static bool IsFikaServerFolder(string name) =>
        name.StartsWith("fika", StringComparison.OrdinalIgnoreCase)
        && name.Contains("server", StringComparison.OrdinalIgnoreCase);
}
