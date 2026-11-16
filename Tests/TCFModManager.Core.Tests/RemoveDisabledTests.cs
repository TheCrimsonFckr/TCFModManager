using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-49 S1: removing a disabled mod, or the disabled half of one, from where it sits. Mods are
// installed with a real InstallAsync into a throwaway SPT layout, disabled by moving their folders
// the way ModDisableService does, then removed and put back. Every test asserts on the disk.
//
public class RemoveDisabledTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-remove-disabled-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;

    private static int _nextId = 970_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    public RemoveDisabledTests()
    {
        _install = Path.Combine(_root, "install");
        Write("EscapeFromTarkov.exe", "game");
        Write("SPT/SPT.Server.exe", "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }

        foreach (var id in _ids)
        {
            foreach (var key in new[] { id.ToString(), $"{id}-addon" })
            {
                var kept = Path.Combine(AppPaths.DataDirectory, ModInstallService.OverwrittenDirectoryName, key);
                try { if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true); }
                catch (IOException) { }
            }
        }
    }

    private string Full(string relative) => Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));

    private string Write(string relative, string content)
    {
        var full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private void Disable(string modFolder)
    {
        DisabledModPaths.TryGetRelativeCounterpart(modFolder, out var counterpart);
        Directory.CreateDirectory(Path.GetDirectoryName(Full(counterpart))!);
        Directory.Move(Full(modFolder), Full(counterpart));
    }

    private InstallTarget NewTarget()
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        return new InstallTarget(id, false, $"Test {id}", null, null, null);
    }

    private sealed class ArchiveHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private ModInstallService Service(byte[]? archive = null) => new(
        new ModDownloadService(new HttpClient(new ArchiveHandler(archive ?? []))),
        _manifest,
        removedMods: new RemovedMods(() => RemovedModsRetention.UntilCleared));

    private Task<ModInstallResult> Install(InstallTarget target, params (string Path, string Content)[] files) =>
        Service(ArchiveFileTests.Zip(files))
            .InstallAsync(target, new ModVersion { Id = 1, Version = "1.0.0", Link = "https://example.test/a" }, _install);

    private InstalledModRecord RecordOf(InstallTarget target) =>
        _manifest.Load().Find(target.Id, target.IsAddon)!;

    private static string Held(string folder, string relative) =>
        Path.Combine(folder, RemovedMods.FilesFolder, relative.Replace('/', Path.DirectorySeparatorChar));

    // --- Where a recorded path is now -----------------------------------------------------------

    [Fact]
    public void WhereNow_FindsTheLivePath_ThenTheDisabledOne_ElseLeavesItAlone()
    {
        Write("BepInEx/plugins/Live/a.dll", "live");
        Write("BepInEx/plugins.disabled/Off/b.dll", "off");

        Assert.Equal("BepInEx/plugins/Live/a.dll", DisabledModPaths.WhereNow(_install, "BepInEx/plugins/Live/a.dll"));
        Assert.Equal("BepInEx/plugins.disabled/Off/b.dll", DisabledModPaths.WhereNow(_install, "BepInEx/plugins/Off/b.dll"));
        Assert.Equal("BepInEx/plugins/Gone/c.dll", DisabledModPaths.WhereNow(_install, "BepInEx/plugins/Gone/c.dll"));
        Assert.Equal("BepInEx/config/x.cfg", DisabledModPaths.WhereNow(_install, "BepInEx/config/x.cfg"));
    }

    [Fact]
    public void IsOnDiskTwice_OnlyWhenBothSidesAreThere()
    {
        Write("BepInEx/plugins/Twice/a.dll", "live");
        Write("BepInEx/plugins.disabled/Twice/a.dll", "off");
        Write("BepInEx/plugins.disabled/Once/a.dll", "off");

        Assert.True(DisabledModPaths.IsOnDiskTwice(_install, "BepInEx/plugins/Twice"));
        Assert.False(DisabledModPaths.IsOnDiskTwice(_install, "BepInEx/plugins/Once"));
    }

    // --- Remove --------------------------------------------------------------------------------

    [Fact]
    public async Task Remove_DisabledMod_TakesItFromTheDisabledFolders_AndDropsTheRecord()
    {
        var target = NewTarget();
        await Install(target,
            ("BepInEx/plugins/Mod/mod.dll", "client"),
            ("user/mods/ModServer/server.dll", "server"));
        Disable("BepInEx/plugins/Mod");
        Disable("SPT/user/mods/ModServer");

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal(2, result.FilesDeleted);
        Assert.Equal("client", File.ReadAllText(Held(result.HoldingFolder!, "BepInEx/plugins.disabled/Mod/mod.dll")));
        Assert.Equal("server", File.ReadAllText(Held(result.HoldingFolder!, "SPT/user/mods.disabled/ModServer/server.dll")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins.disabled")));
        Assert.False(Directory.Exists(Full("SPT/user/mods.disabled")));
        Assert.True(Directory.Exists(Full("BepInEx/plugins")));
        Assert.Null(_manifest.Load().Find(target.Id, false));

        var log = RemovedMods.ReadLog(result.HoldingFolder!)!;
        Assert.All(log.Entries.Where(e => e.Outcome == RemovalOutcome.Moved), e => Assert.Contains(".disabled/", e.Path));
    }

    [Fact]
    public async Task Remove_HalfDisabledMod_TakesBothHalves()
    {
        var target = NewTarget();
        await Install(target,
            ("BepInEx/plugins/Mod/mod.dll", "client"),
            ("user/mods/ModServer/server.dll", "server"));
        Disable("SPT/user/mods/ModServer");

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal(2, result.FilesDeleted);
        Assert.False(Directory.Exists(Full("BepInEx/plugins/Mod")));
        Assert.False(Directory.Exists(Full("SPT/user/mods.disabled/ModServer")));
        Assert.Equal("client", File.ReadAllText(Held(result.HoldingFolder!, "BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal("server", File.ReadAllText(Held(result.HoldingFolder!, "SPT/user/mods.disabled/ModServer/server.dll")));
    }

    [Fact]
    public async Task Remove_RefusesAModOnDiskTwice_AndMovesNothing()
    {
        var target = NewTarget();
        await Install(target, ("BepInEx/plugins/Mod/mod.dll", "client"));
        Disable("BepInEx/plugins/Mod");
        Write("BepInEx/plugins/Mod/mod.dll", "a live copy");

        var ex = await Assert.ThrowsAsync<ModInstallException>(() =>
            Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete));

        Assert.Equal(ModInstallFailure.RemovalDuplicate, ex.Reason);
        Assert.Equal("BepInEx/plugins/Mod", ex.Folder);
        Assert.Equal("a live copy", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal("client", File.ReadAllText(Full("BepInEx/plugins.disabled/Mod/mod.dll")));
        Assert.NotNull(_manifest.Load().Find(target.Id, false));
        Assert.Empty(RemovedMods.List(_install));
    }

    [Fact]
    public async Task Remove_KeepsADisabledModsConfigs_AndUndoPutsThemBackDisabled()
    {
        var target = NewTarget();
        await Install(target,
            ("user/mods/CfgMod/mod.dll", "server"),
            ("user/mods/CfgMod/config/config.json", "{ \"a\": 1 }"));
        Disable("SPT/user/mods/CfgMod");

        var removal = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Keep);

        Assert.Equal(1, removal.ConfigsKept);
        Assert.True(File.Exists(Path.Combine(removal.ConfigsFolder!, "SPT", "user", "mods.disabled", "CfgMod", "config", "config.json")));
        Assert.False(Directory.Exists(Full("SPT/user/mods.disabled")));

        Assert.Empty(Service().UndoRemoval(_install, removal.HoldingFolder!).Blocked);

        Assert.Equal("server", File.ReadAllText(Full("SPT/user/mods.disabled/CfgMod/mod.dll")));
        Assert.Equal("{ \"a\": 1 }", File.ReadAllText(Full("SPT/user/mods.disabled/CfgMod/config/config.json")));
        Assert.False(Directory.Exists(Full("SPT/user/mods/CfgMod")));
    }

    [Fact]
    public async Task Remove_PutsAnOriginalBackOnTheDisabledSide_AndUndoTakesItAside()
    {
        Write("BepInEx/plugins/Mod/old.dll", "earlier copy");
        var target = NewTarget();
        await Install(target,
            ("BepInEx/plugins/Mod/mod.dll", "client"),
            ("BepInEx/plugins/Mod/old.dll", "the mod's copy"));
        Disable("BepInEx/plugins/Mod");

        var removal = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal(1, removal.OriginalsRestored);
        Assert.Equal("earlier copy", File.ReadAllText(Full("BepInEx/plugins.disabled/Mod/old.dll")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins/Mod")));

        var undo = Service().UndoRemoval(_install, removal.HoldingFolder!);

        Assert.Empty(undo.Blocked);
        Assert.Equal("the mod's copy", File.ReadAllText(Full("BepInEx/plugins.disabled/Mod/old.dll")));
        Assert.Equal("client", File.ReadAllText(Full("BepInEx/plugins.disabled/Mod/mod.dll")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins/Mod")));
    }

    [Fact]
    public async Task Remove_SaysWhenADisabledModsFolderStays_ForAFileItDidntInstall()
    {
        var target = NewTarget();
        await Install(target, ("BepInEx/plugins/Mod/mod.dll", "client"));
        Disable("BepInEx/plugins/Mod");
        Write("BepInEx/plugins.disabled/Mod/notes.txt", "the user's");

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        var left = Assert.Single(result.FoldersLeft);
        Assert.Equal("BepInEx/plugins.disabled/Mod", left.Folder);
        Assert.Equal(1, left.Files);
        Assert.Equal("the user's", File.ReadAllText(Full("BepInEx/plugins.disabled/Mod/notes.txt")));
    }

    // --- Undo ----------------------------------------------------------------------------------

    [Fact]
    public async Task Undo_PutsADisabledModBackDisabled_WithItsRecord()
    {
        var target = NewTarget();
        await Install(target,
            ("BepInEx/plugins/Mod/mod.dll", "client"),
            ("user/mods/ModServer/server.dll", "server"));
        Disable("BepInEx/plugins/Mod");
        Disable("SPT/user/mods/ModServer");
        var removal = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        var undo = Service().UndoRemoval(_install, removal.HoldingFolder!);

        Assert.True(undo.Ran);
        Assert.Empty(undo.Blocked);
        Assert.Equal("client", File.ReadAllText(Full("BepInEx/plugins.disabled/Mod/mod.dll")));
        Assert.Equal("server", File.ReadAllText(Full("SPT/user/mods.disabled/ModServer/server.dll")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins/Mod")));
        Assert.Contains("BepInEx/plugins/Mod/mod.dll", _manifest.Load().Find(target.Id, false)!.Files);
    }

    [Fact]
    public async Task Undo_RefusesToMakeADuplicate()
    {
        var target = NewTarget();
        await Install(target, ("BepInEx/plugins/Mod/mod.dll", "client"));
        var removal = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);
        Write("BepInEx/plugins.disabled/Mod/mod.dll", "a disabled copy");

        var undo = Service().UndoRemoval(_install, removal.HoldingFolder!);

        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], undo.Blocked);
        Assert.False(Directory.Exists(Full("BepInEx/plugins/Mod")));
        Assert.Equal("client", File.ReadAllText(Held(removal.HoldingFolder!, "BepInEx/plugins/Mod/mod.dll")));
    }

    [Fact]
    public void Undo_OfADisabledHandInstalledFolder_PutsItBackDisabled()
    {
        Write("BepInEx/plugins.disabled/Hand/h.dll", "by hand");
        var held = Service().RemoveHandInstalled([Full("BepInEx/plugins.disabled/Hand")], _install, "Hand");

        Assert.False(Directory.Exists(Full("BepInEx/plugins.disabled/Hand")));

        var undo = Service().UndoRemoval(_install, held!);

        Assert.True(undo.Ran);
        Assert.Equal("by hand", File.ReadAllText(Full("BepInEx/plugins.disabled/Hand/h.dll")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins/Hand")));
    }
}
