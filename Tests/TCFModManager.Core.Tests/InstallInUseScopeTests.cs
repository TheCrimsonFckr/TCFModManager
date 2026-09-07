using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Which install a running SPT belongs to.
//
// The in-use guard used to match on process NAME alone, so any SPT.Server.exe anywhere on the
// machine blocked every install on it. Anyone running a dedicated server on one drive and playing
// from a second install on another could not install a mod into the second while the first was up -
// and the fix it suggested was to close the server they were modding the other install for.
//
// These pin the containment rule the scoped check is built on. The process enumeration around it is
// not testable without real processes; this is the part that decides whether someone gets blocked.
//
public class InstallInUseScopeTests
{
    private static string Root(params string[] parts) =>
        Path.Combine([Path.DirectorySeparatorChar + "installs", .. parts]);

    [Fact]
    public void ExecutableAtTheInstallRoot_IsInside()
    {
        Assert.True(ModInstallService.IsInside(
            Root("Tarkov", "EscapeFromTarkov.exe"),
            Root("Tarkov")));
    }

    [Fact]
    public void ExecutableNestedInTheInstall_IsInside()
    {
        // The server exe lives under SPT\ or SPT_Runtime\ depending on the layout, and the guard is
        // given the install root either way.
        Assert.True(ModInstallService.IsInside(
            Root("Tarkov", "SPT", "SPT.Server.exe"),
            Root("Tarkov")));
    }

    [Fact]
    public void ExecutableInADifferentInstall_IsNotInside()
    {
        Assert.False(ModInstallService.IsInside(
            Root("Tarkov Server", "SPT", "SPT.Server.exe"),
            Root("Tarkov")));
    }

    //
    // The reason the comparison appends a separator. "…\Tarkov" is a prefix of "…\Tarkov Server" as
    // plain text, so a bare StartsWith would read the dedicated server as living inside the install
    // being modded and block it - the original bug, surviving the fix.
    //
    [Fact]
    public void InstallWhoseNameIsAPrefixOfAnother_IsNotInside()
    {
        Assert.False(ModInstallService.IsInside(
            Root("Tarkov Server", "SPT.Server.exe"),
            Root("Tarkov")));
    }

    [Fact]
    public void TrailingSeparatorOnTheInstallPath_MakesNoDifference()
    {
        Assert.True(ModInstallService.IsInside(
            Root("Tarkov", "SPT", "SPT.Server.exe"),
            Root("Tarkov") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void CasingDoesNotDecideIt()
    {
        Assert.True(ModInstallService.IsInside(
            Root("Tarkov", "SPT", "SPT.Server.exe"),
            Root("tarkov")));
    }

    // The install root is not "inside itself" - there is no executable at that path to hold a handle.
    [Fact]
    public void TheInstallRootItself_IsNotInside()
    {
        Assert.False(ModInstallService.IsInside(Root("Tarkov"), Root("Tarkov")));
    }

    //
    // Unresolvable either side reads as inside, so the caller keeps blocking. "I could not tell" is
    // not "it is fine": guessing the other way modifies an install mid-update, which is the whole
    // thing this guard exists to prevent.
    //
    [Fact]
    public void APathThatCannotBeResolved_ReadsAsInside()
    {
        Assert.True(ModInstallService.IsInside("\0not a path", Root("Tarkov")));
    }
}
