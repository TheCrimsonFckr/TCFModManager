namespace TCFModManager.Core.Services;

//
// OPEN-12 F7: where the download queue keeps an archive between downloading it and installing it.
//
// The install's own hidden scratch folder (.tcfmm-work), as loose files beside the per-install work
// folders, so an archive sits on the same drive as the install it is for - and so the sweep of stale
// work FOLDERS never takes one that is still waiting its turn. %TEMP% when that folder can't be made.
//
// Each archive is deleted once it is installed or given up on. Ones a crash left behind are swept
// the first time each folder is used in a session, once they are a day old.
//
public static class QueueArchives
{
    private const string Prefix = "queue-";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);

    private static readonly HashSet<string> Swept = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();

    public static string NewPath(string installPath)
    {
        var folder = Folder(installPath);
        SweepOnce(folder);
        return Path.Combine(folder, $"{Prefix}{Guid.NewGuid():N}.bin");
    }

    public static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Downloads", $"couldn't remove {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static string Folder(string installPath)
    {
        try
        {
            var local = Path.Combine(installPath, ModInstallService.WorkFolderName);
            Directory.CreateDirectory(local);
            return local;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var fallback = Path.Combine(Path.GetTempPath(), "TCFModManager");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    private static void SweepOnce(string folder)
    {
        lock (Gate)
        {
            if (!Swept.Add(folder)) return;
        }

        try
        {
            var cutoff = DateTime.UtcNow - StaleAfter;
            foreach (var file in Directory.EnumerateFiles(folder, Prefix + "*"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff) Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort; a stale archive is untidy, not harmful.
        }
    }
}
