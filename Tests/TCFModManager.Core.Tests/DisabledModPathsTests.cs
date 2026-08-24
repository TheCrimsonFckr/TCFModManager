using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class DisabledModPathsTests
{
    [Fact]
    public void Disabled_AppendsSuffixToContainer()
    {
        var container = Path.Combine("C:", "SPT", "user", "mods");

        Assert.Equal(container + ".disabled", DisabledModPaths.Disabled(container));
    }

    [Fact]
    public void Disabled_AlreadyDisabled_IsUnchanged()
    {
        var container = Path.Combine("C:", "SPT", "user", "mods.disabled");

        Assert.Equal(container, DisabledModPaths.Disabled(container));
    }

    [Fact]
    public void Enabled_StripsSuffix()
    {
        var disabled = Path.Combine("C:", "SPT", "BepInEx", "plugins.disabled");
        var expected = Path.Combine("C:", "SPT", "BepInEx", "plugins");

        Assert.Equal(expected, DisabledModPaths.Enabled(disabled));
        Assert.Equal(expected, DisabledModPaths.Enabled(expected));
    }

    [Fact]
    public void IsModDisabled_ReadsTheParentContainerNotTheModItself()
    {
        Assert.True(DisabledModPaths.IsModDisabled(Path.Combine("C:", "SPT", "user", "mods.disabled", "SomeMod")));
        Assert.False(DisabledModPaths.IsModDisabled(Path.Combine("C:", "SPT", "user", "mods", "SomeMod")));
    }

    [Fact]
    public void IsModDisabled_HandlesALooseDll()
    {
        Assert.True(DisabledModPaths.IsModDisabled(Path.Combine("C:", "SPT", "BepInEx", "plugins.disabled", "Loose.dll")));
        Assert.False(DisabledModPaths.IsModDisabled(Path.Combine("C:", "SPT", "BepInEx", "plugins", "Loose.dll")));
    }

    [Fact]
    public void Counterpart_MovesAModBetweenStatesKeepingItsName()
    {
        var live = Path.Combine("C:", "SPT", "user", "mods", "SomeMod");
        var disabled = Path.Combine("C:", "SPT", "user", "mods.disabled", "SomeMod");

        Assert.Equal(disabled, DisabledModPaths.Counterpart(live));
        Assert.Equal(live, DisabledModPaths.Counterpart(disabled));
    }

    [Fact]
    public void Counterpart_KeepsALooseDllsExtension()
    {
        var live = Path.Combine("C:", "SPT", "BepInEx", "plugins", "Loose.dll");
        var disabled = Path.Combine("C:", "SPT", "BepInEx", "plugins.disabled", "Loose.dll");

        Assert.Equal(disabled, DisabledModPaths.Counterpart(live));
        Assert.Equal(live, DisabledModPaths.Counterpart(disabled));
    }

    [Fact]
    public void ClientContainers_CoversPluginsAndPatchers()
    {
        var containers = DisabledModPaths.ClientContainers(Path.Combine("C:", "SPT")).ToList();

        Assert.Equal(2, containers.Count);
        Assert.Contains(containers, c => c.EndsWith(Path.Combine("BepInEx", "plugins")));
        Assert.Contains(containers, c => c.EndsWith(Path.Combine("BepInEx", "patchers")));
    }

    [Fact]
    public void ServerContainers_CoversAllThreeKnownLayouts()
    {
        var containers = DisabledModPaths.ServerContainers(Path.Combine("C:", "SPT")).ToList();

        Assert.Equal(3, containers.Count);
        Assert.Contains(containers, c => c.EndsWith(Path.Combine("SPT_Runtime", "user", "mods")));
        Assert.Contains(containers, c => c.EndsWith(Path.Combine("SPT", "user", "mods")));
        Assert.Contains(containers, c => c.EndsWith(Path.Combine("user", "mods")));
    }
}
