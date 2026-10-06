using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-12 F17: telling an install that runs Fika from one that doesn't.
public class FikaInstallTests
{
    private static InstalledMod Client(string name, string? guid, bool disabled = false) => new()
    {
        Name = name,
        Guid = guid,
        Target = InstalledModTarget.Client,
        FolderPath = Path.Combine("C:", "SPT", "BepInEx", disabled ? "plugins.disabled" : "plugins", name),
        IsDisabled = disabled,
    };

    private static InstalledMod Server(string name, string? guid = null, bool disabled = false) => new()
    {
        Name = name,
        Guid = guid,
        Target = InstalledModTarget.Server,
        FolderPath = Path.Combine("C:", "SPT", "user", disabled ? "mods.disabled" : "mods", name),
        IsDisabled = disabled,
    };

    [Fact]
    public void The_client_plugin_counts() =>
        Assert.True(FikaInstall.IsPresent([Client("Fika", "com.fika.core")]));

    [Fact]
    public void The_server_mod_counts_by_guid_or_by_its_SPT_3_folder()
    {
        Assert.True(FikaInstall.IsPresent([Server("Fika.Server", "com.fika.server")]));
        Assert.True(FikaInstall.IsPresent([Server("fika-server")]));
    }

    [Fact]
    public void A_disabled_Fika_or_a_mod_that_only_mentions_it_does_not()
    {
        Assert.False(FikaInstall.IsPresent([Client("Fika", "com.fika.core", disabled: true)]));
        Assert.False(FikaInstall.IsPresent([Client("FikaPingFix", "com.someone.fikapingfix"), Server("FikaTweaks")]));
        Assert.False(FikaInstall.IsPresent([]));
    }

    [Theory]
    [InlineData("incompatible", true)]
    [InlineData("Incompatible ", true)]
    [InlineData("compatible", false)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void Only_a_version_marked_incompatible_is_asked_about(string? value, bool expected) =>
        Assert.Equal(expected, FikaInstall.IsIncompatible(value));
}
