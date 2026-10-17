using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModListMissingParentsTests
{
    private const int CamoId = 2658;

    private static readonly Dictionary<int, Addon> Addons = new()
    {
        [40] = new Addon
        {
            Id = 40, Name = "Essential Texture Pack", ModId = CamoId,
            Versions =
            [
                new AddonVersionSummary { Id = 91, Version = "1.1.0", ModVersionConstraint = "^1.4.0" },
                new AddonVersionSummary { Id = 92, Version = "1.1.1", ModVersionConstraint = "~1.17" },
            ],
        },
        [68] = new Addon { Id = 68, Name = "Pride Flag Sticker Pack", ModId = CamoId, Versions = [new AddonVersionSummary { Id = 130, Version = "1.0.0", ModVersionConstraint = "~1" }] },
    };

    private static readonly Dictionary<int, Mod> Catalog = new()
    {
        [CamoId] = new Mod
        {
            Id = CamoId, Name = "Camo And Stickers",
            Versions =
            [
                new ModVersionSummary { Id = 3, Version = "1.19.0", SptVersionConstraint = "~4.1", PublishedAt = Day(3) },
                new ModVersionSummary { Id = 2, Version = "1.17.2", SptVersionConstraint = "~4.1", PublishedAt = Day(2) },
                new ModVersionSummary { Id = 1, Version = "1.16.0", SptVersionConstraint = "~4.0", PublishedAt = Day(1) },
            ],
        },
    };

    [Fact]
    public void AnAddonWhoseParentIsntOnTheList_OffersTheParent_AtTheNewestForThisSpt()
    {
        var parents = Missing([Addon(68, "1.0.0", 130)]);

        var parent = Assert.Single(parents);
        Assert.Equal(CamoId, parent.ModId);
        Assert.Equal("Camo And Stickers", parent.Name);
        Assert.Equal("1.19.0", parent.Version);
        Assert.True(parent.IsParent);
        Assert.Equal(["Pride Flag Sticker Pack"], parent.NeededBy);
    }

    [Fact]
    public void TheParentsVersion_IsTheNewestEveryListedAddonAccepts()
    {
        var parent = Assert.Single(Missing([Addon(40, "1.1.1", 92), Addon(68, "1.0.0", 130)]));

        Assert.Equal("1.17.2", parent.Version);
        Assert.Equal(2, parent.VersionId);
        Assert.Equal(["Essential Texture Pack", "Pride Flag Sticker Pack"], parent.NeededBy.Order());
    }

    [Fact]
    public void AParentOnTheList_OrAlreadyReportedMissing_IsNotOfferedAgain()
    {
        var camo = new ModListEntry { Name = "Camo And Stickers", ModId = CamoId, Version = "1.19.0" };

        Assert.Empty(Missing([camo, Addon(68, "1.0.0", 130)]));
        Assert.Empty(Missing([Addon(68, "1.0.0", 130)], alreadyMissing: [CamoId]));
    }

    [Fact]
    public void AModWithTheAddonsId_IsNotMistakenForTheAddon()
    {
        var modNumbered68 = new ModListEntry { Name = "A mod numbered 68", ModId = 68, Version = "1.0.0" };

        Assert.Empty(Missing([modNumbered68]));
    }

    [Fact]
    public void AParentWithNothingForThisSpt_ComesBackWithNoVersion()
    {
        var parent = Assert.Single(Missing([Addon(68, "1.0.0", 130)], spt: "3.11.0"));

        Assert.Null(parent.Version);
        Assert.False(parent.HasVersion);
    }

    private static IReadOnlyList<SpModDependency> Missing(
        ModListEntry[] entries, string spt = "4.1.6", int[]? alreadyMissing = null) =>
        ModListDependencies.MissingParents(entries, Addons, Catalog, spt, (alreadyMissing ?? []).ToHashSet());

    private static ModListEntry Addon(int id, string version, int versionId) =>
        new() { Name = Addons[id].Name!, ModId = id, IsAddon = true, Version = version, VersionId = versionId };

    private static DateTimeOffset Day(int day) => new(2026, 10, day, 8, 0, 0, TimeSpan.Zero);
}
