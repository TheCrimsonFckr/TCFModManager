using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// A mod's installed addons, for taking them along when the mod is removed, disabled or enabled
// (OPEN-31 R2, R3, R5). Which mod an addon belongs to comes from the addon catalog, so the caller
// passes it in as parentOf.
//
public static class AddonFamily
{
    public static IReadOnlyList<InstalledModRecord> InstalledAddons(
        int parentModId, IEnumerable<InstalledModRecord> records, Func<int, int?> parentOf) =>
        [.. records
            .Where(r => r is { IsAddon: true, IsAppManaged: true } && parentOf(r.ModId) == parentModId)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];

    public static IReadOnlyList<InstalledModRecord> DisabledWithParent(
        int parentModId, IEnumerable<InstalledModRecord> records, Func<int, int?> parentOf) =>
        [.. InstalledAddons(parentModId, records, parentOf).Where(r => r.DisabledWithParent)];

    // The scanned shares those addons hold in their parent's folder.
    public static IReadOnlyList<InstalledMod> EntriesOf(IEnumerable<InstalledMod> scanned, IEnumerable<InstalledModRecord> addons)
    {
        var ids = addons.Where(a => a.IsAddon).Select(a => a.ModId).ToHashSet();
        return [.. scanned.Where(m => m is { ShareOwnerIsAddon: true, ShareOwnerModId: { } id } && ids.Contains(id))];
    }
}
