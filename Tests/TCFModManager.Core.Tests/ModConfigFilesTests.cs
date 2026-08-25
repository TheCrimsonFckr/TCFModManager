using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModConfigFilesTests
{
    [Theory]
    [InlineData("user/mods/SomeMod/config/config.json", true)]
    [InlineData("user/mods/SomeMod/configs/settings.json", true)]
    [InlineData("user/mods/SomeMod/src/config/nested/values.json", true)]
    [InlineData("SPT_Runtime/user/mods/SomeMod/config/config.json", true)]
    [InlineData("SPT/user/mods/SomeMod/config/config.json", true)]
    [InlineData("user/mods/SomeMod/package.json", false)]
    [InlineData("user/mods/SomeMod/config/readme.txt", false)]
    [InlineData("user/mods/config/loose.json", false)]
    [InlineData("BepInEx/plugins/SomeMod/config/config.json", false)]
    [InlineData("BepInEx/config/com.author.mod.cfg", false)]
    public void IsServerModConfig_MatchesConfigsInsideAServerModFolder(string path, bool expected) =>
        Assert.Equal(expected, ModConfigFiles.IsServerModConfig(path));

    [Fact]
    public void IsServerModConfig_AcceptsBackslashSeparators() =>
        Assert.True(ModConfigFiles.IsServerModConfig(@"user\mods\SomeMod\config\config.json"));

    //
    // A config sitting directly in the mod's own folder, which is by far the most common layout -
    // and the one this used to miss entirely, so uninstalling such a mod deleted the user's settings
    // along with it.
    //
    [Theory]
    [InlineData("user/mods/acidphantasm-botplacementsystem/config.json", true)]
    [InlineData("user/mods/MoreBotsServer/config.jsonc", true)]
    [InlineData("user/mods/SomeMod/settings.json", true)]
    [InlineData("user/mods/SomeMod/cfg.json5", true)]
    public void IsServerModConfig_MatchesAConventionallyNamedConfigAtTheModsRoot(string path, bool expected) =>
        Assert.Equal(expected, ModConfigFiles.IsServerModConfig(path));

    //
    // The rest of what a real server mod keeps beside its config: bundle manifests, .NET build
    // output, shipped data, and the pristine copy of the defaults. All named from mods actually
    // installed in a live SPT install - none of them is a config, and preserving them on uninstall
    // (or offering them for editing) would be wrong.
    //
    [Theory]
    [InlineData("user/mods/EpicsAIO/bundles.json")]
    [InlineData("user/mods/DynamicRaidEvents/config.example.json")]
    [InlineData("user/mods/TCF-ModSync.Server/TCFModSync.Server.deps.json")]
    [InlineData("user/mods/TCF-ModSync.Server/TCFModSync.Server.staticwebassets.endpoints.json")]
    [InlineData("user/mods/SPT-AKI Profile Editor.ModHelper/Hashes.json")]
    [InlineData("user/mods/Tyfon.WeaponCustomizer.Server/customizations.json")]
    [InlineData("user/mods/SomeMod/package-lock.json")]
    public void IsServerModConfig_LeavesAModsOwnDataAndBuildOutputAlone(string path) =>
        Assert.False(ModConfigFiles.IsServerModConfig(path));

    // JSON5 and JSONC are what most SPT server mods actually ship, since their configs are full of
    // comments explaining each setting.
    [Theory]
    [InlineData("user/mods/betterkeys/config/config.jsonc", true)]
    [InlineData("user/mods/fika-server/assets/configs/fika.jsonc", true)]
    [InlineData("user/mods/CaliberSplitAmmoCases/config/defaultConfig.jsonc", true)]
    public void IsServerModConfig_AcceptsTheCommentedJsonFlavoursModsShip(string path, bool expected) =>
        Assert.Equal(expected, ModConfigFiles.IsServerModConfig(path));

    // A disabled mod sits in the ".disabled" sibling of its container, and its config is still its
    // config - the Configs page finds it there.
    [Fact]
    public void IsServerModConfig_RecognisesAConfigUnderTheDisabledSibling() =>
        Assert.True(ModConfigFiles.IsServerModConfig("user/mods.disabled/Parked/config/config.json"));

    [Fact]
    public void InRecord_KeepsAConfigSittingAtTheModsRoot()
    {
        var record = new InstalledModRecord
        {
            ModId = 1,
            Name = "Some Mod",
            VersionId = 1,
            Version = "1.0.0",
            InstalledAt = DateTimeOffset.UtcNow,
            Files =
            [
                "user/mods/SomeMod/package.json",
                "user/mods/SomeMod/config.jsonc",
                "user/mods/SomeMod/bundles.json",
            ],
        };

        Assert.Equal(["user/mods/SomeMod/config.jsonc"], ModConfigFiles.InRecord(record));
    }

    [Fact]
    public void InRecord_PicksOutOnlyTheConfigFiles()
    {
        var record = new InstalledModRecord
        {
            ModId = 1,
            Name = "Some Mod",
            VersionId = 1,
            Version = "1.0.0",
            InstalledAt = DateTimeOffset.UtcNow,
            Files =
            [
                "user/mods/SomeMod/package.json",
                "user/mods/SomeMod/config/config.json",
                "user/mods/SomeMod/src/mod.js",
                "BepInEx/plugins/SomeMod/SomeMod.dll",
            ],
        };

        Assert.Equal(["user/mods/SomeMod/config/config.json"], ModConfigFiles.InRecord(record));
    }

    [Fact]
    public void InFolder_ReturnsInstallRelativePathsForConfigsOnDisk()
    {
        var installPath = Path.Combine(Path.GetTempPath(), "tcfmm-config-tests-" + Guid.NewGuid().ToString("N"));
        var modFolder = Path.Combine(installPath, "user", "mods", "SomeMod");
        Directory.CreateDirectory(Path.Combine(modFolder, "config"));

        try
        {
            File.WriteAllText(Path.Combine(modFolder, "config", "config.json"), "{}");
            File.WriteAllText(Path.Combine(modFolder, "package.json"), "{}");

            var found = ModConfigFiles.InFolder(installPath, modFolder);

            Assert.Equal(["user/mods/SomeMod/config/config.json"], found);
        }
        finally
        {
            Directory.Delete(installPath, recursive: true);
        }
    }

    [Fact]
    public void InFolder_ReturnsNothingForAMissingFolder() =>
        Assert.Empty(ModConfigFiles.InFolder(@"C:\nope", @"C:\nope\user\mods\Missing"));
}
