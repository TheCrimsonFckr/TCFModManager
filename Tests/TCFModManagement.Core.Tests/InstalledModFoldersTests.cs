using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class InstalledModFoldersTests
{
    [Fact]
    public void FromPlacedFiles_FindsServerModFolders()
    {
        var folders = InstalledModFolders.FromPlacedFiles(
        [
            "user/mods/EpicsAIO/package.json",
            "user/mods/EpicsAIO/src/mod.js",
        ]);

        Assert.Equal(["EpicsAIO"], folders);
    }

    [Fact]
    public void FromPlacedFiles_FindsServerModFoldersUnderARemappedServerRoot()
    {
        // Server content is remapped under whatever the install calls its server root.
        var folders = InstalledModFolders.FromPlacedFiles(["SPT_Runtime/user/mods/EpicsAIO/package.json"]);

        Assert.Equal(["EpicsAIO"], folders);
    }

    [Fact]
    public void FromPlacedFiles_FindsClientPluginAndPatcherFolders()
    {
        var folders = InstalledModFolders.FromPlacedFiles(
        [
            "BepInEx/plugins/WTT-ClientCommonLib/WTT-ClientCommonLib.dll",
            "BepInEx/patchers/SomePatcher/SomePatcher.dll",
        ]);

        Assert.Equal(["WTT-ClientCommonLib", "SomePatcher"], folders);
    }

    [Fact]
    public void FromPlacedFiles_NamesALooseDllAfterTheFileItself()
    {
        // The scanner reports a loose top-level DLL by its file name, so this has to agree.
        var folders = InstalledModFolders.FromPlacedFiles(["BepInEx/plugins/SomeMod.dll"]);

        Assert.Equal(["SomeMod"], folders);
    }

    [Fact]
    public void FromPlacedFiles_ReturnsBothHalvesOfASplitModOnce()
    {
        var folders = InstalledModFolders.FromPlacedFiles(
        [
            "BepInEx/plugins/WTT-ClientCommonLib/WTT-ClientCommonLib.dll",
            "BepInEx/plugins/WTT-ClientCommonLib/extra.dll",
            "user/mods/WTT-ServerCommonLib/package.json",
        ]);

        Assert.Equal(["WTT-ClientCommonLib", "WTT-ServerCommonLib"], folders);
    }

    [Fact]
    public void FromPlacedFiles_IgnoresFilesOutsideAKnownContainer()
    {
        Assert.Empty(InstalledModFolders.FromPlacedFiles(["SPT_Data/Server/database/x.json", "readme.txt"]));
    }

    [Fact]
    public void Resolve_PrefersTheStoredFolders()
    {
        var record = NewRecord(files: ["user/mods/Derived/package.json"], folders: ["Stored"]);

        Assert.Equal(["Stored"], InstalledModFolders.Resolve(record));
    }

    [Fact]
    public void Resolve_FallsBackToTheFileListForRecordsWrittenBeforeFoldersWereStored()
    {
        var record = NewRecord(files: ["user/mods/EpicsAIO/package.json"], folders: []);

        Assert.Equal(["EpicsAIO"], InstalledModFolders.Resolve(record));
    }

    private static InstalledModRecord NewRecord(List<string> files, List<string> folders) => new()
    {
        ModId = 1263,
        Guid = "com.epicrangetime.aio",
        Name = "Epic's All in One",
        VersionId = 13812,
        Version = "4.0.8",
        InstalledAt = DateTimeOffset.UnixEpoch,
        Files = files,
        Folders = folders,
    };
}
