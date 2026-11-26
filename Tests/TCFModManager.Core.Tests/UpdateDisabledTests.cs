using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-50 S1: updating a disabled mod. Mods are installed with a real InstallAsync into a throwaway SPT
// layout, disabled by moving their folders the way ModDisableService does, then updated. Every test
// asserts on the disk.
//
public class UpdateDisabledTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-update-disabled-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;

    private static int _nextId = 980_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    public UpdateDisabledTests()
    {
        _install = Path.Combine(_root, "install");
        Write("EscapeFromTarkov.exe", "game");
        Write("SPT/SPT.Server.exe", "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installedMods.json"));
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

    private Task<ModInstallResult> Install(InstallTarget target, string version, params (string Path, string Content)[] files) =>
        Service(ArchiveFileTests.Zip(files))
            .InstallAsync(target, new ModVersion { Id = version.GetHashCode(), Version = version, Link = "https://example.test/a" }, _install);

    private InstalledModRecord RecordOf(InstallTarget target) =>
        _manifest.Load().Find(target.Id, target.IsAddon)!;

    [Fact]
    public async Task Update_OfADisabledMod_StaysDisabled_WithNoLiveCopy()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "client 1"), ("user/mods/ModServer/server.dll", "server 1"));
        Disable("BepInEx/plugins/Mod");
        Disable("SPT/user/mods/ModServer");

        var result = await Install(target, "2.0.0", ("BepInEx/plugins/Mod/mod.dll", "client 2"), ("user/mods/ModServer/server.dll", "server 2"));

        Assert.True(result.SetAsideAgain);
        Assert.Equal("client 2", File.ReadAllText(Full("BepInEx/plugins.disabled/Mod/mod.dll")));
        Assert.Equal("server 2", File.ReadAllText(Full("SPT/user/mods.disabled/ModServer/server.dll")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins/Mod")));
        Assert.False(Directory.Exists(Full("SPT/user/mods/ModServer")));
        Assert.Equal("2.0.0", RecordOf(target).Version);
        Assert.Contains("BepInEx/plugins/Mod/mod.dll", RecordOf(target).Files);
    }

    [Fact]
    public async Task Update_OfAHalfDisabledMod_ComesOutWholeAndLive()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "client 1"), ("user/mods/ModServer/server.dll", "server 1"));
        Disable("SPT/user/mods/ModServer");

        var result = await Install(target, "2.0.0", ("BepInEx/plugins/Mod/mod.dll", "client 2"), ("user/mods/ModServer/server.dll", "server 2"));

        Assert.Null(result.SetAsideAgain);
        Assert.Equal("client 2", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal("server 2", File.ReadAllText(Full("SPT/user/mods/ModServer/server.dll")));
        Assert.False(Directory.Exists(Full("SPT/user/mods.disabled")));
    }

    [Fact]
    public async Task Update_RefusesAModOnDiskTwice_AndChangesNothing()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "client 1"));
        Disable("BepInEx/plugins/Mod");
        Write("BepInEx/plugins/Mod/mod.dll", "a live copy");

        var ex = await Assert.ThrowsAsync<ModInstallException>(() =>
            Install(target, "2.0.0", ("BepInEx/plugins/Mod/mod.dll", "client 2")));

        Assert.Equal(ModInstallFailure.RemovalDuplicate, ex.Reason);
        Assert.Equal(ModInstallAction.Install, ex.Action);
        Assert.Equal("a live copy", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal("client 1", File.ReadAllText(Full("BepInEx/plugins.disabled/Mod/mod.dll")));
        Assert.Equal("1.0.0", RecordOf(target).Version);
    }

    [Fact]
    public async Task Update_OfADisabledMod_KeepsFilesItDidntInstallBesideIt()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("user/mods/ModServer/server.dll", "server 1"));
        Disable("SPT/user/mods/ModServer");
        Write("SPT/user/mods.disabled/ModServer/presets/mine.json", "the user's");

        var result = await Install(target, "2.0.0", ("user/mods/ModServer/server.dll", "server 2"));

        Assert.True(result.SetAsideAgain);
        Assert.Equal("server 2", File.ReadAllText(Full("SPT/user/mods.disabled/ModServer/server.dll")));
        Assert.Equal("the user's", File.ReadAllText(Full("SPT/user/mods.disabled/ModServer/presets/mine.json")));
        Assert.False(Directory.Exists(Full("SPT/user/mods/ModServer")));
    }

    [Fact]
    public async Task Update_OfADisabledMod_SetsAsideAFolderTheNewVersionAdds()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Old/mod.dll", "client 1"));
        Disable("BepInEx/plugins/Old");

        var result = await Install(target, "2.0.0", ("BepInEx/plugins/New/mod.dll", "client 2"));

        Assert.True(result.SetAsideAgain);
        Assert.Equal("client 2", File.ReadAllText(Full("BepInEx/plugins.disabled/New/mod.dll")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins/New")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins/Old")));
    }

    [Fact]
    public async Task Update_OfAnAddonDisabledInsideItsLiveParentsFolder_StaysDisabled()
    {
        var parent = NewTarget();
        var addon = NewTarget();
        await Install(parent, "1.0.0", ("BepInEx/plugins/Parent/parent.dll", "parent"));
        await Install(addon, "1.0.0", ("BepInEx/plugins/Parent/addon.dll", "addon 1"));
        ModDisableService.MoveFiles(_install, ["BepInEx/plugins/Parent/addon.dll"], disable: true, "addon");

        var result = await Install(addon, "2.0.0", ("BepInEx/plugins/Parent/addon.dll", "addon 2"));

        Assert.True(result.SetAsideAgain);
        Assert.Equal("addon 2", File.ReadAllText(Full("BepInEx/plugins.disabled/Parent/addon.dll")));
        Assert.False(File.Exists(Full("BepInEx/plugins/Parent/addon.dll")));
        Assert.Equal("parent", File.ReadAllText(Full("BepInEx/plugins/Parent/parent.dll")));
    }

    [Fact]
    public async Task Update_OfAnEnabledMod_IsUnchanged()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "client 1"));

        var result = await Install(target, "2.0.0", ("BepInEx/plugins/Mod/mod.dll", "client 2"));

        Assert.Null(result.SetAsideAgain);
        Assert.Equal("client 2", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins.disabled")));
    }
}
