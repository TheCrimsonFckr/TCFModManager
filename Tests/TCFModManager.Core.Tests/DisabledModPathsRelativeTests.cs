using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The relative-path half of DisabledModPaths: the same questions asked about a forward-slash path
// relative to a root, which is the shape a mod list entry or a served inventory carries.
//
// What all of it protects: a disabled mod must not read as missing. A scanner that only looks at
// the live path reinstalls it and silently re-enables it; hashing the disabled copy instead makes
// the diff update it into the live folder, re-enabling it another way. The counterpart lookup is
// what lets a caller say "present, and deliberately off" and do nothing at all.
//
public class DisabledModPathsRelativeTests
{
    private static string Container(string path) =>
        DisabledModPaths.TryFindRelativeContainer(path, out var container, out var disabled)
            ? $"{container}|{(disabled ? "disabled" : "enabled")}"
            : "<none>";

    private static string Counterpart(string path) =>
        DisabledModPaths.TryGetRelativeCounterpart(path, out var counterpart) ? counterpart : "<none>";

    [Theory]
    [InlineData("BepInEx/plugins/SAIN/SAIN.dll", "BepInEx/plugins|enabled")]
    [InlineData("BepInEx/plugins.disabled/SAIN/SAIN.dll", "BepInEx/plugins.disabled|disabled")]
    [InlineData("BepInEx/patchers/spt-prepatch.dll", "BepInEx/patchers|enabled")]
    [InlineData("user/mods/MyMod/package.json", "user/mods|enabled")]
    [InlineData("BepInEx/plugins", "BepInEx/plugins|enabled")]
    public void FindsTheContainerAPathSitsIn(string path, string expected) =>
        Assert.Equal(expected, Container(path));

    // Server content is relative to a root that may still have the layout folder in front of it.
    [Theory]
    [InlineData("SPT_Runtime/user/mods/MyMod/package.json", "SPT_Runtime/user/mods|enabled")]
    [InlineData("SPT/user/mods/MyMod/package.json", "SPT/user/mods|enabled")]
    [InlineData("SPT_Runtime/user/mods.disabled/MyMod/package.json", "SPT_Runtime/user/mods.disabled|disabled")]
    public void MatchesAContainerUnderALayoutPrefix(string path, string expected) =>
        Assert.Equal(expected, Container(path));

    [Theory]
    [InlineData("hostfxr.dll")]
    [InlineData("TCFModSync.Updater.exe")]
    [InlineData("BepInEx/pluginsExtra/x.dll")]     // a partial segment is not a match
    public void PathsOutsideAnyContainerAreNotClaimed(string path)
    {
        Assert.Equal("<none>", Container(path));
        Assert.Equal("<none>", Counterpart(path));
    }

    //
    // A mod folder can be called anything, including "user/mods". The leftmost container wins, so
    // this belongs to BepInEx/plugins - naming the inner one would point at a path that cannot
    // exist and the disabled twin would never be found.
    //
    [Fact]
    public void TheOutermostContainerWins()
    {
        Assert.Equal("BepInEx/plugins|enabled", Container("BepInEx/plugins/user/mods/x.dll"));
        Assert.Equal("BepInEx/plugins.disabled/user/mods/x.dll", Counterpart("BepInEx/plugins/user/mods/x.dll"));
    }

    [Theory]
    [InlineData("BepInEx/plugins/SAIN/config.json", "BepInEx/plugins.disabled/SAIN/config.json")]
    [InlineData("BepInEx/plugins.disabled/SAIN/config.json", "BepInEx/plugins/SAIN/config.json")]
    [InlineData("BepInEx/plugins/skwizzy.LootingBots.dll", "BepInEx/plugins.disabled/skwizzy.LootingBots.dll")]
    [InlineData("SPT_Runtime/user/mods/MyMod/package.json", "SPT_Runtime/user/mods.disabled/MyMod/package.json")]
    public void CounterpartSwapsOnlyTheContainerSegment(string path, string expected) =>
        Assert.Equal(expected, Counterpart(path));

    [Theory]
    [InlineData(@"BepInEx\plugins\SAIN\SAIN.dll", "BepInEx/plugins|enabled")]
    [InlineData("bepinex/PLUGINS/SAIN/SAIN.dll", "bepinex/PLUGINS|enabled")]
    public void SeparatorsAndCasingDoNotMatter(string path, string expected) =>
        Assert.Equal(expected, Container(path));

    [Fact]
    public void ALeadingSeparatorIsTrimmed() =>
        Assert.Equal("BepInEx/plugins.disabled/a.dll", Counterpart("/BepInEx/plugins/a.dll"));

    [Theory]
    [InlineData("user/mods.disabled/M/p.json", true)]
    [InlineData("user/mods/M/p.json", false)]
    [InlineData("hostfxr.dll", false)]
    public void IsRelativePathDisabledReadsTheContainer(string path, bool expected) =>
        Assert.Equal(expected, DisabledModPaths.IsRelativePathDisabled(path));

    // Safe to key a lookup on: unchanged for a path that is already enabled or in no container.
    [Theory]
    [InlineData("BepInEx/plugins.disabled/SAIN/a.dll", "BepInEx/plugins/SAIN/a.dll")]
    [InlineData("BepInEx/plugins/SAIN/a.dll", "BepInEx/plugins/SAIN/a.dll")]
    [InlineData("hostfxr.dll", "hostfxr.dll")]
    public void ToEnabledRelativePathNormalisesToTheLiveContainer(string path, string expected) =>
        Assert.Equal(expected, DisabledModPaths.ToEnabledRelativePath(path));

    [Fact]
    public void ToRelativeIsIdempotent()
    {
        var once = DisabledModPaths.ToRelative(@"\BepInEx\plugins\a.dll");

        Assert.Equal("BepInEx/plugins/a.dll", once);
        Assert.Equal(once, DisabledModPaths.ToRelative(once));
    }
}
