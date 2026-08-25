using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// What counts as one of a server mod's own config files, and how to move those out of an install
// instead of deleting them along with the rest of the mod.
//
// The rule here is the single one both callers share: uninstalling a mod reads it to decide what to
// keep, and ModConfigDiscovery gates every file it finds through it, so the Configs page and the
// removal path can never end up disagreeing about what a config is.
//
// Client mods keep their config in BepInEx\config, outside the mod folder, so removal never touches
// it and there's nothing to preserve there.
//
public static class ModConfigFiles
{
    // Matched anywhere in a path, since server content is remapped under the install's own server
    // root (e.g. "SPT_Runtime/user/mods/..."). The ".disabled" sibling is recognised too, so a
    // disabled mod's config is still identified as one.
    private static readonly string[] ServerModsContainers =
        ["user/mods/", $"user/mods{DisabledModPaths.DisabledSuffix}/"];

    // Folder names a server mod keeps its config in, matched at any depth below the mod's own folder.
    private static readonly string[] ConfigFolderNames = ["config", "configs", "cfg"];

    //
    // JSON5 and JSONC are at least as common as plain JSON in SPT server mods - most of them ship a
    // config full of comments explaining each setting - and a file the mod itself reads happily is
    // one of its configs whatever extension it was given.
    //
    private static readonly string[] ConfigExtensions = [".json", ".json5", ".jsonc"];

    //
    // Names that count as a config when the file sits directly in a mod's own folder, where there is
    // no config folder to go by. This is the most common layout of all, but it is also the one that
    // has to be judged by name: a server mod's root holds bundle manifests, build output and shipped
    // data alongside its config, and only the file actually named after the job is one.
    //
    private static readonly string[] RootConfigStems = ["config", "configs", "cfg", "settings"];

    // Never a config when it sits in a mod's root: package.json is the mod's own manifest, and the
    // lock file beside it is generated.
    private static readonly string[] NeverConfigFileNames = ["package.json", "package-lock.json"];

    // True for a folder name a server mod would keep its config in.
    public static bool IsConfigFolderName(string name) =>
        ConfigFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    //
    // True for an install-relative path that is one of a server mod's own config files. Two layouts
    // count, and between them they cover what mods actually do:
    //
    //   <mod>/config/settings.json  - anything JSON inside a folder the mod calls "config"
    //   <mod>/config.json           - a conventionally-named file sitting at the mod's root
    //
    public static bool IsServerModConfig(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        var path = relativePath.Replace('\\', '/');
        if (!HasConfigExtension(path)) return false;

        if (SegmentsBelowServerMods(path) is not { } segments) return false;

        // <mod folder>/<file> - judged by the file's own name, since a mod's root is full of JSON
        // that isn't settings.
        if (segments.Length == 2)
        {
            var fileName = segments[1];

            return !NeverConfigFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
                && RootConfigStems.Contains(StemOf(fileName), StringComparer.OrdinalIgnoreCase);
        }

        // <mod folder>/.../<config folder>/<file> - at least a mod folder, a config folder and the
        // file itself, and the config folder has to be below the mod folder rather than be it.
        if (segments.Length < 3) return false;

        return segments[1..^1].Any(IsConfigFolderName);
    }

    // The path's segments below the server mods container, or null when it isn't under one.
    private static string[]? SegmentsBelowServerMods(string path)
    {
        // The live container is looked for first. Neither string is a prefix of the other once the
        // trailing separator is counted ("user/mods/" against "user/mods.disabled/"), so a path
        // under one is never taken for a path under the other.
        foreach (var container in ServerModsContainers)
        {
            var index = path.IndexOf(container, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            return path[(index + container.Length)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        return null;
    }

    private static bool HasConfigExtension(string path) =>
        ConfigExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // "config.json" -> "config". Note this also means "config.default.json" -> "config.default",
    // which deliberately isn't a config: a file shipped as a pristine copy of the defaults is not
    // the one being edited.
    private static string StemOf(string fileName) => Path.GetFileNameWithoutExtension(fileName);

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
            // "*.json*" rather than "*.json" so JSON5 and JSONC are seen too, and rather than "*" so
            // the walk isn't handed every file in a mod's data folder just to reject them one by one.
            foreach (var file in Directory.EnumerateFiles(modFolderPath, "*.json*", SearchOption.AllDirectories))
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
