using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed partial class ModInstallService
{
    //
    // Goes back to the version an update replaced, from the files the update's removal half held
    // (RemovedMods.LatestReplaced). The current version leaves the way an update's old one does, held
    // as ReplacedByUpdate, so going back can itself be gone back on. The record that returns keeps the
    // current one's list of the originals it replaced, so a later removal still puts them back. A path
    // something else now occupies is never overwritten: it is reported and stays held.
    //
    public UndoResult RevertUpdate(string installPath, InstalledModRecord current, string folder)
    {
        EnsureInstallNotInUse(ModInstallAction.Undo, installPath);
        EnsureRecordBelongsHere(current, installPath);
        RecoverInterruptedInstalls(installPath);

        var log = RemovedMods.ReadLog(folder);
        if (log is not { Kind: RemovalKind.ReplacedByUpdate, Undone: false, Record: { } old }
            || old.ModId != current.ModId || old.IsAddon != current.IsAddon
            || !string.Equals(log.InstallPath, InstallStamp.Of(installPath), StringComparison.OrdinalIgnoreCase))
        {
            return new UndoResult(false, 0, []);
        }

        profileBackups?.BackupIfChanged(installPath, ProfileBackups.BeforeInstall);

        //
        // Settings changed since the update stay as they are; everything else about to come back is
        // "incoming", as an update's new files are: the current copies move out even if changed, and
        // stay recoverable.
        //
        var options = _options.Effective();
        var moved = log.Entries.Where(e => e.Outcome == RemovalOutcome.Moved).Select(e => e.Path).ToList();
        var tuned = moved
            .Where(p => ModConfigFiles.IsBepInExConfig(p) || ModConfigFiles.IsServerModConfig(p, ModConfigFiles.OptionsFor(p, options)))
            .Where(p =>
            {
                var full = Path.Combine(installPath, p.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(full) && current.FingerprintFor(p) is { } print && !print.Matches(full);
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incoming = moved.Where(p => !tuned.Contains(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        RemoveRecordedFiles(installPath, current, ConfigAction.Preserve, tuned, incoming, RemovalKind.ReplacedByUpdate, CancellationToken.None);

        //
        // What the newer version replaced: its entry wins over the older record's for the same path,
        // and a file of another mod only the newer version replaced goes back now.
        //
        var oldPaths = old.Files.Concat(old.Overwrote.Select(o => o.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = new List<OverwrittenFile>();
        foreach (var original in current.Overwrote)
        {
            if (!original.SameMod && !oldPaths.Contains(original.Path) && PutOriginalBack(installPath, original))
                continue;
            merged.Add(original);
        }
        merged.AddRange(old.Overwrote.Where(o => !current.Overwrote.Any(c => string.Equals(c.Path, o.Path, StringComparison.OrdinalIgnoreCase))));
        old.Overwrote.Clear();
        old.Overwrote.AddRange(merged);

        var manifest = manifestService.Load();
        manifest.Mods.RemoveAll(m => m.ModId == old.ModId && m.IsAddon == old.IsAddon);
        manifest.Mods.Add(old);
        manifestService.Save(manifest);

        var blocked = new List<string>();
        var back = PutBackMoved(installPath, folder, log, blocked);
        blocked.RemoveAll(tuned.Contains);

        log.Undone = true;
        SafeFile.WriteText(Path.Combine(folder, RemovedMods.LogName), System.Text.Json.JsonSerializer.Serialize(log));

        AppLog.Info("Undo", $"{old.Name}: back to {old.Version} from {current.Version}, {back} item(s){(blocked.Count > 0 ? $"; {blocked.Count} path(s) were occupied and left in {folder}" : "")}");

        if (blocked.Count == 0) RemovedMods.DeleteHeld(installPath, folder);

        return new UndoResult(true, back, blocked);
    }

    // A file one version replaced, copied back from Data when its path is free and passes the guard.
    // The kept copy stays where it is: going forward again needs it.
    private static bool PutOriginalBack(string installPath, OverwrittenFile original)
    {
        var backup = Path.Combine(AppPaths.DataDirectory, original.BackupPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(backup)
            || InstallPathGuard.CheckRecordedPath(installPath, original.Path, out var target) is not null
            || File.Exists(target) || Directory.Exists(target))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(backup, target, overwrite: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Undo", $"couldn't put back the original {original.Path}: {ex.Message}");
            return false;
        }
    }
}
