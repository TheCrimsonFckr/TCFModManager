using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-12 F7: the queue downloads ahead and hands InstallAsync the archive it already has. Nothing is
// downloaded then, and the archive is left where it is - deleting it is the queue's job.
//
public class InstallFromDownloadedArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-predownloaded-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly int _id = 960_000 + Random.Shared.Next(0, 10_000);

    public InstallFromDownloadedArchiveTests()
    {
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(Path.Combine(_install, "SPT"));
        File.WriteAllText(Path.Combine(_install, "EscapeFromTarkov.exe"), "game");
        File.WriteAllText(Path.Combine(_install, "SPT", "SPT.Server.exe"), "server");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class NoDownloads : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("nothing should be downloaded");
    }

    [Fact]
    public async Task The_archive_given_is_installed_and_left_in_place()
    {
        var archive = Path.Combine(_root, "queue-test.bin");
        File.WriteAllBytes(archive, ArchiveFileTests.Zip(("BepInEx/plugins/Mod/mod.dll", "the mod")));

        var service = new ModInstallService(
            new ModDownloadService(new HttpClient(new NoDownloads())),
            new ModInstallManifestService(Path.Combine(_root, "installed-mods.json")));

        // No link at all: a downloaded archive needs none.
        var result = await service.InstallAsync(
            new InstallTarget(_id, false, "Test", null, null, null),
            new ModVersion { Id = 1, Version = "1.0.0" },
            _install,
            downloadedArchive: archive);

        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], result.Record.Files);
        Assert.Equal("the mod", File.ReadAllText(Path.Combine(_install, "BepInEx", "plugins", "Mod", "mod.dll")));
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public async Task An_addon_installed_anyway_records_the_mod_version_and_a_normal_install_clears_it()
    {
        var archive = Path.Combine(_root, "addon.bin");
        File.WriteAllBytes(archive, ArchiveFileTests.Zip(("BepInEx/plugins/Mod/addon.dll", "the addon")));

        var manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
        var service = new ModInstallService(new ModDownloadService(new HttpClient(new NoDownloads())), manifest);
        var addon = new InstallTarget(_id, true, "Addon", null, null, null);

        await service.InstallAsync(addon with { FitOverride = "1.1.3" }, new ModVersion { Id = 1, Version = "1.0.0" }, _install, downloadedArchive: archive);
        Assert.Equal("1.1.3", manifest.Load().Find(_id, isAddon: true)?.FitOverride);

        await service.InstallAsync(addon, new ModVersion { Id = 2, Version = "1.1.0" }, _install, downloadedArchive: archive);
        Assert.Null(manifest.Load().Find(_id, isAddon: true)?.FitOverride);
    }

    [Fact]
    public async Task A_mod_never_records_a_fit_override()
    {
        var archive = Path.Combine(_root, "mod.bin");
        File.WriteAllBytes(archive, ArchiveFileTests.Zip(("BepInEx/plugins/Mod/mod.dll", "the mod")));

        var manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
        var service = new ModInstallService(new ModDownloadService(new HttpClient(new NoDownloads())), manifest);

        await service.InstallAsync(
            new InstallTarget(_id, false, "Test", null, null, null, FitOverride: "1.1.3"),
            new ModVersion { Id = 1, Version = "1.0.0" },
            _install,
            downloadedArchive: archive);

        Assert.Null(manifest.Load().Find(_id, isAddon: false)?.FitOverride);
    }

    [Fact]
    public void Queue_archives_sit_in_the_install_scratch_folder()
    {
        var path = QueueArchives.NewPath(_install);

        Assert.Equal(Path.Combine(_install, ".tcfmm-work"), Path.GetDirectoryName(path));
        Assert.StartsWith("queue-", Path.GetFileName(path));
    }

    [Fact]
    public void The_scratch_folder_is_removed_only_when_empty()
    {
        var work = Path.Combine(_install, ".tcfmm-work");
        var waiting = QueueArchives.NewPath(_install);
        File.WriteAllText(waiting, "archive");

        ModInstallService.RemoveWorkFolderIfEmpty(_install);
        Assert.True(File.Exists(waiting));

        File.Delete(waiting);
        ModInstallService.RemoveWorkFolderIfEmpty(_install);
        Assert.False(Directory.Exists(work));

        ModInstallService.RemoveWorkFolderIfEmpty(_install);
        ModInstallService.RemoveWorkFolderIfEmpty(null);
    }
}
