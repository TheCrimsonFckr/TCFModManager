using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModDisableServiceTests : IDisposable
{
    private readonly string _installRoot;

    public ModDisableServiceTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "TCFModManagerDisableTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_installRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_installRoot)) Directory.Delete(_installRoot, recursive: true);
    }

    private string ServerMod(string name, bool disabled = false)
    {
        var dir = Path.Combine(_installRoot, "user", disabled ? "mods.disabled" : "mods", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), $$"""{ "name": "{{name}}", "version": "1.0.0" }""");
        return dir;
    }

    private static InstalledMod Scanned(string folderPath, string name, bool disabled) => new()
    {
        Name = name,
        Target = InstalledModTarget.Server,
        FolderPath = folderPath,
        IsDisabled = disabled,
    };

    [Fact]
    public void Apply_DisableMovesTheModIntoTheDisabledSibling()
    {
        var folder = ServerMod("SomeMod");

        var outcome = ModDisableService.Apply([Scanned(folder, "SomeMod", false)], disable: true);

        var expected = Path.Combine(_installRoot, "user", "mods.disabled", "SomeMod");
        Assert.Empty(outcome.Failed);
        Assert.Equal(expected, Assert.Single(outcome.Moved).To);
        Assert.True(Directory.Exists(expected));
        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(expected, "package.json")));
    }

    [Fact]
    public void Apply_EnableMovesItBack()
    {
        var folder = ServerMod("SomeMod", disabled: true);

        var outcome = ModDisableService.Apply([Scanned(folder, "SomeMod", true)], disable: false);

        var expected = Path.Combine(_installRoot, "user", "mods", "SomeMod");
        Assert.Empty(outcome.Failed);
        Assert.True(Directory.Exists(expected));
    }

    [Fact]
    public void Apply_SkipsModsAlreadyInTheRequestedState()
    {
        var folder = ServerMod("SomeMod");

        var outcome = ModDisableService.Apply([Scanned(folder, "SomeMod", false)], disable: false);

        Assert.Empty(outcome.Moved);
        Assert.Empty(outcome.Failed);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Apply_RefusesWhenSomethingIsAlreadyAtTheDestination()
    {
        var live = ServerMod("SomeMod");
        ServerMod("SomeMod", disabled: true);

        var outcome = ModDisableService.Apply([Scanned(live, "SomeMod", false)], disable: true);

        Assert.Empty(outcome.Moved);
        Assert.Equal("SomeMod", Assert.Single(outcome.Failed).ModName);
        Assert.True(Directory.Exists(live));
    }

    [Fact]
    public void Apply_MovesWhatItCanWhenOneModFails()
    {
        var first = ServerMod("First");
        var blocked = ServerMod("Blocked");
        ServerMod("Blocked", disabled: true);

        var outcome = ModDisableService.Apply(
            [Scanned(first, "First", false), Scanned(blocked, "Blocked", false)],
            disable: true);

        Assert.Single(outcome.Moved);
        Assert.Single(outcome.Failed);
        Assert.True(Directory.Exists(Path.Combine(_installRoot, "user", "mods.disabled", "First")));
    }

    [Fact]
    public void Apply_MovesALooseClientDll()
    {
        var plugins = Path.Combine(_installRoot, "BepInEx", "plugins");
        Directory.CreateDirectory(plugins);
        var dll = Path.Combine(plugins, "Loose.dll");
        File.WriteAllBytes(dll, [0x4D, 0x5A]);

        var mod = new InstalledMod
        {
            Name = "Loose",
            Target = InstalledModTarget.Client,
            FolderPath = dll,
            IsDisabled = false,
        };

        var outcome = ModDisableService.Apply([mod], disable: true);

        Assert.Empty(outcome.Failed);
        Assert.True(File.Exists(Path.Combine(_installRoot, "BepInEx", "plugins.disabled", "Loose.dll")));
        Assert.False(File.Exists(dll));
    }

    [Fact]
    public void Revert_PutsEverythingBackWhereItCameFrom()
    {
        var folder = ServerMod("SomeMod");
        var outcome = ModDisableService.Apply([Scanned(folder, "SomeMod", false)], disable: true);

        var reverted = ModDisableService.Revert(outcome.Moved);

        Assert.Empty(reverted.Failed);
        Assert.True(Directory.Exists(folder));
        Assert.False(Directory.Exists(Path.Combine(_installRoot, "user", "mods.disabled", "SomeMod")));
    }

    [Fact]
    public void Revert_SkipsAMoveThatNoLongerMatchesDisk()
    {
        var folder = ServerMod("SomeMod");
        var outcome = ModDisableService.Apply([Scanned(folder, "SomeMod", false)], disable: true);

        Directory.CreateDirectory(folder);

        var reverted = ModDisableService.Revert(outcome.Moved);

        Assert.Empty(reverted.Moved);
        Assert.Single(reverted.Failed);
    }

    [Fact]
    public void Apply_RemovesTheDisabledContainerOnceItIsEmpty()
    {
        var folder = ServerMod("SomeMod", disabled: true);
        var container = Path.Combine(_installRoot, "user", "mods.disabled");

        ModDisableService.Apply([Scanned(folder, "SomeMod", true)], disable: false);

        Assert.False(Directory.Exists(container));
    }

    [Fact]
    public void DuplicatePairs_MatchesOnTheExactPathNotJustTheName()
    {
        var live = ServerMod("SomeMod");
        var parked = ServerMod("SomeMod", disabled: true);

        var pair = Assert.Single(ModDisableService.DuplicatePairs(
            [Scanned(live, "SomeMod", false), Scanned(parked, "SomeMod", true)]));

        Assert.Equal(live, pair.Enabled.FolderPath);
        Assert.Equal(parked, pair.Disabled.FolderPath);
    }

    [Fact]
    public void DuplicatePairs_IgnoresAClientServerModWithOneHalfDisabled()
    {
        var plugins = Path.Combine(_installRoot, "BepInEx", "plugins", "BigMod");
        Directory.CreateDirectory(plugins);
        var parkedServer = ServerMod("BigMod", disabled: true);

        var mods = new[]
        {
            new InstalledMod { Name = "BigMod", Target = InstalledModTarget.Client, FolderPath = plugins, IsDisabled = false },
            Scanned(parkedServer, "BigMod", true),
        };

        Assert.Empty(ModDisableService.DuplicatePairs(mods));
    }

    [Fact]
    public void ResolveDuplicate_KeepingTheEnabledCopySetsTheOtherAside()
    {
        var live = ServerMod("SomeMod");
        var parked = ServerMod("SomeMod", disabled: true);
        var pair = new ModDuplicatePair(Scanned(live, "SomeMod", false), Scanned(parked, "SomeMod", true));

        var outcome = ModDisableService.ResolveDuplicate(_installRoot, pair, keepEnabled: true, Stamp);

        Assert.Empty(outcome.Failed);
        Assert.True(Directory.Exists(live));
        Assert.False(Directory.Exists(parked));
        Assert.Single(SetAsideFolders());
    }

    [Fact]
    public void ResolveDuplicate_KeepingTheDisabledCopyMovesItIntoPlace()
    {
        var live = ServerMod("SomeMod");
        File.WriteAllText(Path.Combine(live, "marker.txt"), "live");
        var parked = ServerMod("SomeMod", disabled: true);
        File.WriteAllText(Path.Combine(parked, "marker.txt"), "parked");

        var pair = new ModDuplicatePair(Scanned(live, "SomeMod", false), Scanned(parked, "SomeMod", true));

        var outcome = ModDisableService.ResolveDuplicate(_installRoot, pair, keepEnabled: false, Stamp);

        Assert.Empty(outcome.Failed);
        Assert.True(Directory.Exists(live));
        Assert.False(Directory.Exists(parked));
        Assert.Equal("parked", File.ReadAllText(Path.Combine(live, "marker.txt")));
        Assert.Single(SetAsideFolders());
    }

    [Fact]
    public void ResolveDuplicate_SetsNothingAsideInsideAModContainer()
    {
        var live = ServerMod("SomeMod");
        var parked = ServerMod("SomeMod", disabled: true);
        var pair = new ModDuplicatePair(Scanned(live, "SomeMod", false), Scanned(parked, "SomeMod", true));

        ModDisableService.ResolveDuplicate(_installRoot, pair, keepEnabled: true, Stamp);

        // The set-aside copy must not be picked up as an installed mod again.
        Assert.Single(InstalledModScanner.Scan(_installRoot));
    }

    [Fact]
    public void ResolveDuplicate_IsUndoneByRevert()
    {
        var live = ServerMod("SomeMod");
        var parked = ServerMod("SomeMod", disabled: true);
        var pair = new ModDuplicatePair(Scanned(live, "SomeMod", false), Scanned(parked, "SomeMod", true));

        var outcome = ModDisableService.ResolveDuplicate(_installRoot, pair, keepEnabled: false, Stamp);
        var reverted = ModDisableService.Revert(outcome.Moved);

        Assert.Empty(reverted.Failed);
        Assert.True(Directory.Exists(live));
        Assert.True(Directory.Exists(parked));
        Assert.Empty(SetAsideFolders());
    }

    private static readonly DateTimeOffset Stamp = new(2026, 8, 24, 12, 15, 0, TimeSpan.Zero);

    private string[] SetAsideFolders()
    {
        var root = Path.Combine(_installRoot, ".tcfmm-duplicates");
        return Directory.Exists(root) ? Directory.GetDirectories(root) : [];
    }

    [Fact]
    public void DuplicatedNames_FindsModsPresentInBothStates()
    {
        var mods = new[]
        {
            Scanned(Path.Combine("a", "SomeMod"), "SomeMod", false),
            Scanned(Path.Combine("b", "SomeMod"), "SomeMod", true),
            Scanned(Path.Combine("c", "Clean"), "Clean", false),
        };

        Assert.Equal("SomeMod", Assert.Single(ModDisableService.DuplicatedNames(mods)));
    }
}
