using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The per-mod locations: a settings file nothing would find, a folder of the user's own documents, and
// data that only looks like config. One rule answers all three - see ModConfigFiles.
//
public class ModConfigLocationsTests : IDisposable
{
    private const string Mod = "user/mods/[SVM] Server Value Modifier";

    private readonly string _root;
    private readonly ModConfigOptionsStore _store;

    public ModConfigLocationsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TCFModManagerLocations_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
        _store = new ModConfigOptionsStore(Path.Combine(_root, "mod_configs.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static ModConfigOptions Options(
        IEnumerable<string>? userData = null,
        IEnumerable<string>? settings = null,
        IEnumerable<string>? exclude = null) => new()
    {
        UserData = [.. userData ?? []],
        Settings = [.. settings ?? []],
        Exclude = [.. exclude ?? []],
    };

    [Fact]
    public void ASettingsFileTheModKeepsElsewhereCounts()
    {
        var options = Options(settings: ["Loader/loader.json"]);

        Assert.False(ModConfigFiles.IsServerModConfig($"{Mod}/Loader/loader.json"));
        Assert.True(ModConfigFiles.IsServerModConfig($"{Mod}/Loader/loader.json", options));
    }

    // Opting a location in does not opt its extension in: an entry still has to name a config file.
    [Fact]
    public void AnOptedInPathStillNeedsAConfigExtension()
    {
        var options = Options(settings: ["Misc/MOTD.txt"]);

        Assert.False(ModConfigFiles.IsServerModConfig($"{Mod}/Misc/MOTD.txt", options));
    }

    [Fact]
    public void AUserDataFolderIsNotAConfig()
    {
        var options = Options(userData: ["Presets"]);

        Assert.True(ModConfigFiles.IsUserData($"{Mod}/Presets/custom_V2.json", options));
        Assert.False(ModConfigFiles.IsServerModConfig($"{Mod}/Presets/custom_V2.json", options));
    }

    // The measured noise from the live install: real data inside a folder literally called config.
    [Fact]
    public void AnExcludedFileStopsBeingAConfig()
    {
        const string path = "user/mods/ozen-Foldables/config/locales/en.json";
        var options = Options(exclude: ["config/locales"]);

        Assert.True(ModConfigFiles.IsServerModConfig(path));
        Assert.False(ModConfigFiles.IsServerModConfig(path, options));
    }

    // An entry for one mod says nothing about another.
    [Fact]
    public void EntriesAreFoundByTheModsOwnFolder()
    {
        var map = new Dictionary<string, ModConfigOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["[svm] server value modifier"] = Options(settings: ["Loader/loader.json"]),
        };

        Assert.True(ModConfigFiles.IsServerModConfig(
            $"{Mod}/Loader/loader.json", ModConfigFiles.OptionsFor($"{Mod}/Loader/loader.json", map)));

        Assert.False(ModConfigFiles.IsServerModConfig(
            "user/mods/Other/Loader/loader.json",
            ModConfigFiles.OptionsFor("user/mods/Other/Loader/loader.json", map)));
    }

    [Fact]
    public void SvmIsSeededWithBothHalvesOfItsLayout()
    {
        var seeded = _store.For("[SVM] Server Value Modifier");

        Assert.Equal(["Presets"], seeded.UserData);
        Assert.Equal(["Loader/loader.json"], seeded.Settings);
    }

    // A seed is a default, not a rule: clearing it has to stick.
    [Fact]
    public void ClearingASeededLocationIsRemembered()
    {
        _store.SetUserData("[SVM] Server Value Modifier", []);

        Assert.Empty(_store.For("[SVM] Server Value Modifier").UserData);
        Assert.Equal(["Loader/loader.json"], _store.For("[SVM] Server Value Modifier").Settings);
    }

    // Changing one thing about a seeded mod must not drop the rest of its seed.
    [Fact]
    public void SettingAPolicyOnASeededModKeepsItsLocations()
    {
        _store.SetPolicy("[SVM] Server Value Modifier", ModConfigPolicy.KeepMine);

        var stored = _store.Load()["[svm] server value modifier"];
        Assert.Equal(ModConfigPolicy.KeepMine, stored.Policy);
        Assert.Equal(["Presets"], stored.UserData);
    }

    [Theory]
    [InlineData("../../elsewhere", null)]
    [InlineData("Presets/", "Presets")]
    [InlineData("\\Loader\\loader.json", "Loader/loader.json")]
    [InlineData("  ", null)]
    [InlineData(".", null)]
    public void APathIsNormalisedOrRefused(string given, string? expected) =>
        Assert.Equal(expected, ModConfigPaths.Normalise(given));

    [Fact]
    public void AnEntryPathThatClimbsOutIsNeverStored()
    {
        _store.SetUserData("SomeMod", ["../../../Windows"]);

        Assert.Empty(_store.For("SomeMod").UserData);
    }

    //
    // What a removal rescues: SVM's presets are written by its own generator, so they are in no record
    // and have to be found on disk.
    //
    [Fact]
    public void UserDataIsFoundOnDiskEvenWhenNoRecordListsIt()
    {
        var install = Path.Combine(_root, "install");
        var presets = Path.Combine(install, "user", "mods", "SomeMod", "Presets");
        Directory.CreateDirectory(presets);
        File.WriteAllText(Path.Combine(presets, "custom_V2.json"), "{}");

        var record = new InstalledModRecord
        {
            ModId = 1,
            Name = "SomeMod",
            Version = "1.0.0",
            InstalledAt = DateTimeOffset.UtcNow,
            Files = ["user/mods/SomeMod/config.json"],
        };

        var map = new Dictionary<string, ModConfigOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["somemod"] = Options(userData: ["Presets"]),
        };

        Assert.Equal(["user/mods/SomeMod/Presets/custom_V2.json"],
            ModConfigFiles.UserDataOnDisk(install, record, map));
    }

    // The guard rail: a folder holding a database was opted in by mistake, and is not walked.
    [Fact]
    public void AUserDataFolderWithTooManyFilesIsRefused()
    {
        var install = Path.Combine(_root, "install2");
        var data = Path.Combine(install, "user", "mods", "SomeMod", "db");
        Directory.CreateDirectory(data);

        for (var i = 0; i <= ModConfigFiles.MaxUserDataFiles; i++)
            File.WriteAllText(Path.Combine(data, $"{i}.json"), "{}");

        var record = new InstalledModRecord
        {
            ModId = 1,
            Name = "SomeMod",
            Version = "1.0.0",
            InstalledAt = DateTimeOffset.UtcNow,
            Files = ["user/mods/SomeMod/config.json"],
        };

        var map = new Dictionary<string, ModConfigOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["somemod"] = Options(userData: ["db"]),
        };

        Assert.Empty(ModConfigFiles.UserDataOnDisk(install, record, map));
    }
}
