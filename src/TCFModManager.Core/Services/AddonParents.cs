using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>An installed addon with a folder of its own, and the installed mod it is an addon for.</summary>
public sealed record AddonParent(InstalledMod Addon, int AddonId, InstalledMod Parent, int ParentId);

//
// Which installed mods are addons of which, from the install records: a mod folder placed by an
// addon's record belongs to that addon, and its parent (from sp-mod's addon listing) is the mod
// whose record placed the parent's folder. An addon installed into its parent's own folder has no
// folder of its own and needs no link - it goes wherever the folder goes.
//
public static class AddonParents
{
    public static IReadOnlyList<AddonParent> Find(
        IReadOnlyList<InstalledMod> mods,
        string installPath,
        IEnumerable<InstalledModRecord> records,
        Func<int, int?> parentOfAddon)
    {
        var ownership = new ModOwnership(records);
        if (ownership.IsEmpty) return [];

        var owners = mods.Select(m => ownership.RecordsIn(installPath, Relative(installPath, m.FolderPath))).ToList();

        var byParentRecord = new Dictionary<int, InstalledMod>();
        for (var i = 0; i < mods.Count; i++)
            foreach (var record in owners[i].Where(r => !r.IsAddon))
                byParentRecord.TryAdd(record.ModId, mods[i]);

        var links = new List<AddonParent>();
        for (var i = 0; i < mods.Count; i++)
        {
            var own = owners[i].Where(r => !r.IsAddon).Select(r => r.ModId).ToHashSet();
            foreach (var addon in owners[i].Where(r => r.IsAddon))
            {
                if (parentOfAddon(addon.ModId) is not { } parentId || own.Contains(parentId)) continue;
                if (!byParentRecord.TryGetValue(parentId, out var parent) || ReferenceEquals(parent, mods[i])) continue;

                links.Add(new AddonParent(mods[i], addon.ModId, parent, parentId));
            }
        }

        return links;
    }

    private static string Relative(string installPath, string path) =>
        Path.GetRelativePath(installPath, path).Replace('\\', '/');
}
