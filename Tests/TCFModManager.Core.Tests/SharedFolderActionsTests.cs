using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;
using static TCFModManager.Core.Tests.ModMetadataFixture;

namespace TCFModManager.Core.Tests;

public class SharedFolderActionsTests : IDisposable
{
    private const string StanceGuid = "com.fontaine.spt-stance-overhaul";
    private const string LibGuid = "com.fontaine.spt-realism-common-lib";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-shares-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly string _live;
    private readonly string _off;

    public SharedFolderActionsTests()
    {
        _install = Path.Combine(_root, "install");
        _live = Path.Combine(_install, "BepInEx", "plugins", "Realism");
        _off = Path.Combine(_install, "BepInEx", "plugins.disabled", "Realism");
        Directory.CreateDirectory(_install);
        File.WriteAllText(Path.Combine(_install, "EscapeFromTarkov.exe"), "game");

        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin(StanceGuid, "Fontaine-SPTStanceOverhaul", "0.0.2")
            .Dependency(LibGuid, flags: 1)
            .Write(_live, "spt-stance-overhaul.dll");
        File.WriteAllText(Path.Combine(_live, "curves.json"), "{}");
        new ModMetadataFixture(Kind.Plain).Constructor(_ => { })
            .Plugin(LibGuid, "Fontaine-RealismCommonLib", "0.0.2")
            .Write(_live, "spt-realism-common-lib.dll");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Disable_Share_MovesOnlyItsOwnFiles()
    {
        var outcome = ModDisableService.Apply([Stance()], disable: true, _install);

        Assert.Empty(outcome.Failed);
        Assert.Equal(2, outcome.Moved.Count);
        Assert.Equal(["curves.json", "spt-stance-overhaul.dll"], Names(_off));
        Assert.Equal(["spt-realism-common-lib.dll"], Names(_live));

        var mods = Scan();
        var stance = Assert.Single(mods, m => m.ShareOwnerModId == 3136);
        Assert.True(stance.IsDisabled);
        Assert.Equal("Realism", stance.Name);
        Assert.False(Assert.Single(mods, m => m.ShareOwnerModId == 3135).IsDisabled);
    }

    [Fact]
    public void Enable_Share_PutsItBack_AndRemovesTheEmptiedDisabledFolders()
    {
        ModDisableService.Apply([Stance()], disable: true, _install);

        var outcome = ModDisableService.Apply([Assert.Single(Scan(), m => m.ShareOwnerModId == 3136)], disable: false, _install);

        Assert.Empty(outcome.Failed);
        Assert.Equal(["curves.json", "spt-realism-common-lib.dll", "spt-stance-overhaul.dll"], Names(_live));
        Assert.False(Directory.Exists(Path.Combine(_install, "BepInEx", "plugins.disabled")));
    }

    [Fact]
    public void Revert_OfADisabledShare_PutsItsFilesBack()
    {
        var outcome = ModDisableService.Apply([Stance()], disable: true, _install);

        var undo = ModDisableService.Revert(outcome.Moved, _install);

        Assert.Empty(undo.Failed);
        Assert.Equal(["curves.json", "spt-realism-common-lib.dll", "spt-stance-overhaul.dll"], Names(_live));
        Assert.False(Directory.Exists(Path.Combine(_install, "BepInEx", "plugins.disabled")));
    }

    [Fact]
    public void Disable_Share_WhenAFileIsInTheWay_MovesNothing()
    {
        Directory.CreateDirectory(_off);
        File.WriteAllText(Path.Combine(_off, "curves.json"), "someone else's");

        var outcome = ModDisableService.Apply([Stance()], disable: true, _install);

        Assert.Empty(outcome.Moved);
        Assert.Equal("Realism", Assert.Single(outcome.Failed).ModName);
        Assert.Equal(["curves.json", "spt-realism-common-lib.dll", "spt-stance-overhaul.dll"], Names(_live));
        Assert.Equal("someone else's", File.ReadAllText(Path.Combine(_off, "curves.json")));
    }

    [Fact]
    public void Disable_EveryShare_LeavesNoEmptyLiveFolder()
    {
        var outcome = ModDisableService.Apply(Scan(), disable: true, _install);

        Assert.Empty(outcome.Failed);
        Assert.False(Directory.Exists(_live));
        Assert.Equal(["curves.json", "spt-realism-common-lib.dll", "spt-stance-overhaul.dll"], Names(_off));
    }

    [Fact]
    public void DisabledShareBesideAnEnabledNeighbour_IsNotADuplicate()
    {
        ModDisableService.Apply([Stance()], disable: true, _install);

        Assert.Empty(ModDisableService.DuplicatePairs(Scan()));
    }

    [Fact]
    public void ShareInBothStates_IsADuplicate_AndSettlingItSetsAsideOnlyItsFiles()
    {
        Directory.CreateDirectory(_off);
        File.Copy(Path.Combine(_live, "spt-stance-overhaul.dll"), Path.Combine(_off, "spt-stance-overhaul.dll"));

        var pair = Assert.Single(ModDisableService.DuplicatePairs(Scan()));
        Assert.Equal(3136, pair.Enabled.ShareOwnerModId);

        var outcome = ModDisableService.ResolveDuplicate(_install, pair, keepEnabled: true, DateTimeOffset.Now);

        Assert.Empty(outcome.Failed);
        Assert.False(Directory.Exists(_off));
        Assert.Equal(["curves.json", "spt-realism-common-lib.dll", "spt-stance-overhaul.dll"], Names(_live));
    }

    [Fact]
    public void RemoveHandInstalledShare_TakesOnlyItsFiles_AndUndoPutsThemBack()
    {
        var stance = Assert.Single(Scan(Listings()), m => m.ShareOwnerModId == 3136);

        var held = Service().RemoveHandInstalledShare(stance, _install, "SPT Stance Overhaul");

        Assert.Equal(["spt-realism-common-lib.dll"], Names(_live));
        Assert.True(File.Exists(Path.Combine(held!, RemovedMods.FilesFolder, "BepInEx", "plugins", "Realism", "curves.json")));

        var undo = Service().UndoRemoval(_install, held!);

        Assert.True(undo.Ran);
        Assert.Equal(["curves.json", "spt-realism-common-lib.dll", "spt-stance-overhaul.dll"], Names(_live));
    }

    [Fact]
    public void RemoveHandInstalledShare_LastOneOut_RemovesTheEmptyFolder()
    {
        var service = Service();
        foreach (var share in Scan(Listings())) service.RemoveHandInstalledShare(share, _install, share.Name);

        Assert.False(Directory.Exists(_live));
        Assert.True(Directory.Exists(Path.Combine(_install, "BepInEx", "plugins")));
    }

    [Fact]
    public void RemoveHandInstalledShare_OfAWholeFolderEntry_RemovesTheFolder()
    {
        var whole = Assert.Single(InstalledModScanner.Scan(_install));

        Service().RemoveHandInstalledShare(whole, _install, "Realism");

        Assert.False(Directory.Exists(_live));
    }

    private InstalledMod Stance() => Assert.Single(Scan(), m => m.ShareOwnerModId == 3136 && !m.IsDisabled);

    private List<InstalledMod> Scan(ModOwnership? owners = null) =>
        InstalledModScanner.Scan(_install, owners ?? Records());

    private ModOwnership Records() => new(
    [
        Record(3136, StanceGuid, new DateTimeOffset(2026, 10, 15, 8, 2, 46, TimeSpan.Zero),
            "BepInEx/plugins/Realism/curves.json", "BepInEx/plugins/Realism/spt-stance-overhaul.dll"),
        Record(3135, LibGuid, new DateTimeOffset(2026, 10, 15, 8, 2, 47, TimeSpan.Zero),
            "BepInEx/plugins/Realism/spt-realism-common-lib.dll"),
    ]);

    private static ModOwnership Listings() => new([],
    [
        new KeyValuePair<string, int>(StanceGuid, 3136),
        new KeyValuePair<string, int>(LibGuid, 3135),
    ]);

    private InstalledModRecord Record(int id, string guid, DateTimeOffset placed, params string[] files) => new()
    {
        ModId = id,
        Name = $"Mod {id}",
        Guid = guid,
        Version = "0.0.2",
        InstalledAt = placed,
        Files = [.. files],
        InstallPath = InstallStamp.Of(_install),
    };

    private ModInstallService Service() => new(
        new ModDownloadService(new HttpClient(new NoNetwork())),
        new ModInstallManifestService(Path.Combine(_root, "installed-mods.json")),
        removedMods: new RemovedMods(() => RemovedModsRetention.UntilCleared));

    private static List<string> Names(string dir) =>
        Directory.Exists(dir)
            ? [.. Directory.EnumerateFiles(dir).Select(Path.GetFileName).Select(n => n!).Order(StringComparer.Ordinal)]
            : [];

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
