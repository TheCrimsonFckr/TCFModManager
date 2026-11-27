using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// GitHub #11: an addon whose files went with its parent's folder kept its record and came back in every new mod list.
public class DropAddonsWithNoFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dropaddons-{Guid.NewGuid():N}");
    private readonly string _install;
    private readonly ModInstallManifestService _service;

    public DropAddonsWithNoFilesTests()
    {
        _install = Directory.CreateDirectory(Path.Combine(_root, "SPT")).FullName;
        _service = new ModInstallManifestService(Path.Combine(_root, "installedMods.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private InstalledModRecord Record(int id, string name, bool isAddon = true, bool appManaged = true, string? stamped = "here", params string[] files) => new()
    {
        ModId = id,
        IsAddon = isAddon,
        Name = name,
        Version = "1.0.0",
        InstalledAt = DateTimeOffset.UnixEpoch,
        IsAppManaged = appManaged,
        Files = [.. files],
        InstallPath = stamped == "here" ? InstallStamp.Of(_install) : stamped,
    };

    private void Place(string relative)
    {
        var full = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    [Fact]
    public void AnAddonWithAllItsFilesGone_IsDropped()
    {
        _service.Save(new ModInstallManifest
        {
            Mods = [Record(37, "APBS Preset", files: "SPT/user/mods/apbs/presets/Realistic/a.json")],
        });

        Assert.Equal("APBS Preset", Assert.Single(_service.DropAddonsWithNoFiles(_install)));
        Assert.Empty(_service.Load().Mods);
    }

    [Fact]
    public void AnAddonWithOneFileLeft_IsKept()
    {
        Place("SPT/user/mods/apbs/presets/Realistic/b.json");
        _service.Save(new ModInstallManifest
        {
            Mods = [Record(37, "APBS Preset", files: ["SPT/user/mods/apbs/presets/Realistic/a.json", "SPT/user/mods/apbs/presets/Realistic/b.json"])],
        });

        Assert.Empty(_service.DropAddonsWithNoFiles(_install));
        Assert.Single(_service.Load().Mods);
    }

    [Fact]
    public void AnAddonWhoseFilesAreDisabled_IsKept()
    {
        Place("BepInEx/plugins.disabled/BlackDiv/BlackDivInfo.dll");
        _service.Save(new ModInstallManifest
        {
            Mods = [Record(34, "Black Div spawn notifier", files: "BepInEx/plugins/BlackDiv/BlackDivInfo.dll")],
        });

        Assert.Empty(_service.DropAddonsWithNoFiles(_install));
        Assert.Single(_service.Load().Mods);
    }

    [Fact]
    public void ModsHandConfirmedUnstampedAndOtherInstalls_AreNeverTouched()
    {
        _service.Save(new ModInstallManifest
        {
            Mods =
            [
                Record(1594, "APBS", isAddon: false, files: "SPT/user/mods/apbs/a.dll"),
                Record(40, "Hand confirmed", appManaged: false),
                Record(41, "Unstamped", stamped: null, files: "SPT/user/mods/x/a.json"),
                Record(42, "Other install", stamped: Path.Combine(_root, "Other"), files: "SPT/user/mods/x/b.json"),
            ],
        });

        Assert.Empty(_service.DropAddonsWithNoFiles(_install));
        Assert.Equal(4, _service.Load().Mods.Count);
    }
}
