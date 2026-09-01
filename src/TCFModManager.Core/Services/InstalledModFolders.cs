using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Works out which mod folders an install placed, from the file list it recorded. These are the
// names InstalledModScanner reports for the same mod, so they're what links a manifest record to
// what's on disk - far more reliable than guessing the catalog listing from a folder name
// ("EpicsAIO" is never going to fuzzy-match "Epic's All in One").
// 
public static class InstalledModFolders
{
    // Folders whose immediate child is a mod. Matched anywhere in the path, since server content is
    // remapped under the install's own server root (e.g. "SPT_Runtime/user/mods/...").
    private static string[] Containers => DisabledModPaths.Containers;

    // 
    // The distinct mod folder names in <paramref name="relativeFiles"/>, in the order first seen.
    // A loose DLL sitting directly in a container contributes its own file name without extension,
    // matching how the scanner names it.
    // 
    public static List<string> FromPlacedFiles(IEnumerable<string> relativeFiles)
    {
        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in relativeFiles)
        {
            var name = FolderFor(file);
            if (name is null || !seen.Add(name)) continue;

            folders.Add(name);
        }

        return folders;
    }

    // The folder names for a record, falling back to deriving them from its file list for
    // records written before the folder names were stored.
    public static IReadOnlyList<string> Resolve(InstalledModRecord record) =>
        record.Folders.Count > 0 ? record.Folders : FromPlacedFiles(record.Files);

    //
    // The files a record placed inside one of its folders, each relative to that folder. Empty for a
    // folder the record doesn't name, and for a manually-confirmed record, which places no files.
    //
    // What this is for: a folder still holding the files this app put in it is still that install's,
    // whatever the DLLs inside it claim to be.
    //
    public static List<string> PlacedFilesUnder(InstalledModRecord record, string folderName)
    {
        var results = new List<string>();

        foreach (var file in record.Files)
        {
            if (Split(file) is not { } split) continue;
            if (!string.Equals(split.Folder, folderName, StringComparison.OrdinalIgnoreCase)) continue;

            results.Add(split.Relative);
        }

        return results;
    }

    private static string? FolderFor(string relativePath) => Split(relativePath)?.Folder;

    // The mod folder a placed file sits in, and the rest of its path below that folder.
    private static (string Folder, string Relative)? Split(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');

        foreach (var container in Containers)
        {
            var marker = container + "/";
            var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            var remainder = path[(index + marker.Length)..];
            if (remainder.Length == 0) continue;

            var slash = remainder.IndexOf('/');

            // No further separator means the file sits directly in the container - a loose DLL,
            // which the scanner names after the file itself, so it is both the folder and the only
            // file in it.
            return slash < 0
                ? (Path.GetFileNameWithoutExtension(remainder), remainder)
                : (remainder[..slash], remainder[(slash + 1)..]);
        }

        return null;
    }
}
