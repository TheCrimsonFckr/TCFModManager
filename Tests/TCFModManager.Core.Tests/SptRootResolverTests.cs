using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The game root and the server root are the same folder on a bundled install and different on a
// standalone server. Conflating them is what made 4.1's SPT_Runtime\ layout and server-only
// machines both go wrong, so every case below is a layout that exists in the wild rather than a
// version number mapped to a folder.
//
public class SptRootResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-roots-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // A temp folder left behind is not worth failing a test run over.
        }
    }

    private string Dir(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }

    private string File_(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, "");
        return path;
    }

    private string At(params string[] segments) => Path.Combine([_root, .. segments]);

    // <root>\ + SPT_Runtime\SPT.Server.exe - the 4.1 layout.
    private void Build41()
    {
        File_("EscapeFromTarkov.exe");
        Dir("BepInEx", "plugins");
        File_("SPT_Runtime", "SPT.Server.exe");
        Dir("SPT_Runtime", "user", "mods");
    }

    // <root>\ + SPT\SPT.Server.exe - the 4.0 layout.
    private void Build40()
    {
        File_("EscapeFromTarkov.exe");
        Dir("BepInEx", "plugins");
        File_("SPT", "SPT.Server.exe");
        Dir("SPT", "user", "mods");
    }

    [Fact]
    public void FourOne_ResolvesBothRootsFromTheServerModFolder()
    {
        Build41();
        var start = Dir("SPT_Runtime", "user", "mods", "SomeServerMod");

        var game = SptRootResolver.ResolveGameRoot(start);
        var server = SptRootResolver.ResolveServerRoot(start);

        Assert.Equal(_root, game.Directory);
        Assert.Equal(At("SPT_Runtime"), server.Directory);
        Assert.Equal(At("SPT_Runtime", "SPT.Server.exe"), server.ServerExePath);
    }

    [Fact]
    public void FourOne_ResolvesBothRootsFromAClientPluginFolder()
    {
        Build41();
        var start = Dir("BepInEx", "plugins", "SomeMod");

        Assert.Equal(_root, SptRootResolver.ResolveGameRoot(start).Directory);
        Assert.Equal(At("SPT_Runtime"), SptRootResolver.ResolveServerRoot(start).Directory);
    }

    [Fact]
    public void FourZero_PutsTheServerRootUnderSpt()
    {
        Build40();
        var start = Dir("BepInEx", "plugins", "SomeMod");

        Assert.Equal(_root, SptRootResolver.ResolveGameRoot(start).Directory);
        Assert.Equal(At("SPT"), SptRootResolver.ResolveServerRoot(start).Directory);
    }

    [Fact]
    public void ThreeX_KeepsBothRootsAtTheInstallRoot()
    {
        File_("EscapeFromTarkov.exe");
        Dir("BepInEx", "plugins");
        File_("Aki.Server.exe");
        var start = Dir("user", "mods", "SomeServerMod");

        Assert.Equal(_root, SptRootResolver.ResolveGameRoot(start).Directory);
        Assert.Equal(_root, SptRootResolver.ResolveServerRoot(start).Directory);
    }

    //
    // The case the split exists for: a server with no game files. The old single-root code returned
    // nothing here, which is exactly the machine a server map cares about.
    //
    [Fact]
    public void StandaloneServer_ResolvesTheServerRootAndNamesTheMissingGameRoot()
    {
        File_("SPT_Runtime", "SPT.Server.exe");
        var start = Dir("SPT_Runtime", "user", "mods", "SomeServerMod");

        var game = SptRootResolver.ResolveGameRoot(start);
        var server = SptRootResolver.ResolveServerRoot(start);

        Assert.False(game.Found);
        Assert.Equal(SptRootProblem.NotFound, game.Problem);
        Assert.Equal(SptRootKind.Game, game.Kind);

        Assert.Equal(At("SPT_Runtime"), server.Directory);
    }

    // An install upgraded 4.0 -> 4.1 can still have a stale SPT\ beside the live SPT_Runtime\.
    [Fact]
    public void BothLayoutsPresent_PrefersSptRuntime()
    {
        Build41();
        File_("SPT", "SPT.Server.exe");

        Assert.Equal(At("SPT_Runtime"), SptRootResolver.ResolveServerRoot(At()).Directory);
    }

    //
    // A named exe anywhere in the chain beats a wildcard guess anywhere in it. Without that
    // ordering, a mod shipping its own *Server*.exe under BepInEx would win from a deeper start
    // directory than the real server exe.
    //
    [Fact]
    public void ANamedExeAnywhereBeatsAWildcardMatchCloserToTheStart()
    {
        Build40();
        File_("BepInEx", "SomeModsHelperServer.exe");
        var start = Dir("BepInEx", "plugins", "SomeMod");

        Assert.Equal(At("SPT"), SptRootResolver.ResolveServerRoot(start).Directory);
    }

    [Fact]
    public void WildcardIsUsedWhenNoNamedCandidateMatches()
    {
        File_("EscapeFromTarkov.exe");
        File_("SPT_Runtime", "Fika.HeadlessServer.exe");
        var start = Dir("BepInEx", "plugins");

        var server = SptRootResolver.ResolveServerRoot(start);

        Assert.Equal(At("SPT_Runtime"), server.Directory);
        Assert.Equal(At("SPT_Runtime", "Fika.HeadlessServer.exe"), server.ServerExePath);
    }

    [Fact]
    public void ADisabledBepInExStillMarksTheGameRoot()
    {
        Dir("BepInEx.disabled", "plugins");
        File_("SPT_Runtime", "SPT.Server.exe");

        Assert.Equal(_root, SptRootResolver.ResolveGameRoot(At()).Directory);
    }

    [Fact]
    public void NothingAnywhere_ReportsNotFoundForBoth()
    {
        var start = Dir("somewhere", "deep");

        Assert.Equal(SptRootProblem.NotFound, SptRootResolver.ResolveGameRoot(start).Problem);
        Assert.Equal(SptRootProblem.NotFound, SptRootResolver.ResolveServerRoot(start).Problem);
    }

    [Fact]
    public void AMissingStartDirectoryIsItsOwnProblem()
    {
        var result = SptRootResolver.ResolveGameRoot(At("does", "not", "exist"));

        Assert.Equal(SptRootProblem.StartDirectoryMissing, result.Problem);
    }

    [Fact]
    public void ANullStartDirectoryIsTreatedTheSameWay()
    {
        Assert.Equal(SptRootProblem.StartDirectoryMissing, SptRootResolver.ResolveGameRoot(null).Problem);
        Assert.Equal(SptRootProblem.StartDirectoryMissing, SptRootResolver.ResolveServerRoot(null).Problem);
    }

    [Fact]
    public void AConfiguredRootWinsOverProbing()
    {
        Build41();
        var elsewhere = Dir("elsewhere");

        var result = SptRootResolver.ResolveServerRoot(At(), elsewhere);

        Assert.Equal(elsewhere, result.Directory);
        Assert.Equal(elsewhere, result.ConfiguredDirectory);
    }

    [Fact]
    public void AConfiguredRootThatIsNotThereSaysSoRatherThanFallingBack()
    {
        Build41();

        var result = SptRootResolver.ResolveServerRoot(At(), At("nope"));

        Assert.False(result.Found);
        Assert.Equal(SptRootProblem.ConfiguredDirectoryMissing, result.Problem);
    }

    [Fact]
    public void ToGameRoot_FourZeroServerFolderStepsUpToTheGameRoot()
    {
        Build40();

        Assert.Equal(_root, SptInstallationService.ToGameRoot(At("SPT")));
    }

    [Fact]
    public void ToGameRoot_FourOneRuntimeFolderStepsUpToTheGameRoot()
    {
        Build41();

        Assert.Equal(_root, SptInstallationService.ToGameRoot(At("SPT_Runtime")));
    }

    [Fact]
    public void ToGameRoot_TrailingSeparatorStillStepsUp()
    {
        Build40();

        Assert.Equal(_root, SptInstallationService.ToGameRoot(At("SPT") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void ToGameRoot_GameRootIsUnchanged()
    {
        Build40();

        Assert.Equal(_root, SptInstallationService.ToGameRoot(_root));
    }

    [Fact]
    public void ToGameRoot_ServerFolderWithStrayBepInExStillStepsUp()
    {
        Build40();
        Dir("SPT", "BepInEx", "plugins");

        Assert.Equal(_root, SptInstallationService.ToGameRoot(At("SPT")));
    }

    [Fact]
    public void ToGameRoot_StandaloneServerIsUnchanged()
    {
        var server = At("server");
        File_("server", "SPT.Server.exe");

        Assert.Equal(server, SptInstallationService.ToGameRoot(server));
    }

    [Fact]
    public void ToGameRoot_OtherFolderUnderTheGameIsUnchanged()
    {
        Build40();
        var plugins = At("BepInEx", "plugins");

        Assert.Equal(plugins, SptInstallationService.ToGameRoot(plugins));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ToGameRoot_UnsetPathIsUnchanged(string? path)
    {
        Assert.Equal(path, SptInstallationService.ToGameRoot(path));
    }
}
