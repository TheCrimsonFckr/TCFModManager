using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed partial class ModInstallService
{
    //
    // Puts a removal back (D28): the original files this mod had replaced go back to Data, the mod's
    // files and folders come back to the install, its kept configs come back, and its record is
    // restored. A path something else now occupies is never overwritten - it is reported and its copy
    // stays in the holding folder, which is then kept rather than deleted.
    //
    //
    // Undo for a removal made as part of a batch (OPEN-31 R2): puts back folder's removal and every
    // other removal in its batch, the lead first. A removal outside any batch is undone on its own.
    //
    public UndoResult UndoRemovalBatch(string installPath, string folder)
    {
        var batch = RemovedMods.BatchOf(installPath, folder);
        if (batch.Count == 0) return UndoRemoval(installPath, folder);

        var ran = false;
        var putBack = 0;
        var blocked = new List<string>();

        foreach (var (held, _) in batch)
        {
            var result = UndoRemoval(installPath, held);
            ran |= result.Ran;
            putBack += result.PutBack;
            blocked.AddRange(result.Blocked);
        }

        return new UndoResult(ran, putBack, blocked);
    }

    public UndoResult UndoRemoval(string installPath, string folder)
    {
        EnsureInstallNotInUse(ModInstallAction.Undo, installPath);
        RecoverInterruptedInstalls(installPath);

        var log = RemovedMods.ReadLog(folder);
        if (log is null || log.Undone || log.Kind == RemovalKind.ReplacedByUpdate
            || !string.Equals(log.InstallPath, InstallStamp.Of(installPath), StringComparison.OrdinalIgnoreCase))
        {
            return new UndoResult(false, 0, []);
        }

        var blocked = new List<string>();
        var back = 0;

        // 1. Originals this removal put back leave the install again - into the holding folder, never deleted.
        if (log.Record is { } removedRecord)
        {
            foreach (var entry in log.Entries.Where(e => e.Outcome == RemovalOutcome.OriginalRestored))
            {
                var original = removedRecord.Overwrote.FirstOrDefault(o => string.Equals(o.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
                if (original is null) continue;

                if (InstallPathGuard.CheckRecordedPath(installPath, entry.Path, out var target) is null
                    && File.Exists(target)
                    && new FileFingerprint(original.Path, original.Size, original.Sha256).Matches(target))
                {
                    var aside = Path.Combine(folder, "undo-displaced", entry.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
                    File.Move(target, aside, overwrite: false);
                }
            }
        }

        // 2. The mod's files and folders.
        foreach (var entry in log.Entries.Where(e => e.Outcome == RemovalOutcome.Moved))
        {
            var held = Path.Combine(folder, RemovedMods.FilesFolder, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            var target = Path.Combine(installPath, entry.Path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(held) && !Directory.Exists(held)) { blocked.Add(entry.Path); continue; }

            // A held file may go back into a new-file-only area (R17): it only left it because its
            // fingerprint proved it this app's, and it only goes back to a free path.
            var check = File.Exists(held)
                ? InstallPathGuard.CheckPlacedPath(installPath, entry.Path, out _)
                : InstallPathGuard.CheckRecordedPath(installPath, entry.Path, out _);

            //
            // A held folder whose place has since been taken by a folder of the same name - the mod
            // put back first, then a folder of leftovers removed after it - is merged file by file,
            // each file only into a free path. Anything occupied stays held and is reported.
            //
            if (check is null && Directory.Exists(held) && Directory.Exists(target) && !InstallPathGuard.IsLink(target))
            {
                var merged = MergeHeldFolder(installPath, held, entry.Path, blocked);
                if (merged > 0) back++;
                continue;
            }

            if (check is not null || File.Exists(target) || Directory.Exists(target))
            {
                blocked.Add(entry.Path);
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (Directory.Exists(held)) Directory.Move(held, target);
                else File.Move(held, target, overwrite: false);
                back++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Undo", $"couldn't put back {entry.Path}: {ex.Message}");
                blocked.Add(entry.Path);
            }
        }

        // 3. The copies of files it had replaced, back into Data where the record expects them.
        var originals = Path.Combine(folder, RemovedMods.OriginalsFolder);
        if (Directory.Exists(originals))
        {
            foreach (var file in Directory.EnumerateFiles(originals, "*", SearchOption.AllDirectories).ToList())
            {
                var dataRelative = Path.GetRelativePath(originals, file);
                var destination = Path.Combine(AppPaths.DataDirectory, dataRelative);
                if (File.Exists(destination)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(file, destination, overwrite: false);
            }
        }

        // 4. Configs the removal moved out to Data\LegacyConfigs.
        if (log.ConfigsKeptFolder is { } configsFolder && Directory.Exists(configsFolder))
        {
            foreach (var file in Directory.EnumerateFiles(configsFolder, "*", SearchOption.AllDirectories).ToList())
            {
                var relative = Path.GetRelativePath(configsFolder, file).Replace('\\', '/');

                if (InstallPathGuard.CheckRecordedPath(installPath, relative, out var target) is not null || File.Exists(target))
                {
                    blocked.Add(relative);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target, overwrite: false);
            }

            DeleteEmptyConfigFolders(configsFolder);
        }

        // 5. The record.
        if (log.Record is { } record)
        {
            var manifest = manifestService.Load();
            manifest.Mods.RemoveAll(m => m.ModId == record.ModId && m.IsAddon == record.IsAddon);
            manifest.Mods.Add(record);
            manifestService.Save(manifest);
        }

        log.Undone = true;
        SafeFile.WriteText(Path.Combine(folder, RemovedMods.LogName), System.Text.Json.JsonSerializer.Serialize(log));

        AppLog.Info("Undo", $"{log.ModName}: put back {back} item(s){(blocked.Count > 0 ? $"; {blocked.Count} path(s) were occupied and left in {folder}" : "")}");

        if (blocked.Count == 0) RemovedMods.DeleteHeld(installPath, folder);

        return new UndoResult(true, back, blocked);
    }

    // Moves each file of a held folder back to its place under an existing folder, only where that
    // place is free and passes the guard. Returns how many came back; the rest go in blocked.
    private static int MergeHeldFolder(string installPath, string held, string relativeFolder, List<string> blocked)
    {
        var moved = 0;
        var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };

        foreach (var file in Directory.EnumerateFiles(held, "*", walk).ToList())
        {
            var relative = relativeFolder + "/" + Path.GetRelativePath(held, file).Replace('\\', '/');

            if (InstallPathGuard.CheckRecordedPath(installPath, relative, out var target) is not null
                || File.Exists(target) || Directory.Exists(target))
            {
                blocked.Add(relative);
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target, overwrite: false);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Undo", $"couldn't put back {relative}: {ex.Message}");
                blocked.Add(relative);
            }
        }

        return moved;
    }

    //
    // Each of the record's mod folders still on disk after its removal, with how many files in it the
    // record doesn't list. Files the record does list and that stayed (changed, another mod's) are
    // reported on their own, so a folder left only for those isn't counted here. Read-only; links are
    // not followed.
    //
    private static List<FolderLeft> FoldersLeftBehind(string installPath, InstalledModRecord record)
    {
        var recorded = record.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var left = new List<FolderLeft>();
        var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };

        foreach (var folder in record.Files.Select(InstallPathGuard.ModFolderOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (InstallPathGuard.CheckRecordedPath(installPath, folder, out var full) is not null || !Directory.Exists(full))
                continue;

            try
            {
                var foreign = Directory.EnumerateFiles(full, "*", walk)
                    .Count(f => !recorded.Contains(Path.GetRelativePath(installPath, f).Replace('\\', '/')));

                if (foreign > 0) left.Add(new FolderLeft(folder, foreign));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only a report - a folder that can't be read is simply not mentioned.
            }
        }

        return left;
    }

    //
    // Removes the folders an Undo emptied under Data\LegacyConfigs - the timestamped folder and the
    // install-shaped path inside it. Empty folders only, never a link, never outside LegacyConfigs.
    //
    private static void DeleteEmptyConfigFolders(string configsFolder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppPaths.LegacyConfigsDirectory)) + Path.DirectorySeparatorChar;
        var top = Path.GetFullPath(configsFolder);
        if (!top.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(top)) return;

        try
        {
            var folders = Directory.EnumerateDirectories(top, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                })
                .Append(top)
                .OrderByDescending(d => d.Length)
                .ToList();

            foreach (var dir in folders)
            {
                if (InstallPathGuard.IsLink(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) continue;
                Directory.Delete(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tidying only: an empty folder that won't go is left.
        }
    }

    // The config files a hand-installed mod keeps in its own folder, as install-relative paths.
    // Read off disk rather than from a record, since this path has none.
    public static List<string> FindLegacyConfigs(string installPath, IEnumerable<string> modFolderPaths) =>
        modFolderPaths.SelectMany(folder => ModConfigFiles.InFolder(installPath, folder)).Distinct().ToList();

    // Moves a hand-installed mod's config files out of the install before its folder is deleted.
    public static KeptConfigs KeepLegacyConfigs(string installPath, IEnumerable<string> relativeFiles, string modName) =>
        ModConfigFiles.MoveOut(installPath, relativeFiles, modName, DateTimeOffset.UtcNow);
}
