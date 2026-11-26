namespace TCFModManager.Core.Services;

/// <summary>What Purge logs takes away from one install.</summary>
/// <param name="Paths">Log files and game log folders, each as a whole.</param>
/// <param name="Files">Files in all, counting those inside the folders.</param>
/// <param name="Bytes">Their size in all.</param>
public sealed record LogPurgePlan(IReadOnlyList<string> Paths, int Files, long Bytes)
{
    public bool IsEmpty => Paths.Count == 0;
}

//
// OPEN-73: the server's logs, the game's Logs\log_* folders (with this app's kept copies of them) and
// BepInEx's logs. The launcher's, installers' and patcher's logs are left.
//
public static class LogPurge
{
    private static readonly string[] ServerExtensions = [".log", ".txt"];

    public static LogPurgePlan Plan(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) return new([], 0, 0);

        var paths = new List<string>();
        var files = 0;
        long bytes = 0;

        void AddFile(FileInfo file)
        {
            paths.Add(file.FullName);
            files++;
            bytes += file.Length;
        }

        if (ServerLogs.Folder(installPath) is { } logs)
        {
            foreach (var file in Files(logs, "*", recurse: true)
                .Where(f => ServerExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)
                    && !f.Name.StartsWith("launcher", StringComparison.OrdinalIgnoreCase)))
            {
                AddFile(file);
            }
        }

        var gameRoot = SptInstallationService.ToGameRoot(installPath) ?? installPath;

        foreach (var parent in new[] { Path.Combine(gameRoot, "Logs"), LogFiles.KeptGameLogsFolder(gameRoot) })
        {
            foreach (var folder in Folders(parent, "log_*"))
            {
                paths.Add(folder.FullName);
                foreach (var file in Files(folder.FullName, "*", recurse: true))
                {
                    files++;
                    bytes += file.Length;
                }
            }
        }

        foreach (var file in Files(Path.Combine(gameRoot, "BepInEx"), "*LogOutput*.log", recurse: false)) AddFile(file);

        return new LogPurgePlan(paths, files, bytes);
    }

    private static IEnumerable<FileInfo> Files(string folder, string pattern, bool recurse)
    {
        try
        {
            return Directory.Exists(folder)
                ? [.. new DirectoryInfo(folder).EnumerateFiles(pattern, new EnumerationOptions { RecurseSubdirectories = recurse, IgnoreInaccessible = true })]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Diagnose", $"couldn't look in {folder}: {ex.Message}");
            return [];
        }
    }

    private static IEnumerable<DirectoryInfo> Folders(string folder, string pattern)
    {
        try
        {
            return Directory.Exists(folder) ? [.. new DirectoryInfo(folder).EnumerateDirectories(pattern)] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Diagnose", $"couldn't look in {folder}: {ex.Message}");
            return [];
        }
    }
}
