using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModConfigDiscoveryTests : IDisposable
{
    private readonly string _installRoot;

    public ModConfigDiscoveryTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "TCFModManagerConfigDiscoveryTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_installRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_installRoot)) Directory.Delete(_installRoot, recursive: true);
    }

    // BepInEx\config\<name>.cfg, optionally inside a subfolder of it.
    private string ClientConfig(string fileName, string? subFolder = null)
    {
        var folder = ModConfigDiscovery.ClientConfigFolder(_installRoot);
        if (subFolder is not null) folder = Path.Combine(folder, subFolder);

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, "[General]\nEnabled = true\n");
        return path;
    }

    private string ServerModFolder(string name, bool disabled = false)
    {
        var dir = Path.Combine(_installRoot, "user", disabled ? "mods.disabled" : "mods", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), $$"""{ "name": "{{name}}" }""");
        return dir;
    }

    private static void WriteJson(string folder, params string[] segments)
    {
        var path = Path.Combine([folder, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
    }

    private static InstalledMod Client(string name, string? guid = null) => new()
    {
        Name = name,
        Guid = guid,
        Target = InstalledModTarget.Client,
        FolderPath = Path.Combine("BepInEx", "plugins", name),
    };

    private static InstalledMod Server(string name, string folderPath, bool disabled = false) => new()
    {
        Name = name,
        Target = InstalledModTarget.Server,
        FolderPath = folderPath,
        IsDisabled = disabled,
    };

    [Fact]
    public void Find_MatchesAClientConfigToThePluginGuidItIsNamedAfter()
    {
        ClientConfig("me.sol.sain.cfg");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("SAIN", "me.sol.sain")]));

        Assert.Equal(ModConfigSource.Client, entry.Source);
        Assert.Equal("SAIN", entry.ModName);
        Assert.Equal("me.sol.sain", entry.ModGuid);
        Assert.Equal(ModConfigFormat.BepInExCfg, entry.Format);
        Assert.Equal("BepInEx/config/me.sol.sain.cfg", entry.DisplayPath);
    }

    [Fact]
    public void Find_FallsBackToTheModsOwnFolderNameForAPluginThatNamesItsFileAfterItself()
    {
        ClientConfig("Lots of Loot.cfg");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("LotsOfLoot", "wtf.archangel.lotsofloot")]));

        Assert.Equal(ModConfigSource.Client, entry.Source);
        Assert.Equal("LotsOfLoot", entry.ModName);

        // The matched mod's GUID comes along whichever tier found it - this file just wasn't named
        // after it.
        Assert.Equal("wtf.archangel.lotsofloot", entry.ModGuid);
    }

    [Fact]
    public void Find_LeavesAConfigUnmatchedWhenTwoModsNormalizeToTheSameName()
    {
        ClientConfig("some-mod.cfg");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("SomeMod"), Client("Some Mod")]));

        Assert.Equal(ModConfigSource.Unmatched, entry.Source);
        Assert.Null(entry.ModName);
    }

    [Fact]
    public void Find_MatchesASubfolderOfBepInExConfigNamedAfterTheMod()
    {
        ClientConfig("weapons.cfg", subFolder: "Realism");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("Realism", "com.fontaine.realism")]));

        Assert.Equal(ModConfigSource.Client, entry.Source);
        Assert.Equal("Realism", entry.ModName);
    }

    [Fact]
    public void Find_ReportsAConfigNoInstalledPluginClaimsRatherThanHidingIt()
    {
        ClientConfig("com.someone.removedmod.cfg");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("SAIN", "me.sol.sain")]));

        Assert.Equal(ModConfigSource.Unmatched, entry.Source);
        Assert.Null(entry.ModName);
    }

    [Theory]
    [InlineData("BepInEx.cfg")]
    [InlineData("com.bepis.bepinex.configurationmanager.cfg")]
    public void Find_CallsBepInExsOwnConfigTheFrameworksRatherThanAMods(string fileName)
    {
        ClientConfig(fileName);

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, []));

        Assert.Equal(ModConfigSource.Framework, entry.Source);
        Assert.Null(entry.ModName);
    }

    [Fact]
    public void Find_TakesEveryJsonInsideAServerModsConfigFolder()
    {
        var folder = ServerModFolder("SVM");
        WriteJson(folder, "config", "config.json");
        WriteJson(folder, "config", "nested", "extra.json5");

        var entries = ModConfigDiscovery.Find(_installRoot, [Server("SVM", folder)]);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(ModConfigSource.Server, e.Source));
        Assert.All(entries, e => Assert.Equal("SVM", e.ModName));
        Assert.All(entries, e => Assert.Equal(ModConfigFormat.Json, e.Format));
    }

    [Fact]
    public void Find_TakesAConventionallyNamedConfigSittingAtTheModsRoot()
    {
        var folder = ServerModFolder("Waypoints");
        WriteJson(folder, "config.json");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Server("Waypoints", folder)]));

        Assert.Equal("config.json", entry.FileName);
    }

    [Fact]
    public void Find_IgnoresAServerModsManifestAndItsOwnData()
    {
        var folder = ServerModFolder("BigMod");

        // package.json is the manifest, not a config, and a mod's data folder is full of JSON that
        // is content rather than settings - offering to edit an item table as if it were a config
        // is worse than not listing it at all.
        WriteJson(folder, "db", "items.json");
        WriteJson(folder, "src", "mod.json");
        WriteJson(folder, "node_modules", "something", "config", "config.json");

        Assert.Empty(ModConfigDiscovery.Find(_installRoot, [Server("BigMod", folder)]));
    }

    [Fact]
    public void Find_LooksInsideAConfigFolderNestedInTheModsSource()
    {
        var folder = ServerModFolder("Nested");
        WriteJson(folder, "src", "config", "values.json");

        Assert.Single(ModConfigDiscovery.Find(_installRoot, [Server("Nested", folder)]));
    }

    [Fact]
    public void Find_FindsADisabledServerModsConfigWhereItNowSitsAndSaysItIsDisabled()
    {
        var folder = ServerModFolder("Parked", disabled: true);
        WriteJson(folder, "config", "config.json");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Server("Parked", folder, disabled: true)]));

        Assert.True(entry.IsModDisabled);
        Assert.Equal("user/mods.disabled/Parked/config/config.json", entry.DisplayPath);
    }

    [Fact]
    public void Find_OrdersModsBeforeTheFrameworkAndAnythingUnclaimedLast()
    {
        ClientConfig("me.sol.sain.cfg");
        ClientConfig("BepInEx.cfg");
        ClientConfig("com.someone.gone.cfg");

        var sources = ModConfigDiscovery.Find(_installRoot, [Client("SAIN", "me.sol.sain")]).Select(e => e.Source);

        Assert.Equal([ModConfigSource.Client, ModConfigSource.Framework, ModConfigSource.Unmatched], sources);
    }

    [Fact]
    public void Find_ReturnsNothingForAnInstallWithNoConfigsAtAll() =>
        Assert.Empty(ModConfigDiscovery.Find(_installRoot, [Client("SAIN", "me.sol.sain")]));
}
