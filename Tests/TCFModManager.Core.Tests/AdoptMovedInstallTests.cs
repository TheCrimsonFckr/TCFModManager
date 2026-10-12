using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// An install folder renamed after its mods were installed (Chris, 2026-10-12): the records follow it.
public class AdoptMovedInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"adopt-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static InstalledModRecord Record(string name, string? stamped, params string[] files) => new()
    {
        ModId = name.Length,
        Name = name,
        Version = "1.0.0",
        InstalledAt = DateTimeOffset.UnixEpoch,
        IsAppManaged = true,
        Files = [.. files],
        InstallPath = stamped,
    };

    [Fact]
    public void RecordsFromAVanishedInstallWhoseFilesAreHere_AreReStamped_OthersAreLeft()
    {
        var install = Directory.CreateDirectory(Path.Combine(_root, "SPT 4.1 Server")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_root, "SPT 4.0")).FullName;
        var gone = Path.Combine(_root, "SPT 4.1");

        Directory.CreateDirectory(Path.Combine(install, "BepInEx", "plugins", "Sense"));
        File.WriteAllText(Path.Combine(install, "BepInEx", "plugins", "Sense", "Sense.dll"), "x");

        var service = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
        service.Save(new ModInstallManifest
        {
            Mods =
            [
                Record("Sense", gone, "BepInEx/plugins/Sense/Sense.dll"),
                Record("Elsewhere", other, "BepInEx/plugins/Elsewhere/E.dll"),
                Record("Missing", gone, "BepInEx/plugins/Missing/M.dll"),
            ],
        });

        Assert.Equal(1, service.AdoptMovedInstall(install));

        var mods = service.Load().Mods;
        Assert.Equal(InstallStamp.Of(install), mods.Single(m => m.Name == "Sense").InstallPath);
        Assert.Equal(other, mods.Single(m => m.Name == "Elsewhere").InstallPath);
        Assert.Equal(gone, mods.Single(m => m.Name == "Missing").InstallPath);
    }
}
