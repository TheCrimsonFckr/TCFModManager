using System.IO.Compression;
using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Folders an install creates outside a mod's own folder. The APBS Realistic preset ships an optional
// "JSON CONFIG FILE (RECOMMENDED)" folder that landed in the install root and stayed there, empty, after
// the preset was removed (GitHub #11 follow-up, 2026-11-27).
//
public class CreatedFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-created-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;
    private static int _nextId = 2_800_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    private const string Apbs = "SPT_Runtime/user/mods/acidphantasm-progressivebotsystem";
    private const string Extra = "JSON CONFIG FILE (RECOMMENDED)";

    public CreatedFoldersTests()
    {
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(Path.Combine(_install, "SPT_Runtime", "user", "mods"));
        File.WriteAllText(Path.Combine(_install, "EscapeFromTarkov.exe"), "game");
        File.WriteAllText(Path.Combine(_install, "SPT_Runtime", "SPT.Server.exe"), "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installedMods.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        foreach (var id in _ids)
        {
            var kept = Path.Combine(AppPaths.DataDirectory, ModInstallService.OverwrittenDirectoryName, id.ToString());
            try { if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true); } catch (IOException) { }
        }
    }

    private string Full(string relative) => Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));

    private static byte[] Zip(params (string Path, string Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private sealed class Handler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private ModInstallService Service(byte[] archive) => new(new ModDownloadService(new HttpClient(new Handler(archive))), _manifest);

    private InstallTarget NewTarget(string name)
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        return new InstallTarget(id, false, $"{name} {id}", null, null, null);
    }

    private static byte[] PresetArchive(string preset) => Zip(
        ($"{Apbs}/Presets/{preset}/manifest.json", "{}"),
        ($"{Extra}/SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/{preset}.json", "{}"));

    private async Task<InstalledModRecord> InstallAsync(InstallTarget target, byte[] archive)
    {
        await Service(archive).InstallAsync(target, new ModVersion { Id = 1, Version = "1.0.0", Link = "https://example.test/a" }, _install);
        return _manifest.Load().Mods.Single(target.Matches);
    }

    [Fact]
    public async Task An_install_records_every_folder_it_created_and_none_that_were_there()
    {
        var record = await InstallAsync(NewTarget("Preset"), PresetArchive("realistic"));

        Assert.Contains(Extra, record.CreatedFolders);
        Assert.Contains($"{Extra}/SPT_Runtime/user/mods/acidphantasm-progressivebotsystem", record.CreatedFolders);
        Assert.Contains($"{Apbs}/Presets/realistic", record.CreatedFolders);
        Assert.DoesNotContain("SPT_Runtime/user/mods", record.CreatedFolders);
        Assert.DoesNotContain("SPT_Runtime", record.CreatedFolders);
    }

    [Fact]
    public async Task A_removal_takes_the_folders_it_created_outside_the_mods_folder()
    {
        var target = NewTarget("Preset");
        var record = await InstallAsync(target, PresetArchive("realistic"));

        await Service([]).UninstallAsync(_install, record, ConfigAction.Delete);

        Assert.False(Directory.Exists(Full(Extra)));
        Assert.False(Directory.Exists(Full(Apbs)));
        Assert.True(Directory.Exists(Full("SPT_Runtime/user/mods")));
    }

    [Fact]
    public async Task A_created_folder_the_user_has_put_something_in_stays()
    {
        var target = NewTarget("Preset");
        var record = await InstallAsync(target, PresetArchive("realistic"));
        File.WriteAllText(Full($"{Extra}/notes.txt"), "mine");

        await Service([]).UninstallAsync(_install, record, ConfigAction.Delete);

        Assert.True(File.Exists(Full($"{Extra}/notes.txt")));
        Assert.False(Directory.Exists(Full($"{Extra}/SPT_Runtime")));
    }

    [Fact]
    public async Task A_folder_two_installs_created_goes_with_the_last_one_out()
    {
        var first = await InstallAsync(NewTarget("First"), PresetArchive("realistic"));
        var second = await InstallAsync(NewTarget("Second"), PresetArchive("extended"));

        Assert.Contains(Extra, second.CreatedFolders);

        await Service([]).UninstallAsync(_install, first, ConfigAction.Delete);
        Assert.True(Directory.Exists(Full(Extra)));

        await Service([]).UninstallAsync(_install, _manifest.Load().Mods.Single(m => m.ModId == second.ModId), ConfigAction.Delete);
        Assert.False(Directory.Exists(Full(Extra)));
    }

    [Theory]
    [InlineData("BepInEx", false)]
    [InlineData("BepInEx/plugins", false)]
    [InlineData("BepInEx/patchers", false)]
    [InlineData("SPT_Runtime", false)]
    [InlineData("SPT_Runtime/user", false)]
    [InlineData("SPT_Runtime/user/mods", false)]
    [InlineData("SPT_Runtime/user/profiles", false)]
    [InlineData("SPT_Runtime/SPT_Data/Server", false)]
    [InlineData("EscapeFromTarkov_Data/Managed", false)]
    [InlineData("BepInEx/core", false)]
    [InlineData("../outside", false)]
    [InlineData(Extra, true)]
    [InlineData(Extra + "/SPT_Runtime/user/mods", true)]
    [InlineData("SPT_Runtime/user/mods/somemod/sub", true)]
    [InlineData("BepInEx/config", true)]
    public void Only_folders_outside_spt_and_the_mod_containers_may_be_taken(string folder, bool allowed)
    {
        Assert.Equal(allowed, InstallPathGuard.MayRemoveCreatedFolder(_install, folder, out _));
    }

    [Fact]
    public void Leftovers_are_the_files_no_record_lists_in_the_mods_folder()
    {
        Directory.CreateDirectory(Full($"{Apbs}/logs"));
        File.WriteAllText(Full($"{Apbs}/apbs.dll"), "dll");
        File.WriteAllText(Full($"{Apbs}/config.json"), "{}");
        File.WriteAllText(Full($"{Apbs}/logs/pmc.json"), "{}");
        Directory.CreateDirectory(Full($"{Apbs}/Presets/realistic"));
        File.WriteAllText(Full($"{Apbs}/Presets/realistic/manifest.json"), "{}");

        var apbs = Record(1594, false, $"{Apbs}/apbs.dll");
        var preset = Record(37, true, $"{Apbs}/Presets/realistic/manifest.json");
        _manifest.Save(new ModInstallManifest { Mods = [apbs, preset] });

        var leftovers = Service([]).LeftoversOf(_install, apbs, [preset]);

        var only = Assert.Single(leftovers);
        Assert.Equal(Apbs, only.Folder);
        Assert.Equal(["config.json", "logs/pmc.json"], only.Files);
    }

    [Fact]
    public void A_folder_another_record_still_has_files_in_is_not_a_leftover()
    {
        Directory.CreateDirectory(Full($"{Apbs}/Presets/realistic"));
        File.WriteAllText(Full($"{Apbs}/apbs.dll"), "dll");
        File.WriteAllText(Full($"{Apbs}/config.json"), "{}");
        File.WriteAllText(Full($"{Apbs}/Presets/realistic/manifest.json"), "{}");

        var apbs = Record(1594, false, $"{Apbs}/apbs.dll");
        var preset = Record(37, true, $"{Apbs}/Presets/realistic/manifest.json");
        _manifest.Save(new ModInstallManifest { Mods = [apbs, preset] });

        Assert.Empty(Service([]).LeftoversOf(_install, apbs, []));
    }

    private InstalledModRecord Record(int id, bool isAddon, params string[] files) => new()
    {
        ModId = id,
        IsAddon = isAddon,
        Name = $"mod {id}",
        Version = "1.0.0",
        InstalledAt = DateTimeOffset.UnixEpoch,
        IsAppManaged = true,
        Files = [.. files],
        InstallPath = InstallStamp.Of(_install),
    };
}
