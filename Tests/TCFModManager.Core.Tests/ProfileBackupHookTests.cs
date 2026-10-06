using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-12 F4: where ModInstallService takes its copies of the SPT profiles - before an install that
// goes ahead, never before one that is refused, and before a removal.
//
public class ProfileBackupHookTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-profilehook-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;
    private readonly ProfileBackups _backups;

    private static int _nextId = 990_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    public ProfileBackupHookTests()
    {
        _install = Path.Combine(_root, "install");
        Write("EscapeFromTarkov.exe", "game");
        Write("SPT/SPT.Server.exe", "server");
        Write("SPT/user/profiles/abc.json", "{}");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
        _backups = new ProfileBackups(Path.Combine(_root, "profile-backups"), keep: 5);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }

        foreach (var id in _ids)
        {
            var kept = Path.Combine(AppPaths.DataDirectory, ModInstallService.OverwrittenDirectoryName, id.ToString());
            try
            {
                if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true);
            }
            catch (IOException) { }
        }
    }

    private void Write(string relative, string content)
    {
        var full = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private sealed class ArchiveHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private Task<ModInstallResult> Install(params (string Path, string Content)[] files)
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);

        var service = new ModInstallService(
            new ModDownloadService(new HttpClient(new ArchiveHandler(ArchiveFileTests.Zip(files)))),
            _manifest,
            profileBackups: _backups);

        return service.InstallAsync(
            new InstallTarget(id, false, $"Test {id}", null, null, null),
            new ModVersion { Id = 1, Version = "1.0.0", Link = "https://example.test/a" },
            _install);
    }

    [Fact]
    public async Task An_install_takes_a_copy_first_and_a_refused_one_does_not()
    {
        // Only BepInEx's own file: every file refused, nothing placed (NothingToPlace).
        await Assert.ThrowsAsync<ModInstallException>(() => Install(("BepInEx/core/BepInEx.dll", "x")));
        Assert.Empty(_backups.List(_install));

        await Install(("BepInEx/plugins/Mod/mod.dll", "the mod"));
        Assert.Equal(ProfileBackups.BeforeInstall, Assert.Single(_backups.List(_install)).Reason);
    }

    [Fact]
    public async Task A_removal_takes_a_copy_when_the_profiles_changed_since_the_last()
    {
        var result = await Install(("BepInEx/plugins/Mod/mod.dll", "the mod"));
        Write("SPT/user/profiles/abc.json", "{ \"changed\": true }");

        var service = new ModInstallService(new ModDownloadService(), _manifest, profileBackups: _backups);
        await service.UninstallAsync(_install, result.Record);

        Assert.Equal(
            [ProfileBackups.BeforeRemove, ProfileBackups.BeforeInstall],
            _backups.List(_install).Select(b => b.Reason));
    }
}
