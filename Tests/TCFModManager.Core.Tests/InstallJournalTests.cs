using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-12 F1: an install the app was stopped in the middle of. A process that ends can't be staged in
// a test, so each one sets up what it leaves behind - the note, the manifest, the files on disk - and
// asserts on what recovery makes of it. Recovery rolls forward: never a file deleted or put back, only
// records made or marked Incomplete.
//
public class InstallJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-journal-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;
    private readonly InstallJournal _journal;

    private static int _nextId = 980_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    public InstallJournalTests()
    {
        _install = Path.Combine(_root, "install");
        Write("EscapeFromTarkov.exe", "game");
        Write("SPT/SPT.Server.exe", "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
        _journal = InstallJournal.Beside(_manifest);
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
                try
                {
                    if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true);
                }
                catch (IOException) { }
            }
        }
    }

    private string Full(string relative) => Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relative, string content)
    {
        var full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private int NewId()
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        return id;
    }

    private sealed class ArchiveHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private ModInstallService Service(params (string Path, string Content)[] files) =>
        new(new ModDownloadService(new HttpClient(new ArchiveHandler(ArchiveFileTests.Zip(files)))), _manifest);

    private InstallJournalEntry Note(int id, string version, DateTimeOffset startedAt, params string[] planned) => new()
    {
        InstallPath = InstallStamp.Of(_install),
        ModId = id,
        Name = $"Test {id}",
        VersionId = 2,
        Version = version,
        StartedAt = startedAt,
        Planned = [.. planned],
    };

    private void SaveRecord(InstalledModRecord record)
    {
        var manifest = _manifest.Load();
        manifest.Mods.Add(record);
        _manifest.Save(manifest);
    }

    private InstalledModRecord Record(int id, string version, DateTimeOffset installedAt, params string[] files) => new()
    {
        ModId = id,
        Name = $"Test {id}",
        VersionId = 1,
        Version = version,
        InstalledAt = installedAt,
        Files = [.. files],
        Folders = InstalledModFolders.FromPlacedFiles(files),
        InstallPath = InstallStamp.Of(_install),
    };

    private string[] NotesLeft() =>
        Directory.Exists(_journal.Folder) ? Directory.GetFiles(_journal.Folder, "*.json") : [];

    [Fact]
    public async Task A_finished_install_leaves_no_note()
    {
        var id = NewId();
        var target = new InstallTarget(id, false, $"Test {id}", null, null, null);

        await Service(("BepInEx/plugins/Mod/mod.dll", "v1"))
            .InstallAsync(target, new ModVersion { Id = 1, Version = "1.0.0", Link = "https://example.test/a" }, _install);

        Assert.Empty(NotesLeft());
        Assert.False(_manifest.Load().Find(id, false)!.Incomplete);
    }

    [Fact]
    public void Stopped_after_the_previous_record_went_records_what_is_on_disk_as_partly_installed()
    {
        var id = NewId();
        var started = DateTimeOffset.UtcNow.AddMinutes(-5);
        Write("BepInEx/plugins/Mod/a.dll", "new a");

        var note = Note(id, "2.0.0", started, "BepInEx/plugins/Mod/a.dll", "BepInEx/plugins/Mod/b.dll");
        note.Overwrote = [new OverwrittenFile("BepInEx/plugins/Mod/a.dll", 5, "abc", "kept/a.dll")];
        _journal.Write(note);
        _journal.Release(note);

        var recovered = _journal.Recover(_install, _manifest);

        Assert.Equal([$"Test {id}"], recovered);
        var record = _manifest.Load().Find(id, false)!;
        Assert.True(record.Incomplete);
        Assert.Equal("2.0.0", record.Version);
        Assert.Equal(["BepInEx/plugins/Mod/a.dll"], record.Files);
        Assert.Equal(["Mod"], record.Folders);
        Assert.Single(record.Overwrote);
        Assert.Empty(record.Fingerprints);
        Assert.Equal(InstallStamp.Of(_install), record.InstallPath);
        Assert.Empty(NotesLeft());
    }

    [Fact]
    public void Stopped_while_the_previous_version_was_being_removed_marks_it_partly_installed()
    {
        var id = NewId();
        var installed = DateTimeOffset.UtcNow.AddDays(-3);
        Write("BepInEx/plugins/Mod/a.dll", "old a");
        SaveRecord(Record(id, "1.0.0", installed, "BepInEx/plugins/Mod/a.dll", "BepInEx/plugins/Mod/gone.dll"));

        var note = Note(id, "2.0.0", DateTimeOffset.UtcNow.AddMinutes(-1), "BepInEx/plugins/Mod/a.dll");
        _journal.Write(note);
        _journal.Release(note);

        var recovered = _journal.Recover(_install, _manifest);

        Assert.Equal([$"Test {id}"], recovered);
        var record = _manifest.Load().Find(id, false)!;
        Assert.True(record.Incomplete);
        Assert.Equal("1.0.0", record.Version);
        Assert.Equal(installed, record.InstalledAt);
        Assert.Equal(2, record.Files.Count);
        Assert.Empty(NotesLeft());
    }

    [Fact]
    public void Stopped_before_anything_changed_leaves_the_previous_record_as_it_was()
    {
        var id = NewId();
        Write("BepInEx/plugins/Mod/a.dll", "old a");
        SaveRecord(Record(id, "1.0.0", DateTimeOffset.UtcNow.AddDays(-3), "BepInEx/plugins/Mod/a.dll"));

        var note = Note(id, "2.0.0", DateTimeOffset.UtcNow.AddMinutes(-1), "BepInEx/plugins/Mod/a.dll");
        _journal.Write(note);
        _journal.Release(note);

        Assert.Empty(_journal.Recover(_install, _manifest));
        Assert.False(_manifest.Load().Find(id, false)!.Incomplete);
        Assert.Empty(NotesLeft());
    }

    [Fact]
    public void A_record_saved_since_the_install_started_only_loses_the_note()
    {
        var id = NewId();
        var started = DateTimeOffset.UtcNow.AddMinutes(-2);
        SaveRecord(Record(id, "2.0.0", started.AddSeconds(30), "BepInEx/plugins/Mod/missing.dll"));

        var note = Note(id, "2.0.0", started, "BepInEx/plugins/Mod/missing.dll");
        _journal.Write(note);
        _journal.Release(note);

        Assert.Empty(_journal.Recover(_install, _manifest));
        Assert.False(_manifest.Load().Find(id, false)!.Incomplete);
        Assert.Empty(NotesLeft());
    }

    [Fact]
    public void A_note_for_another_install_is_left_alone()
    {
        var id = NewId();
        var note = Note(id, "1.0.0", DateTimeOffset.UtcNow);
        note.InstallPath = InstallStamp.Of(Path.Combine(_root, "elsewhere"));
        _journal.Write(note);
        _journal.Release(note);

        Assert.Empty(_journal.Recover(_install, _manifest));
        Assert.Null(_manifest.Load().Find(id, false));
        Assert.Single(NotesLeft());
    }

    [Fact]
    public void An_install_still_running_in_this_process_is_not_taken_for_an_interrupted_one()
    {
        var id = NewId();
        var note = Note(id, "1.0.0", DateTimeOffset.UtcNow, "BepInEx/plugins/Mod/a.dll");
        _journal.Write(note);

        Assert.Empty(_journal.Recover(_install, _manifest));
        Assert.Single(NotesLeft());

        _journal.Release(note);

        Assert.Equal([$"Test {id}"], _journal.Recover(_install, _manifest));
    }

    [Fact]
    public void A_note_that_cannot_be_read_is_skipped()
    {
        Directory.CreateDirectory(_journal.Folder);
        File.WriteAllText(Path.Combine(_journal.Folder, "broken.json"), "{ not json");

        Assert.Empty(_journal.Recover(_install, _manifest));
    }

    [Fact]
    public async Task The_next_install_settles_an_interrupted_one_first()
    {
        var stopped = NewId();
        Write("BepInEx/plugins/Stopped/s.dll", "placed before the app stopped");
        var note = Note(stopped, "3.0.0", DateTimeOffset.UtcNow.AddMinutes(-10), "BepInEx/plugins/Stopped/s.dll");
        _journal.Write(note);
        _journal.Release(note);

        var id = NewId();
        await Service(("BepInEx/plugins/Other/o.dll", "other"))
            .InstallAsync(new InstallTarget(id, false, $"Test {id}", null, null, null),
                new ModVersion { Id = 1, Version = "1.0.0", Link = "https://example.test/a" }, _install);

        var record = _manifest.Load().Find(stopped, false)!;
        Assert.True(record.Incomplete);
        Assert.Equal(["BepInEx/plugins/Stopped/s.dll"], record.Files);
        Assert.Empty(NotesLeft());
    }

    [Fact]
    public void Recovery_settles_a_note_once()
    {
        var stopped = NewId();
        Write("BepInEx/plugins/Stopped/s.dll", "placed before the app stopped");
        var note = Note(stopped, "3.0.0", DateTimeOffset.UtcNow.AddMinutes(-10), "BepInEx/plugins/Stopped/s.dll");
        _journal.Write(note);
        _journal.Release(note);

        Assert.Equal([$"Test {stopped}"], Service().RecoverInterruptedInstalls(_install));
        Assert.Empty(Service().RecoverInterruptedInstalls(_install));
    }
}
