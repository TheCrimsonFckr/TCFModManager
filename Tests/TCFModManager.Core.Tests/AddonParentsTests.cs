using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// As on Chris's G: install, 2026-11-26: Variant Core Slot Picture (addon 61) has a folder of its own
// and is an addon for DefinitiveWeaponVariants (mod 1000); the Satellite map addons sit in Satellite's
// own folder.
public class AddonParentsTests
{
    private const string Install = @"G:\Single Player Tarkov 4.1 Server";

    private static InstalledMod Server(string folder) => new()
    {
        Name = folder,
        Target = InstalledModTarget.Server,
        FolderPath = Path.Combine(Install, "SPT_Runtime", "user", "mods", folder),
    };

    private static InstalledModRecord Record(int id, bool addon, params string[] files) => new()
    {
        ModId = id,
        IsAddon = addon,
        Name = id.ToString(),
        Version = "1.0.0",
        InstalledAt = DateTimeOffset.UnixEpoch,
        Files = [.. files],
    };

    private static readonly InstalledModRecord[] Records =
    [
        Record(1000, false, "SPT_Runtime/user/mods/DefinitiveWeaponVariants/DefinitiveWeaponVariants.dll"),
        Record(61, true, "SPT_Runtime/user/mods/DWVCoreSlotPicture/DWVCoreSlotPicture.dll", "SPT_Runtime/user/mods/DWVCoreSlotPicture/db/10_Slots/mod_core.png"),
        Record(2000, false, "SPT_Runtime/user/mods/Satellite/Satellite.dll"),
        Record(200, true, "SPT_Runtime/user/mods/Satellite/maps/icebreaker/meta.json"),
    ];

    private static int? ParentOf(int addon) => addon switch { 61 => 1000, 200 => 2000, _ => null };

    [Fact]
    public void An_addon_with_a_folder_of_its_own_is_tied_to_its_parent()
    {
        var picture = Server("DWVCoreSlotPicture");
        var dwv = Server("DefinitiveWeaponVariants");
        var satellite = Server("Satellite");

        var link = Assert.Single(AddonParents.Find([picture, dwv, satellite], Install, Records, ParentOf));

        Assert.Same(picture, link.Addon);
        Assert.Same(dwv, link.Parent);
        Assert.Equal(61, link.AddonId);
        Assert.Equal(1000, link.ParentId);
    }

    [Fact]
    public void The_search_never_has_an_addon_on_without_its_parent()
    {
        var picture = Server("DWVCoreSlotPicture");
        var dwv = Server("DefinitiveWeaponVariants");
        var other = Server("Other");
        IReadOnlyList<InstalledMod> scanned = [picture, other, dwv];

        var (mods, needs) = BrokenModSearch.Suspects(scanned, AddonParents.Find(scanned, Install, Records, ParentOf));

        var addon = mods.ToList().IndexOf(picture);
        var parent = mods.ToList().IndexOf(dwv);
        Assert.True(parent < addon);
        Assert.Contains(parent, needs(addon));

        var (plain, plainNeeds) = BrokenModSearch.Suspects(scanned);
        Assert.Empty(plainNeeds(plain.ToList().IndexOf(picture)));
    }
}
