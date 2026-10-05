using System.IO.Compression;
using SharpCompress.Archives;

namespace TCFModManager.Core.Services;

//
// How a mod archive maps onto an SPT install: which top-level folders mark real content, how far
// down a wrapper folder the content sits, and where "user/..." lands in an install that keeps its
// server under SPT\ or SPT_Runtime\.
//
// Shared by the installer, which applies it to a folder it has just extracted, and by a
// download-only save, which applies it to the archive's entry list without extracting anything. The
// two have to agree exactly: the list a download records is what a later scan compares against the
// disk, so a rule that differed here would report a correct hand install as the wrong version.
//
public static class ArchiveLayout
{
    public static readonly IReadOnlySet<string> KnownRootFolders =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BepInEx", "user", "SPT", "SPT_Runtime" };

    // How many single-folder wrapper levels are looked through before giving up.
    private const int MaxWrapperDepth = 4;

    public static bool IsKnownRoot(string? name) => name is not null && KnownRootFolders.Contains(name);

    //
    // BepInEx's own folders, packed without the BepInEx folder around them ("plugins/MyMod.dll").
    // A content level holding only these - and files beside them - is placed under BepInEx\.
    //
    private static readonly IReadOnlyDictionary<string, string> BareBepInExFolders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["plugins"] = "BepInEx/plugins",
            ["patchers"] = "BepInEx/patchers",
        };

    // Files for people rather than the game: by extension, or by whole name when there is none.
    private static readonly HashSet<string> BesideExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".url", ".pdf", ".rtf", ".nfo",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".html", ".htm",
    };

    private static readonly HashSet<string> BesideNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "readme", "license", "licence", "changelog", "changes", "credits", "copying", "notice",
    };

    //
    // A read-me, licence or picture: what an author leaves beside the real content - beside a wrapper
    // folder, beside BepInEx\ and user\, beside plugins\. At the top of the content it would land in
    // the SPT folder itself, so it is never placed there (see Place); deeper down it goes with the mod.
    //
    public static bool IsBeside(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length == 0 ? BesideNames.Contains(fileName) : BesideExtensions.Contains(extension);
    }

    private static bool StopsDescent(string name) => IsKnownRoot(name) || BareBepInExFolders.ContainsKey(name);

    // Whether a content level is BepInEx's folders on their own, with nothing else but files beside them.
    public static bool IsBareBepInEx(IEnumerable<string> folderNames, IEnumerable<string> fileNames)
    {
        var folders = folderNames.ToList();
        return folders.Count > 0 && folders.All(BareBepInExFolders.ContainsKey) && fileNames.All(IsBeside);
    }

    public static bool IsBareBepInEx(string contentRoot) =>
        IsBareBepInEx(
            Directory.GetDirectories(contentRoot).Select(d => Path.GetFileName(d)!),
            Directory.GetFiles(contentRoot).Select(f => Path.GetFileName(f)!));

    // Whether a content level is something an install can place: a known root, or a bare BepInEx layout.
    public static bool IsRecognised(IReadOnlyCollection<string> folderNames, IReadOnlyCollection<string> fileNames) =>
        folderNames.Any(IsKnownRoot) || fileNames.Any(IsKnownRoot) || IsBareBepInEx(folderNames, fileNames);

    //
    // Where a content-relative path (forward slashes) goes in the install, before the server-root
    // remap - or null when it is not placed: a file beside the content at its top, or, in a bare
    // BepInEx layout, anything outside plugins\ and patchers\.
    //
    public static string? Place(string contentRelative, bool bareBepInEx)
    {
        var parts = contentRelative.Split('/', 2);
        if (parts.Length == 1 && IsBeside(parts[0])) return null;

        if (!bareBepInEx) return contentRelative;

        return parts.Length == 2 && BareBepInExFolders.TryGetValue(parts[0], out var folder)
            ? folder + "/" + parts[1]
            : null;
    }

    //
    // The folder under an extracted archive that holds its real content. Some archives wrap
    // "BepInEx/..." and "user/..." inside an extra top-level folder ("HollywoodFX-1.8.4/"), so single
    // folders are descended through until a known root, plugins\ or patchers\, a second folder, a file
    // that isn't a read-me or picture, or the depth cap.
    //
    public static string FindContentRoot(string extractDir)
    {
        var current = extractDir;

        for (var depth = 0; depth < MaxWrapperDepth; depth++)
        {
            var folders = Directory.GetDirectories(current);
            if (folders.Length != 1 || !Directory.GetFiles(current).All(f => IsBeside(Path.GetFileName(f)))) break;
            if (StopsDescent(Path.GetFileName(folders[0]))) break;

            current = folders[0];
        }

        return current;
    }

    //
    // The same descent as FindContentRoot, over forward-slash file entry paths instead of a folder.
    // Returns the prefix to strip, ending in "/", or "" when the content sits at the top.
    //
    // Only file entries are passed in, which matches what extraction produces - neither extractor
    // writes directory entries - so a wrapper holding one empty folder beside the real one is seen
    // the same way by both.
    //
    public static string FindContentPrefix(IReadOnlyCollection<string> files)
    {
        var prefix = string.Empty;

        for (var depth = 0; depth < MaxWrapperDepth; depth++)
        {
            string? only = null;
            var single = true;

            foreach (var file in files)
            {
                var rest = file[prefix.Length..];
                var slash = rest.IndexOf('/');

                // A file directly at this level means the level is not a lone wrapper folder - unless
                // it is a read-me or picture beside it.
                if (slash < 0)
                {
                    if (IsBeside(rest)) continue;
                    single = false;
                    break;
                }

                var name = rest[..slash];
                if (only is null) only = name;
                else if (!string.Equals(only, name, StringComparison.Ordinal)) { single = false; break; }
            }

            if (!single || only is null || StopsDescent(only)) break;

            prefix += only + "/";
        }

        return prefix;
    }

    //
    // Remaps the "user" top-level folder to <paramref name="serverRoot"/>, leaving everything else -
    // BepInEx above all - at the install root. No-op when serverRoot is "".
    //
    public static string RemapForServerRoot(string archiveRelative, string serverRoot)
    {
        if (string.IsNullOrEmpty(serverRoot)) return archiveRelative;

        var firstSegment = archiveRelative.Split(Path.DirectorySeparatorChar, 2)[0];
        return string.Equals(firstSegment, "user", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(serverRoot, archiveRelative)
            : archiveRelative;
    }

    //
    // What an archive would place in an install, worked out from its entry list alone.
    //
    // Never throws for a bad archive. A download-only save has already succeeded by the time this
    // runs, and the file is the user's whatever the app makes of it, so an archive that can't be
    // read, has an entry escaping its own root, or has no known root folder comes back
    // Recognised: false with nothing listed.
    //
    public static ArchivePlan Plan(string archivePath, string serverRoot)
    {
        List<ArchiveFileEntry> entries;

        try
        {
            entries = ReadEntries(archivePath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // SharpCompress throws a family of its own types that don't share a base with the BCL's
            // - "not an archive" alone is an ArchiveOperationException - and every one of them means
            // the same thing here: nothing to compare the disk against.
            AppLog.Warn("Download", $"couldn't list {Path.GetFileName(archivePath)}: {ex.Message}");
            return ArchivePlan.Unrecognised;
        }

        return Plan(entries, serverRoot);
    }

    // The entry-list half of Plan, separate so it can be tested without building archives.
    public static ArchivePlan Plan(IReadOnlyList<ArchiveFileEntry> entries, string serverRoot)
    {
        var normalised = new List<ArchiveFileEntry>(entries.Count);

        foreach (var entry in entries)
        {
            if (Normalise(entry.Path) is not { } path)
            {
                AppLog.Warn("Download", $"archive entry {entry.Path} escapes the archive root");
                return ArchivePlan.Unrecognised;
            }

            normalised.Add(entry with { Path = path });
        }

        var prefix = FindContentPrefix(normalised.Select(e => e.Path).ToList());
        var content = normalised.Where(e => e.Path.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        var topLevel = content.Select(e => e.Path[prefix.Length..].Split('/', 2)).ToList();
        var topFolders = topLevel.Where(p => p.Length == 2).Select(p => p[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var topFiles = topLevel.Where(p => p.Length == 1).Select(p => p[0]).ToList();

        if (!IsRecognised(topFolders, topFiles)) return ArchivePlan.Unrecognised;
        var bare = IsBareBepInEx(topFolders, topFiles);

        var files = content
            .Select(e => Place(e.Path[prefix.Length..], bare) is { } placed
                ? e with { Path = RemapForServerRoot(placed.Replace('/', Path.DirectorySeparatorChar), serverRoot).Replace('\\', '/') }
                : null)
            .OfType<ArchiveFileEntry>()
            // SPT's own files are never placed by an install, so a hand install isn't expected to
            // place them either (D6).
            .Where(e => !ProtectedInstallPaths.IsProtected(e.Path))
            .ToList();

        return new ArchivePlan(true, files, InstalledModFolders.FromPlacedFiles(files.Select(f => f.Path)));
    }

    //
    // Whether a file starts with a zip signature. Read from the header rather than trusted from the
    // extension, since a Forge link says nothing about what it serves.
    //
    public static bool IsZipArchive(string archivePath) => Detect(archivePath) == ArchiveKind.Zip;

    public static ArchiveKind Detect(string archivePath)
    {
        try
        {
            using var stream = File.OpenRead(archivePath);
            Span<byte> header = stackalloc byte[6];
            var read = stream.ReadAtLeast(header, 6, throwOnEndOfStream: false);

            if (read >= 4 && header[0] == 0x50 && header[1] == 0x4B
                && ((header[2] == 0x03 && header[3] == 0x04)
                    || (header[2] == 0x05 && header[3] == 0x06)
                    || (header[2] == 0x07 && header[3] == 0x08)))
                return ArchiveKind.Zip;

            if (read >= 6 && header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC
                && header[3] == 0xAF && header[4] == 0x27 && header[5] == 0x1C)
                return ArchiveKind.SevenZip;

            if (read >= 4 && header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21)
                return ArchiveKind.Rar;

            return ArchiveKind.Unknown;
        }
        catch (IOException)
        {
            return ArchiveKind.Unknown;
        }
    }

    public static string ExtensionFor(ArchiveKind kind) => kind switch
    {
        ArchiveKind.Zip => ".zip",
        ArchiveKind.SevenZip => ".7z",
        ArchiveKind.Rar => ".rar",
        _ => ".bin",
    };

    private static List<ArchiveFileEntry> ReadEntries(string archivePath)
    {
        if (IsZipArchive(archivePath))
        {
            using var zip = ZipFile.OpenRead(archivePath);
            return
            [
                .. zip.Entries
                    .Where(e => !string.IsNullOrEmpty(e.Name))
                    .Select(e => new ArchiveFileEntry(e.FullName, e.Length))
            ];
        }

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        return
        [
            .. archive.Entries
                .Where(e => !e.IsDirectory && e.Key is { Length: > 0 })
                .Select(e => new ArchiveFileEntry(e.Key!, e.Size))
        ];
    }

    //
    // An entry path as forward slashes with no leading "./" or "/", or null when it would land
    // outside the archive's own root - the same entries the installer refuses as UnsafeArchiveEntry.
    //
    private static string? Normalise(string entryPath)
    {
        var path = entryPath.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];

        if (path.StartsWith('/') || path.Contains(':')) return null;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s == "..")) return null;

        return string.Join('/', segments.Where(s => s != "."));
    }
}

public enum ArchiveKind
{
    Unknown,
    Zip,
    SevenZip,
    Rar,
}

// One file inside an archive: its path and its uncompressed size.
public sealed record ArchiveFileEntry(string Path, long Size);

//
// What an archive would place: every file install-relative and forward-slash, after the wrapper is
// stripped and "user/" remapped, and the mod folders those files make.
//
public sealed record ArchivePlan(bool Recognised, IReadOnlyList<ArchiveFileEntry> Files, IReadOnlyList<string> Folders)
{
    public static ArchivePlan Unrecognised { get; } = new(false, [], []);
}
