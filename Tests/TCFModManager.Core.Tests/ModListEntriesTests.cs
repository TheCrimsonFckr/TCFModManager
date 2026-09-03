using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModListEntriesTests
{
    private static ModListEntry Entry(string name, int? modId = null, bool isAddon = false) =>
        new() { Name = name, ModId = modId, IsAddon = isAddon };

    [Fact]
    public void SameMod_MatchesOnModIdWhateverTheNameSays()
    {
        Assert.True(ModListEntries.SameMod(Entry("Realism", 1), Entry("SPT Realism Mod", 1)));
    }

    [Fact]
    public void SameMod_SeparatesAnAddonFromAModWithTheSameNumber()
    {
        // sp-mod.com numbers addons in their own sequence, so these are two unrelated things.
        Assert.False(ModListEntries.SameMod(Entry("Realism", 116), Entry("A preset", 116, isAddon: true)));
    }

    [Fact]
    public void SameMod_FallsBackToTheNameIgnoringCaseAndPadding()
    {
        Assert.True(ModListEntries.SameMod(Entry("  Hand-built thing "), Entry("hand-built THING")));
        Assert.False(ModListEntries.SameMod(Entry("Hand-built thing"), Entry("Something else")));
    }

    [Fact]
    public void SameMod_UsesTheNameWhenOnlyOneSideCarriesAnId()
    {
        // A mod added from the catalog and the folder the scanner found for it have only the name
        // in common until one of them resolves.
        Assert.True(ModListEntries.SameMod(Entry("Realism", 1), Entry("Realism")));
    }

    [Fact]
    public void Sorted_PutsEntriesInNameOrder()
    {
        var sorted = ModListEntries.Sorted([Entry("SAIN"), Entry("amanda"), Entry("Realism")]);

        Assert.Equal(["amanda", "Realism", "SAIN"], sorted.Select(e => e.Name));
    }

    [Fact]
    public void ForCatalogMod_NamesTheModWithoutPinningAVersion()
    {
        // Unpinned on purpose: a version invented from the cache would name a build nobody here has
        // run. No pin means the newest published, which is what the planner installs.
        var entry = ModListEntries.ForCatalogMod(2426, "  Realism  ", " com.fontaine.realism ");

        Assert.Equal("Realism", entry.Name);
        Assert.Equal(2426, entry.ModId);
        Assert.Equal("com.fontaine.realism", entry.Guid);
        Assert.Null(entry.Version);
        Assert.Null(entry.VersionId);
        Assert.False(entry.IsPinned);
        Assert.True(entry.IsResolved);
    }

    [Fact]
    public void ForCatalogMod_LeavesAnEmptyGuidNull()
    {
        Assert.Null(ModListEntries.ForCatalogMod(2426, "Realism", "   ").Guid);
    }
}
