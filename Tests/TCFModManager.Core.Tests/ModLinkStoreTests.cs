using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class ModLinkStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modlinks-" + Guid.NewGuid().ToString("N"));

    public ModLinkStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ModLinkStore Store() => new(Path.Combine(_dir, "mod-links.json"));

    [Fact]
    public void A_link_is_kept_by_folder_name_whatever_its_case()
    {
        Store().Link(["SomeMod", "SomeModServer"], 42, isAddon: false);

        var links = Store().Load();
        Assert.Equal(new ModLink(42, false), links["somemod"]);
        Assert.Equal(new ModLink(42, false), links["SOMEMODSERVER"]);
    }

    [Fact]
    public void A_folder_an_install_record_names_loses_its_link()
    {
        Store().Link(["SomeMod"], 42, isAddon: false);
        Store().Link(["Preset"], 7, isAddon: true);

        var effective = Store().Effective(["somemod"]);

        Assert.False(effective.ContainsKey("SomeMod"));
        Assert.Equal(new ModLink(7, true), effective["Preset"]);
        Assert.False(Store().Load().ContainsKey("SomeMod"));
    }

    [Fact]
    public void Unlink_says_whether_anything_was_linked()
    {
        Store().Link(["SomeMod"], 42, isAddon: false);

        Assert.True(Store().Unlink(["SomeMod", "Other"]));
        Assert.False(Store().Unlink(["SomeMod"]));
        Assert.Empty(Store().Load());
    }
}
