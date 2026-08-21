using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Finds the JSON config files a server mod keeps inside its own folder
// (user/mods/&lt;mod&gt;/config/*.json) and moves them out of the install instead of deleting them
// along with the rest of the mod. Client mods keep their config in BepInEx/config, outside the mod
// folder, so removal never touches it and there's nothing to preserve there.
//
public static class ModConfigFiles
{
    private const string ServerModsContainer = "user/mods/";

    private static readonly string[] ConfigFolderNames = ["config", "configs"];

    // True for an install-relative path that sits in a config folder inside a server mod's own
    // folder - "user/mods/SomeMod/config/settings.json" and any deeper nesting under it.
    public static bool IsServerModConfig(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        var path = relativePath.Replace('\\', '/');
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

        var index = path.IndexOf(ServerModsContainer, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;

        var segments = path[(index + ServerModsContainer.Length)..].Split('/', StringSplitOptions.RemoveEmptyEntries);

        // <mod folder>/.../<config folder>/<file>.json - at least a mod folder, a config folder and
        // the file itself, and the config folder has to be below the mod folder rather than be it.
        if (segments.Length < 3) return false;

        return segments[1..^1].Any(s => ConfigFolderNames.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    // The install-relative config paths in a record's file list.
    public static List<string> InRecord(InstalledModRecord record) =>
        record.Files.Where(IsServerModConfig).ToList();

    //
    // Config files inside a mod folder on disk, as install-relative paths. Used by the
    // hand-installed removal path, where there's no record to read a file list from.
    //
    public static List<string> InFolder(string installPath, string modFolderPath)
    {
        var results = new List<string>();
        if (!Directory.Exists(modFolderPath)) return results;

        try
        {
            foreach (var file in Directory.EnumerateFiles(modFolderPath, "*.json", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(installPath, file).Replace('\\', '/');
                if (IsServerModConfig(relative)) results.Add(relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a folder that can't be walked just reports no configs to keep.
        }

        return results;
    }

    //
    // Moves <paramref name="relativeFiles"/> out of the install into a timestamped folder under
    // AppPaths.LegacyConfigsDirectory, keeping each file's path relative to the install root so the
    // whole tree can be copied back over an SPT install to restore it. Returns the folder they were
    // moved into, with the count actually moved.
    //
    public static KeptConfigs MoveOut(string installPath, IEnumerable<string> relativeFiles, string modName, DateTimeOffset timestamp)
    {
        var destinationRoot = Path.Combine(
            AppPaths.LegacyConfigsDirectory,
            $"{timestamp.ToLocalTime():yyyyMMdd-HHmmss}_{SafeFolderName(modName)}");

        var moved = 0;

        foreach (var relative in relativeFiles)
        {
            var source = Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source)) continue;

            var destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

                File.Move(source, destination, overwrite: true);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Configs", $"couldn't keep {relative}: {ex.Message}");
            }
        }

        if (moved == 0) return new KeptConfigs(0, null);

        AppLog.Info("Configs", $"kept {moved} config file(s) from {modName} in {destinationRoot}");
        return new KeptConfigs(moved, destinationRoot);
    }

    private static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "mod" : cleaned;
    }
}

// How many config files were moved out of the install, and the folder holding them.
public sealed record KeptConfigs(int Count, string? Folder);
