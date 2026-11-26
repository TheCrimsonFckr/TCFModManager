using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class ModLinkStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modlinks-" + Guid.NewGuid().ToString("N"));

    public ModLinkStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ModLinkStore Store() => new(Path.Combine(_dir, "modLinks.json"));

    [Fact]
    public void A_link_is_kept_by_folder_name_whatever_its_case()
    {
        Store().Link(["SomeMod", "SomeModServer"], 42, isAddon: false);

        var links = Store().Load();
        Assert.Equal(new ModLink(42, false), links.Folders["somemod"]);
        Assert.Equal(new ModLink(42, false), links.Folders["SOMEMODSERVER"]);
    }

    [Fact]
    public void A_folder_an_install_record_names_loses_its_link()
    {
        Store().Link(["SomeMod"], 42, isAddon: false);
        Store().Link(["Preset"], 7, isAddon: true);

        var effective = Store().Effective(["somemod"], []);

        Assert.False(effective.Folders.ContainsKey("SomeMod"));
        Assert.Equal(new ModLink(7, true), effective.Folders["Preset"]);
        Assert.False(Store().Load().Folders.ContainsKey("SomeMod"));
    }

    [Fact]
    public void Unlink_says_whether_anything_was_linked()
    {
        Store().Link(["SomeMod"], 42, isAddon: false);

        Assert.True(Store().Unlink(["SomeMod", "Other"]));
        Assert.False(Store().Unlink(["SomeMod"]));
        Assert.True(Store().Load().IsEmpty);
    }

    [Fact]
    public void Addon_files_are_kept_in_their_enabled_form_and_belong_to_one_addon()
    {
        Store().LinkAddonFiles(7, ["BepInEx\\plugins.disabled\\SAIN\\Presets\\a.json", "BepInEx/plugins/SAIN/Presets/b.json"]);
        Store().LinkAddonFiles(8, ["BepInEx/plugins/SAIN/Presets/b.json", "BepInEx/plugins/SAIN/Presets/c.json"]);

        var links = Store().Load().AddonFiles;
        Assert.Equal(["BepInEx/plugins/SAIN/Presets/a.json"], links.Single(a => a.AddonId == 7).Files);
        Assert.Equal(["BepInEx/plugins/SAIN/Presets/b.json", "BepInEx/plugins/SAIN/Presets/c.json"], links.Single(a => a.AddonId == 8).Files);
    }

    [Fact]
    public void An_addon_a_record_holds_loses_its_file_link_and_no_files_unlinks()
    {
        Store().LinkAddonFiles(7, ["BepInEx/plugins/SAIN/Presets/a.json"]);
        Store().LinkAddonFiles(8, ["BepInEx/plugins/SAIN/Presets/b.json"]);

        Assert.DoesNotContain(Store().Effective([], [7]).AddonFiles, a => a.AddonId == 7);

        Store().LinkAddonFiles(8, []);
        Assert.Empty(Store().Load().AddonFiles);
        Assert.False(Store().UnlinkAddonFiles(8));
    }

    [Fact]
    public void A_linked_addon_owns_its_files_in_the_folder_it_shares()
    {
        var parent = new InstalledModRecord
        {
            ModId = 1, Name = "SAIN", Version = "1.0.0", InstalledAt = DateTimeOffset.UnixEpoch,
            Files = ["BepInEx/plugins/SAIN/SAIN.dll"],
        };
        var linked = new InstalledModRecord
        {
            ModId = 7, IsAddon = true, Name = "Preset", Version = "", InstalledAt = DateTimeOffset.MinValue,
            IsAppManaged = false, Files = ["BepInEx/plugins/SAIN/Presets/a.json"],
        };

        var owners = new ModOwnership([parent], null, [linked]);

        Assert.Same(linked, owners.RecordFor("C:/SPT", "BepInEx/plugins.disabled/SAIN/Presets/a.json"));
        Assert.Equal([(1, false), (7, true)], owners.RecordsIn("C:/SPT", "BepInEx/plugins/SAIN").Select(r => (r.ModId, r.IsAddon)));
    }
}
