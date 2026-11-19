using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-52: the Core halves of the quick wins.
public class OpenQuickWinsTests
{
    [Theory]
    [InlineData("1.2.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("1.2.0", "1.2.1", false)]
    [InlineData("1.2.0", "1.3.0-beta", false)]
    [InlineData("1.2.0-hotfix", "1.2.0", true)]
    [InlineData(null, "1.2.0", false)]
    [InlineData("1.2.0", null, false)]
    public void SkippedUpdates_CoverTheSkippedVersionAndOlder(string? skipped, string? update, bool covered) =>
        Assert.Equal(covered, SkippedUpdates.Covers(skipped, update));

    [Fact]
    public void SkippedUpdates_KeepModsAndAddonsApart() =>
        Assert.NotEqual(SkippedUpdates.Key(false, 12), SkippedUpdates.Key(true, 12));

    [Theory]
    [InlineData("https://sp-mod.com/mod/791/sain", SpModLinkKind.Mod, 791)]
    [InlineData("sp-mod.com/mod/791", SpModLinkKind.Mod, 791)]
    [InlineData("  https://www.sp-mod.com/addon/98/some-addon?tab=versions  ", SpModLinkKind.Addon, 98)]
    [InlineData("https://sp-mod.com/list/12/my-list", SpModLinkKind.List, 12)]
    public void SpModPageLink_ReadsModAddonAndListLinks(string text, SpModLinkKind kind, int id)
    {
        var link = SpModPageLink.Parse(text);
        Assert.NotNull(link);
        Assert.Equal(kind, link!.Value.Kind);
        Assert.Equal(id, link.Value.Id);
    }

    [Theory]
    [InlineData("https://example.com/mod/791/sain")]
    [InlineData("https://sp-mod.com/user/62485/someone")]
    [InlineData("https://sp-mod.com/mod/0/zero")]
    [InlineData("just some text")]
    [InlineData("https://sp-mod.com/mod/791\nhttps://sp-mod.com/mod/792")]
    [InlineData(null)]
    public void SpModPageLink_IgnoresAnythingElse(string? text) => Assert.Null(SpModPageLink.Parse(text));

    private const string CoreJson = """
        {
          "fixes": {
            "fixShotgunDispersion": true,
            "removeModItemsFromProfile": false,
            "removeInvalidTradersFromProfile": false
          }
        }
        """;

    [Fact]
    public void ProfileFixes_ReadAndTurnOnOnlyTheTwoSwitches()
    {
        Assert.Equal(SptProfileFixState.Off, SptProfileFixes.StateOf(CoreJson));

        var on = SptProfileFixes.TurnedOn(CoreJson);
        Assert.NotNull(on);
        Assert.Equal(SptProfileFixState.On, SptProfileFixes.StateOf(on!));
        Assert.Equal(CoreJson.Replace("false", "true"), on);
    }

    [Fact]
    public void ProfileFixes_LeaveAFileWithoutBothSwitchesAlone()
    {
        const string partial = """{ "fixes": { "removeModItemsFromProfile": false } }""";
        Assert.Equal(SptProfileFixState.NotFound, SptProfileFixes.StateOf(partial));
        Assert.Null(SptProfileFixes.TurnedOn(partial));
    }

    [Fact]
    public void ProfileFixes_TurnOnKeepsTheOldFileBeside()
    {
        var folder = Directory.CreateTempSubdirectory("tcfmm-profilefix-");
        try
        {
            var path = Path.Combine(folder.FullName, "core.json");
            File.WriteAllText(path, CoreJson);

            Assert.Equal(SptProfileFixOutcome.TurnedOn, SptProfileFixes.TurnOn(path));
            Assert.Equal(SptProfileFixState.On, SptProfileFixes.Read(path));
            Assert.Equal(CoreJson, File.ReadAllText(path + SptProfileFixes.BackupSuffix));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void FreeSpace_NeedsThreeTimesTheDownloadPlusAMargin()
    {
        const long gb = 1024L * 1024 * 1024;
        var shortfall = FreeSpaceCheck.Find(2 * gb, [Path.Combine(Path.GetTempPath(), "x")], _ => 6 * gb);
        Assert.NotNull(shortfall);
        Assert.Equal(6 * gb + FreeSpaceCheck.MarginBytes, shortfall!.RequiredBytes);

        Assert.Null(FreeSpaceCheck.Find(2 * gb, [Path.GetTempPath()], _ => 7 * gb));
        Assert.Null(FreeSpaceCheck.Find(0, [Path.GetTempPath()], _ => 0));
        Assert.Null(FreeSpaceCheck.Find(2 * gb, [Path.GetTempPath()], _ => null));
    }

    [Fact]
    public void Groups_MergeMovesTheModsAndDropsTheGroup()
    {
        var keep = new ModGroup { Id = Guid.NewGuid(), Name = "Weapons" };
        var gone = new ModGroup { Id = Guid.NewGuid(), Name = "weapons" };
        var data = new ModGroupData { Groups = [keep, gone] };
        data.Assignments["a"] = gone.Id;
        data.Assignments["b"] = keep.Id;

        ModGroupStore.Merge(data, gone.Id, keep.Id);

        Assert.Equal([keep.Id], data.Groups.Select(g => g.Id));
        Assert.Equal(keep.Id, data.Assignments["a"]);
        Assert.Equal(keep.Id, data.Assignments["b"]);
    }

    [Fact]
    public void Notes_AreKeptByFolderNameAndClearedWhenEmpty()
    {
        var folder = Directory.CreateTempSubdirectory("tcfmm-notes-");
        try
        {
            var store = new ModNoteStore(Path.Combine(folder.FullName, "mod_notes.json"));
            store.Set("SAIN", "  needs a config change ");
            Assert.Equal("needs a config change", store.For("sain"));

            store.Set("SAIN", "  ");
            Assert.Null(store.For("SAIN"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static InstalledMod Client(string name, string guid, bool disabled = false, params ModDependencyRef[] dependencies) =>
        new()
        {
            Name = name,
            Guid = guid,
            Target = InstalledModTarget.Client,
            FolderPath = Path.Combine("C:", "SPT", "BepInEx", disabled ? "plugins.disabled" : "plugins", name),
            IsDisabled = disabled,
            Dependencies = dependencies,
        };

    [Fact]
    public void MissingFor_NamesAHardNeedThatIsAbsentOrOnlyDisabled()
    {
        var off = Client("Library", "com.author.library", disabled: true);
        var consumer = Client("Consumer", "com.author.consumer", false,
            new ModDependencyRef("com.author.library", IsSoft: false),
            new ModDependencyRef("com.author.gone", IsSoft: false),
            new ModDependencyRef("com.author.optional", IsSoft: true),
            new ModDependencyRef("com.SPT.core", IsSoft: false),
            new ModDependencyRef("com.author.subfolder", IsSoft: false));

        var graph = ModDependencyGraph.Build([off, consumer]);
        var missing = graph.MissingFor([consumer], new HashSet<string>(["com.author.subfolder"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["com.author.library", "com.author.gone"], missing.Select(m => m.Identifier));
        Assert.Same(off, missing[0].DisabledProvider);
        Assert.Null(missing[1].DisabledProvider);
    }

    [Fact]
    public void MissingFor_SaysNothingForADisabledMod()
    {
        var consumer = Client("Consumer", "com.author.consumer", true, new ModDependencyRef("com.author.gone", IsSoft: false));
        var graph = ModDependencyGraph.Build([consumer]);
        Assert.Empty(graph.MissingFor([consumer], new HashSet<string>()));
    }
}
