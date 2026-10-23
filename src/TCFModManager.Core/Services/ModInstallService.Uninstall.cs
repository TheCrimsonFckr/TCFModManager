using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed partial class ModInstallService
{
    //
    // Removes a mod this app installed (D23): every recorded file that is still exactly what was placed
    // and that no other mod owns is MOVED into the install's holding folder, never deleted; the files it
    // replaced are put back (D22, R16); every path's outcome is logged and written to removal.json
    // (D26). <paramref name="configs"/> decides what happens to the mod's own config JSON files first.
    //
    public Task<UninstallResult> UninstallAsync(
        string installPath,
        InstalledModRecord record,
        ConfigAction configs = ConfigAction.Keep,
        CancellationToken ct = default,
        RemovalBatch? batch = null)
    {
        EnsureInstallNotInUse(ModInstallAction.Remove, installPath);
        EnsureRecordBelongsHere(record, installPath);
        RecoverInterruptedInstalls(installPath);

        // The SPT profiles as they were before the removal (OPEN-12 F4).
        profileBackups?.BackupIfChanged(installPath, ProfileBackups.BeforeRemove);

        var result = RemoveRecordedFiles(installPath, record, configs, null, null, RemovalKind.AppInstalled, ct, batch);

        // A mod that is gone has no shipped copies worth keeping. An update does not come through
        // here, which is why this is safe to do unconditionally - see InstallAsync.
        _configs.Baselines.Remove(record.ModId, record.IsAddon);

        return Task.FromResult(result);
    }

    //
    // Installs this app was stopped in the middle of, settled so every file they placed is owned by a
    // record (OPEN-12 F1, see InstallJournal). Run before every change to the install, and by the App
    // once at start. Returns the names of the mods now marked partly installed.
    //
    public IReadOnlyList<string> RecoverInterruptedInstalls(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) return [];

        try
        {
            return _journal.Recover(installPath, manifestService);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Install", $"couldn't settle interrupted installs: {ex.Message}");
            return [];
        }
    }

    //
    // A record stamped with a different install is never acted on here (D17). An unstamped one - made
    // before v1.19.0 - is: the App names the install in its confirmation before anything happens.
    //
    private static void EnsureRecordBelongsHere(InstalledModRecord record, string installPath)
    {
        if (record.InstallPath is not { } stamped) return;
        if (string.Equals(stamped, InstallStamp.Of(installPath), StringComparison.OrdinalIgnoreCase)) return;

        // The install it names is gone - moved or renamed to this one (AdoptMovedInstall, which the
        // App runs at start, re-stamps it on disk; this lets it through before the next start).
        if (!Directory.Exists(stamped) && InstallStamp.AppIsInside(installPath)) return;

        AppLog.Warn("Remove", $"{record.Name}: its record belongs to {stamped}, not {installPath}; refused");
        throw new ModInstallException(ModInstallFailure.RecordFromAnotherInstall)
        {
            ModName = record.Name,
            Folder = stamped,
        };
    }

    //
    // The removal itself, shared by Remove and the update path. For every recorded file, in order:
    // kept for the user (keep/preserve), refused by InstallPathGuard (D13), kept because another record
    // owns it (D9), already gone, kept because it changed since install (D21 - unless an update is about
    // to place over it, when it is moved and so stays recoverable), otherwise moved into holding (D23).
    //
    // <paramref name="keep"/>: configs ConfigCarryOver could not copy aside. <paramref name="incoming"/>:
    // what the update will place. Both null on a real removal.
    //
    private UninstallResult RemoveRecordedFiles(
        string installPath,
        InstalledModRecord record,
        ConfigAction configs,
        IReadOnlySet<string>? keep,
        IReadOnlySet<string>? incoming,
        RemovalKind kind,
        CancellationToken ct,
        RemovalBatch? batch = null)
    {
        var failed = new List<string>();
        var refused = new List<string>();
        var keptChanged = new List<string>();
        var keptOwned = new List<string>();
        var moved = 0;
        var restored = 0;
        var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var options = _options.Effective();

        //
        // A real removal rescues both a mod's settings and the documents the user wrote in it, and the
        // user-data half is read off disk rather than from the record: SVM's presets are written by its
        // own generator and appear in no file list, which is exactly the case worth rescuing.
        //
        var kept = new KeptConfigs(0, null);

        List<string> configFiles = configs == ConfigAction.Keep
            ?
            [
                .. ModConfigFiles.InRecord(record, options),
                .. ModConfigFiles.UserDataOnDisk(installPath, record, options),
            ]
            : [];

        if (configFiles.Count > 0)
            kept = ModConfigFiles.MoveOut(installPath, configFiles, record.Name, DateTimeOffset.UtcNow);

        // An update: the settings are already copied aside and about to be replaced, but the user's own
        // documents stay where they are.
        HashSet<string> preserve = configs == ConfigAction.Preserve
            ? ModConfigFiles.UserDataInRecord(record, options).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Every file another record lists - those stay with their other owner (D9).
        var owned = manifestService.Load().Mods
            .Where(m => !(m.ModId == record.ModId && m.IsAddon == record.IsAddon))
            .SelectMany(m => m.Files)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var session = RemovedMods.Begin(installPath, record.Name, kind, record, batch);
        session.Log.ConfigsKeptFolder = kept.Folder;

        foreach (var relative in record.Files)
        {
            ct.ThrowIfCancellationRequested();

            if ((keep is not null && keep.Contains(relative)) || preserve.Contains(relative))
            {
                session.Note(relative, RemovalOutcome.KeptForTheUser);
                continue;
            }

            // A file in a new-file-only area (R17) is taken only when its fingerprint proves it is the
            // copy this app placed; anything else there stays, as SPT's or the game's.
            if (InstallPathGuard.CheckRecordedPath(installPath, relative, out var fullPath) is { } refusal
                && !(refusal == PathRefusal.Protected && InstallPathGuard.ProvenPlacedFile(installPath, record, relative, out fullPath)))
            {
                refused.Add(relative);
                session.Note(relative, RemovalOutcome.Refused, refusal);
                continue;
            }

            if (owned.Contains(relative))
            {
                keptOwned.Add(relative);
                session.Note(relative, RemovalOutcome.KeptOwnedByAnotherMod);
                continue;
            }

            if (!File.Exists(fullPath))
            {
                session.Note(relative, RemovalOutcome.AlreadyGone);
                NoteFoldersToTidy(installPath, fullPath, touchedDirectories);
                continue;
            }

            if (record.FingerprintFor(relative) is { } print
                && !print.Matches(fullPath)
                && !(kind == RemovalKind.ReplacedByUpdate && incoming is not null && incoming.Contains(relative)))
            {
                keptChanged.Add(relative);
                session.Note(relative, RemovalOutcome.KeptChangedSinceInstall);
                continue;
            }

            try
            {
                session.MoveFileIn(fullPath, relative);
                session.Note(relative, RemovalOutcome.Moved);
                moved++;
                NoteFoldersToTidy(installPath, fullPath, touchedDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Remove", $"{record.Name}: couldn't move {relative} out: {ex.Message}");
                failed.Add(relative);
                session.Note(relative, RemovalOutcome.Failed);
            }
        }

        // On a real removal, what this mod replaced comes back. An update carries them forward instead.
        if (kind == RemovalKind.AppInstalled)
            restored = RestoreOriginals(installPath, record, session);

        // The empty folders the archive shipped go too, while still empty and no other record lists
        // them. An update's own install creates them again.
        var otherEmptyFolders = manifestService.Load().Mods
            .Where(m => !(m.ModId == record.ModId && m.IsAddon == record.IsAddon))
            .SelectMany(m => m.EmptyFolders)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in record.EmptyFolders.Where(f => !otherEmptyFolders.Contains(f)))
        {
            if (InstallPathGuard.CheckRecordedPath(installPath, folder, out var fullFolder) is null)
                NoteFoldersToTidy(installPath, Path.Combine(fullFolder, "_"), touchedDirectories);
        }

        TidyEmptyFolders(installPath, touchedDirectories);

        // On a real removal, the mod's folders that had to stay because they hold files it didn't
        // install - said in the result and marked on the leftover's card, never taken.
        var foldersLeft = kind == RemovalKind.ReplacedByUpdate ? [] : FoldersLeftBehind(installPath, record);
        session.Log.FoldersLeft = [.. foldersLeft.Select(f => f.Folder)];

        var manifest = manifestService.Load();
        manifest.Mods.RemoveAll(m => m.ModId == record.ModId && m.IsAddon == record.IsAddon);
        manifestService.Save(manifest);

        var holding = _removed.Finish(session);

        return new UninstallResult(moved, failed, kept.Count, kept.Folder, refused)
        {
            KeptChanged = keptChanged,
            KeptOwned = keptOwned,
            OriginalsRestored = restored,
            HoldingFolder = holding,
            FoldersLeft = foldersLeft,
        };
    }

    //
    // Puts back what this mod replaced (D22): every original except an earlier copy of the same mod
    // proven by its GUID (R16), and only where the path is free and passes the path checks. Every kept
    // copy then moves out of Data into the holding folder, so Undo has it and retention clears it.
    //
    private static int RestoreOriginals(string installPath, InstalledModRecord record, RemovalSession session)
    {
        var restored = 0;

        foreach (var original in record.Overwrote)
        {
            var backup = Path.Combine(AppPaths.DataDirectory, original.BackupPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(backup))
            {
                session.Note(original.Path, RemovalOutcome.OriginalMissing);
                continue;
            }

            try
            {
                if (original.SameMod)
                {
                    session.Note(original.Path, RemovalOutcome.OriginalHeldSameMod);
                }
                else if (InstallPathGuard.CheckRecordedPath(installPath, original.Path, out var target) is { } refusal)
                {
                    session.Note(original.Path, RemovalOutcome.Refused, refusal);
                }
                else if (File.Exists(target))
                {
                    session.Note(original.Path, RemovalOutcome.OriginalNotRestoredOccupied);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(backup, target, overwrite: false);

                    if (!new FileFingerprint(original.Path, original.Size, original.Sha256).Matches(target))
                        AppLog.Warn("Remove", $"{record.Name}: put back {original.Path}, but its kept copy no longer matches what was kept");

                    session.Note(original.Path, RemovalOutcome.OriginalRestored);
                    restored++;
                }

                session.MoveOriginalIn(original.BackupPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Remove", $"{record.Name}: couldn't put back {original.Path}: {ex.Message}");
                session.Note(original.Path, RemovalOutcome.Failed);
            }
        }

        TidyEmptyOriginalsFolders(record);
        return restored;
    }

    // Removes the now-empty Data\overwritten\<mod>\<time>\... folders - only empty ones, only in there.
    private static void TidyEmptyOriginalsFolders(InstalledModRecord record)
    {
        var key = record.IsAddon ? $"{record.ModId}-addon" : record.ModId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var root = Path.Combine(AppPaths.DataDirectory, OverwrittenDirectoryName, key);
        if (!Directory.Exists(root)) return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(root))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TidyEmptyFolders(string installPath, HashSet<string> touchedDirectories)
    {
        foreach (var dir in touchedDirectories.OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir)
                    && InstallPathGuard.MayRemoveEmptyFolder(installPath, dir)
                    && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Directory that won't delete is left alone.
            }
        }
    }

    private static void NoteFoldersToTidy(string installPath, string fullPath, HashSet<string> touched)
    {
        // Only a mod's own folder and what's below it are ever tidied away (D14).
        for (var dir = Path.GetDirectoryName(fullPath);
             dir is not null && InstallPathGuard.MayRemoveEmptyFolder(installPath, dir);
             dir = Path.GetDirectoryName(dir))
        {
            touched.Add(dir);
        }
    }

    //
    // Removes a mod installed by hand (D15, D23): every folder (or loose DLL) is checked first, and only
    // when all pass are they moved - whole - into the holding folder. <paramref name="configsFolder"/> is
    // where KeepLegacyConfigs put the mod's configs, if it did, so Undo can bring them back too.
    //
    public string? RemoveHandInstalled(
        IReadOnlyList<string> paths, string installPath, string modName, string? configsFolder = null, RemovalBatch? batch = null)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            throw new ModInstallException(ModInstallFailure.NoInstallFolder);

        EnsureInstallNotInUse(ModInstallAction.Remove, installPath);
        RecoverInterruptedInstalls(installPath);

        foreach (var path in paths)
        {
            if (InstallPathGuard.CheckModFolder(installPath, path) is { } refusal)
            {
                AppLog.Warn("Remove", $"refused to remove {path} ({refusal})");
                throw new ModInstallException(ModInstallFailure.RemovalRefused) { Folder = path, Refusal = refusal };
            }
        }

        profileBackups?.BackupIfChanged(installPath, ProfileBackups.BeforeRemove);

        var session = RemovedMods.Begin(installPath, modName, RemovalKind.HandInstalled, record: null, batch);
        session.Log.ConfigsKeptFolder = configsFolder;

        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            var relative = Path.GetRelativePath(Path.GetFullPath(installPath), full).Replace('\\', '/');

            if (Directory.Exists(full)) session.MoveFolderIn(full, relative);
            else if (File.Exists(full)) session.MoveFileIn(full, relative);
            else
            {
                session.Note(relative, RemovalOutcome.AlreadyGone);
                continue;
            }

            session.Note(relative, RemovalOutcome.Moved);
        }

        return _removed.Finish(session);
    }

    //
    // Removes one mod installed by hand from a folder it shares with others (OPEN-28 R6): only the
    // files the share owns move to the holding folder, the folder and every other mod's files stay.
    // A whole-folder entry goes through RemoveHandInstalled as before.
    //
    public string? RemoveHandInstalledShare(
        InstalledMod share, string installPath, string modName, string? configsFolder = null, RemovalBatch? batch = null)
    {
        if (share.OwnedFiles is not { } owned)
            return RemoveHandInstalled([share.FolderPath], installPath, modName, configsFolder, batch);

        if (string.IsNullOrWhiteSpace(installPath))
            throw new ModInstallException(ModInstallFailure.NoInstallFolder);

        EnsureInstallNotInUse(ModInstallAction.Remove, installPath);
        RecoverInterruptedInstalls(installPath);

        if (InstallPathGuard.CheckModFolder(installPath, share.FolderPath) is { } folderRefusal)
        {
            AppLog.Warn("Remove", $"refused to remove {share.FolderPath} ({folderRefusal})");
            throw new ModInstallException(ModInstallFailure.RemovalRefused) { Folder = share.FolderPath, Refusal = folderRefusal };
        }

        var root = Path.GetFullPath(installPath);
        var folder = Path.GetFullPath(share.FolderPath);
        var files = new List<(string Full, string Relative)>();

        foreach (var file in owned)
        {
            var full = Path.GetFullPath(Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar)));
            var relative = Path.GetRelativePath(root, full).Replace('\\', '/');

            var refusal = full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? InstallPathGuard.CheckRecordedPath(installPath, relative, out _)
                : PathRefusal.NotAModFolder;

            if (refusal is not null)
            {
                AppLog.Warn("Remove", $"refused to remove {full} ({refusal})");
                throw new ModInstallException(ModInstallFailure.RemovalRefused) { Folder = full, Refusal = refusal };
            }

            files.Add((full, relative));
        }

        profileBackups?.BackupIfChanged(installPath, ProfileBackups.BeforeRemove);

        var session = RemovedMods.Begin(installPath, modName, RemovalKind.HandInstalled, record: null, batch);
        session.Log.ConfigsKeptFolder = configsFolder;
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (full, relative) in files)
        {
            if (!File.Exists(full))
            {
                session.Note(relative, RemovalOutcome.AlreadyGone);
                continue;
            }

            session.MoveFileIn(full, relative);
            session.Note(relative, RemovalOutcome.Moved);
            NoteFoldersToTidy(installPath, full, touched);
        }

        TidyEmptyFolders(installPath, touched);
        return _removed.Finish(session);
    }
}
