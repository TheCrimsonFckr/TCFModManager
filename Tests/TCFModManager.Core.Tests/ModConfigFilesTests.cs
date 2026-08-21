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
