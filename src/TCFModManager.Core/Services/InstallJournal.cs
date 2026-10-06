using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// OPEN-12 F1: a write-ahead note of an install in progress, so an install the app was stopped in the
// middle of (killed, crashed, the power cut) still leaves every file it placed owned by a record.
//
// InstallAsync already handles its own failures: an IOException part way through saves an Incomplete
// record of what was placed. What it cannot handle is the process ending. An update removes the
// previous version's record before placing the new files, and the new record is only saved once they
// are all placed - stopped in between, the new files are on disk with nothing tracking them, and the
// card shows the mod as hand-installed.
//
// The note is written once, after the originals are kept and before anything in the install is
// removed or placed, and deleted once a record for the install is saved. It lives beside the manifest
// (Data\install-journal\), not in the work folder, which can fall back to %TEMP% and is swept after
// six hours.
//
// Recovery rolls forward, never back (Chris, 2026-10-06) - the same outcome as a failure InstallAsync
// catches itself:
//
//   - a record saved since the install started: it got that far, so only the note is left over;
//   - the previous version's record still there: nothing was placed yet (placing starts after the
//     removal saves the manifest), but the removal may have moved some of its files out - marked
//     Incomplete when any are missing;
//   - no record at all: a new Incomplete record of every planned file now on disk, with the originals
//     the install had already kept.
//
// The card then reads "Only partly installed", and the usual reinstall puts it right. No fingerprints
// are taken for a recovered record - a file the copy was cut off in the middle of must not read as
// exactly what was placed - so a removal falls back to the path checks for it.
//
// Run before every install, removal and undo, and once at start.
//
public sealed class InstallJournal
{
    public const string FolderName = "install-journal";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public InstallJournal(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    // Notes of installs still running in this process - never mistaken for interrupted ones by a
    // removal that runs alongside the download queue.
    private readonly ConcurrentDictionary<string, byte> _running = new();

    public static InstallJournal Beside(ModInstallManifestService manifest) =>
        new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest.FilePath))!, FolderName));

    // Never a reason for an install not to go ahead: without the note it is exactly as safe as before.
    public void Write(InstallJournalEntry entry)
    {
        _running[entry.Id] = 0;

        try
        {
            SafeFile.WriteText(PathOf(entry.Id), JsonSerializer.Serialize(entry, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Install", $"couldn't write the install journal for {entry.Name} {entry.Version}: {ex.Message}");
        }
    }

    // The install has ended, however it ended: a note it left is now recovery's to settle.
    public void Release(InstallJournalEntry entry) => _running.TryRemove(entry.Id, out _);

    public void Delete(InstallJournalEntry entry)
    {
        Release(entry);

        try
        {
            var path = PathOf(entry.Id);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Install", $"couldn't remove the install journal for {entry.Name} {entry.Version}: {ex.Message}");
        }
    }

    // The notes left for this install, oldest first. One that can't be read is logged and skipped.
    public List<InstallJournalEntry> PendingFor(string installPath)
    {
        var stamp = InstallStamp.Of(installPath);
        var found = new List<InstallJournalEntry>();

        if (!Directory.Exists(Folder)) return found;

        IEnumerable<string> files;
        try
        {
            files = Directory.GetFiles(Folder, "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Install", $"couldn't read the install journal folder: {ex.Message}");
            return found;
        }

        foreach (var file in files)
        {
            try
            {
                if (JsonSerializer.Deserialize<InstallJournalEntry>(File.ReadAllText(file), Options) is { } entry
                    && !_running.ContainsKey(entry.Id)
                    && string.Equals(entry.InstallPath, stamp, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(entry);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Install", $"install journal {Path.GetFileName(file)} could not be read: {ex.Message}");
            }
        }

        return [.. found.OrderBy(e => e.StartedAt)];
    }

    //
    // Settles every note left for this install against the manifest. Returns the names of the mods
    // whose records were made or marked Incomplete; an empty list when there was nothing to put right.
    //
    public List<string> Recover(string installPath, ModInstallManifestService manifestService)
    {
        var recovered = new List<string>();

        foreach (var entry in PendingFor(installPath))
        {
            var manifest = manifestService.Load();
            var record = manifest.Find(entry.ModId, entry.IsAddon);

            if (record is not null && record.InstalledAt >= entry.StartedAt)
            {
                // This install's record (or a later one) was saved; the note outlived it.
                Delete(entry);
                continue;
            }

            if (record is not null)
            {
                var missing = record.Files.Where(f => !File.Exists(Resolve(installPath, f))).ToList();
                if (missing.Count > 0 && !record.Incomplete)
                {
                    manifest.Mods.Remove(record);
                    manifest.Mods.Add(MarkedIncomplete(record));
                    manifestService.Save(manifest);
                    recovered.Add(record.Name);

                    AppLog.Warn("Install",
                        $"{entry.Name}: the update to {entry.Version} was stopped while {record.Version} was being removed; " +
                        $"{missing.Count} of its file(s) are gone, marked partly installed");
                }
                else
                {
                    AppLog.Info("Install", $"{entry.Name}: the update to {entry.Version} was stopped before it changed anything");
                }

                Delete(entry);
                continue;
            }

            var onDisk = entry.Planned.Where(f => File.Exists(Resolve(installPath, f))).ToList();

            var made = new InstalledModRecord
            {
                ModId = entry.ModId,
                IsAddon = entry.IsAddon,
                Guid = entry.Guid,
                Name = entry.Name,
                VersionId = entry.VersionId,
                Version = entry.Version,
                InstalledAt = entry.StartedAt,
                Files = onDisk,
                Folders = InstalledModFolders.FromPlacedFiles(onDisk),
                Incomplete = true,
                InstallPath = entry.InstallPath,
                Overwrote = entry.Overwrote,
            };

            manifest.Mods.RemoveAll(m => m.ModId == entry.ModId && m.IsAddon == entry.IsAddon);
            manifest.Mods.Add(made);
            manifestService.Save(manifest);
            recovered.Add(entry.Name);

            AppLog.Warn("Install",
                $"{entry.Name} {entry.Version}: the install was stopped part way; recorded {onDisk.Count}/{entry.Planned.Count} file(s) " +
                $"as partly installed" + (entry.ConfigsFolder is { } configs ? $"; configs set aside in {configs}" : ""));

            Delete(entry);
        }

        return recovered;
    }

    private string PathOf(string id) => Path.Combine(Folder, id + ".json");

    private static string Resolve(string installPath, string relative) =>
        Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar));

    // The same record with Incomplete set - through JSON so no field is left behind as the record grows.
    private static InstalledModRecord MarkedIncomplete(InstalledModRecord record)
    {
        var node = JsonSerializer.SerializeToNode(record)!.AsObject();
        node[nameof(InstalledModRecord.Incomplete)] = true;
        return node.Deserialize<InstalledModRecord>()!;
    }
}

// One install in progress - see InstallJournal.
public sealed class InstallJournalEntry
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");

    // InstallStamp.Of the install it is changing.
    public string InstallPath { get; set; } = string.Empty;

    public int ModId { get; set; }

    public bool IsAddon { get; set; }

    public string? Guid { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? VersionId { get; set; }

    public string Version { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    // Every install-relative file (forward slashes) the install is about to place or keep.
    public List<string> Planned { get; set; } = [];

    // Files no record owned that the install replaces, already copied into Data (D22) - owed back.
    public List<OverwrittenFile> Overwrote { get; set; } = [];

    // Where the update set the mod's configs aside, if it did - named in the log on recovery.
    public string? ConfigsFolder { get; set; }
}
