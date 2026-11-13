using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-46 Q6: updating a mod whose client half is set aside brings it back, updates it, and sets it
// aside again - one copy, still aside.
//
public class InstallClientHalfAsideTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-client-aside-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly int _id = 970_000 + Random.Shared.Next(0, 10_000);
    private readonly ModInstallManifestService _manifest;
    private readonly ModInstallService _service;

    public InstallClientHalfAsideTests()
    {
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(Path.Combine(_install, "SPT"));
        File.WriteAllText(Path.Combine(_install, "EscapeFromTarkov.exe"), "game");
        File.WriteAllText(Path.Combine(_install, "SPT", "SPT.Server.exe"), "server");

        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
        _service = new ModInstallService(new ModDownloadService(new HttpClient(new NoDownloads())), _manifest);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private sealed class NoDownloads : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("nothing should be downloaded");
    }

    private string LiveClient => Path.Combine(_install, "BepInEx", "plugins", "DynamicMaps", "DynamicMaps.dll");
    private string AsideClient => Path.Combine(_install, "BepInEx", "plugins.disabled", "DynamicMaps", "DynamicMaps.dll");
    private string LiveServer => Path.Combine(_install, "SPT", "user", "mods", "DynamicMaps", "server.dll");

    private async Task<InstalledModRecord> InstallAsync(string version)
    {
        var archive = Path.Combine(_root, $"dm-{version}.bin");
        File.WriteAllBytes(archive, ArchiveFileTests.Zip(
            ("BepInEx/plugins/DynamicMaps/DynamicMaps.dll", "client " + version),
            ("user/mods/DynamicMaps/server.dll", "server " + version)));

        var result = await _service.InstallAsync(
            new InstallTarget(_id, false, "Dynamic Maps", null, null, null),
            new ModVersion { Id = version.GetHashCode(), Version = version },
            _install,
            downloadedArchive: archive);

        return result.Record;
    }

    [Fact]
    public async Task AnUpdateKeepsAClientHalfThatWasSetAsideAside()
    {
        var first = await InstallAsync("1.0.0");

        var aside = ModDisableService.MoveFiles(_install, first.Files.Where(ModDisableService.IsClientFile), disable: true, "Dynamic Maps");
        Assert.Single(aside.Moved);
        Assert.Equal(["BepInEx/plugins/DynamicMaps/DynamicMaps.dll"], ModDisableService.ParkedClientFiles(_install, first));

        var second = await InstallAsync("1.1.0");

        Assert.False(File.Exists(LiveClient));
        Assert.Equal("client 1.1.0", File.ReadAllText(AsideClient));
        Assert.Equal("server 1.1.0", File.ReadAllText(LiveServer));
        Assert.Contains("BepInEx/plugins/DynamicMaps/DynamicMaps.dll", second.Files);
        Assert.Equal(["BepInEx/plugins/DynamicMaps/DynamicMaps.dll"], ModDisableService.ParkedClientFiles(_install, second));
    }

    [Fact]
    public async Task AnUpdateWithTheClientHalfLiveLeavesItLive()
    {
        await InstallAsync("1.0.0");
        await InstallAsync("1.1.0");

        Assert.Equal("client 1.1.0", File.ReadAllText(LiveClient));
        Assert.False(File.Exists(AsideClient));
    }

    [Fact]
    public async Task NothingIsAsideWhileAnyClientFileIsLive()
    {
        var record = await InstallAsync("1.0.0");

        Assert.Empty(ModDisableService.ParkedClientFiles(_install, record));
        Assert.True(ModDisableService.IsClientFile("BepInEx/patchers/X.dll"));
        Assert.False(ModDisableService.IsClientFile("SPT/user/mods/DynamicMaps/server.dll"));
        Assert.False(ModDisableService.IsClientFile("BepInEx/config/x.cfg"));
    }

    [Fact]
    public void MovingRefusesWhenSomethingIsAlreadyThere()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LiveClient)!);
        Directory.CreateDirectory(Path.GetDirectoryName(AsideClient)!);
        File.WriteAllText(LiveClient, "live");
        File.WriteAllText(AsideClient, "aside");

        var outcome = ModDisableService.MoveFiles(
            _install, ["BepInEx/plugins/DynamicMaps/DynamicMaps.dll"], disable: true, "Dynamic Maps");

        Assert.Empty(outcome.Moved);
        Assert.Single(outcome.Failed);
        Assert.Equal("live", File.ReadAllText(LiveClient));
    }
}
