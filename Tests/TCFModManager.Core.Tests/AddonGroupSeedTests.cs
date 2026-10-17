using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class AddonGroupSeedTests
{
    private static readonly Guid Camo = Guid.NewGuid();

    private static ModGroupData Data() => new()
    {
        Groups = [new ModGroup { Id = Camo, Name = "Camo" }],
        Assignments = new() { ["7bpencil.weaponcamoandstickers"] = Camo },
    };

    [Fact]
    public void ANewAddon_StartsInItsParentsGroup()
    {
        var data = Data();

        var seen = ModGroupStore.Seed(data, [("pride-flag-sticker-pack", "7Bpencil.WeaponCamoAndStickers")]);

        Assert.Equal(1, seen);
        Assert.Equal(Camo, data.Assignments["pride-flag-sticker-pack"]);
        Assert.Equal(["pride-flag-sticker-pack"], data.SeededAddons);
    }

    [Fact]
    public void AnAddonMovedToUngrouped_StaysThere()
    {
        var data = Data();
        ModGroupStore.Seed(data, [("pride-flag-sticker-pack", "7Bpencil.WeaponCamoAndStickers")]);
        data.Assignments.Remove("pride-flag-sticker-pack");

        var seen = ModGroupStore.Seed(data, [("pride-flag-sticker-pack", "7Bpencil.WeaponCamoAndStickers")]);

        Assert.Equal(0, seen);
        Assert.False(data.Assignments.ContainsKey("pride-flag-sticker-pack"));
    }

    [Fact]
    public void AnAddonAlreadyInAGroup_KeepsIt()
    {
        var data = Data();
        var other = Guid.NewGuid();
        data.Groups.Add(new ModGroup { Id = other, Name = "Other" });
        data.Assignments["pride-flag-sticker-pack"] = other;

        ModGroupStore.Seed(data, [("pride-flag-sticker-pack", "7Bpencil.WeaponCamoAndStickers")]);

        Assert.Equal(other, data.Assignments["pride-flag-sticker-pack"]);
    }

    [Fact]
    public void AnAddonWhoseParentIsUngrouped_IsSeenButStaysUngrouped_AndIsNotDraggedLater()
    {
        var data = Data();
        data.Assignments.Clear();

        ModGroupStore.Seed(data, [("pride-flag-sticker-pack", "7Bpencil.WeaponCamoAndStickers")]);
        data.Assignments["7bpencil.weaponcamoandstickers"] = Camo;
        ModGroupStore.Seed(data, [("pride-flag-sticker-pack", "7Bpencil.WeaponCamoAndStickers")]);

        Assert.False(data.Assignments.ContainsKey("pride-flag-sticker-pack"));
    }

    [Fact]
    public void AParentInADeletedGroup_SeedsNothing()
    {
        var data = Data();
        data.Groups.Clear();

        ModGroupStore.Seed(data, [("pride-flag-sticker-pack", "7Bpencil.WeaponCamoAndStickers")]);

        Assert.False(data.Assignments.ContainsKey("pride-flag-sticker-pack"));
    }
}
