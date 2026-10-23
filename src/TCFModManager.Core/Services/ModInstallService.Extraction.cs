using System.Diagnostics;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace TCFModManager.Core.Services;

public sealed partial class ModInstallService
{
    // Creates a per-install scratch folder for the download and extraction. Prefers a
    // hidden folder inside <paramref name="installPath"/> so extracted files can be moved into
    // place; falls back to %TEMP% when that folder can't be created. <paramref name="canMove"/> is
    // true when the scratch folder ended up on the same volume as the install.
    private static string CreateWorkDirectory(string installPath, out bool canMove)
    {
        var id = Guid.NewGuid().ToString("N");
        var localRoot = Path.Combine(installPath, WorkFolderName);

        try
        {
            var directory = Path.Combine(localRoot, id);
            Directory.CreateDirectory(directory);
            TryHide(localRoot);
            CleanStaleWorkDirectories(localRoot, directory);
            canMove = true;
            return directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var fallback = Path.Combine(Path.GetTempPath(), "TCFModManager", id);
            Directory.CreateDirectory(fallback);
            canMove = string.Equals(
                Path.GetPathRoot(Path.GetFullPath(fallback)),
                Path.GetPathRoot(Path.GetFullPath(installPath)),
                StringComparison.OrdinalIgnoreCase);
            return fallback;
        }
    }

    // Removes work folders left behind by a previous run that crashed or was killed.
    private static void CleanStaleWorkDirectories(string root, string current)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-6);
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (string.Equals(directory, current, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.GetCreationTimeUtc(directory) > cutoff) continue;
                TryDeleteDirectory(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private static void TryHide(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            if (!info.Attributes.HasFlag(FileAttributes.Hidden))
                info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Cosmetic only.
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void RemoveWorkFolderIfEmpty(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return;

        try { Directory.Delete(Path.Combine(installPath, WorkFolderName), recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Extracts every file entry in the archive at <paramref name="archivePath"/> into
    // <paramref name="extractDir"/>. Zip archives go through System.IO.Compression; every other
    // format goes through SharpCompress's forward-only reader. Zip-slip protection: any entry whose
    // resolved destination would land outside extractDir is rejected before anything is written.
    internal static async Task ExtractArchiveAsync(
        string archivePath,
        string extractDir,
        IProgress<ModInstallProgress>? status,
        CancellationToken ct,
        List<string>? folderEntries = null)
    {
        Directory.CreateDirectory(extractDir);
        var extractRoot = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;
        var folders = new FolderEntries(extractDir, extractRoot, folderEntries);

        if (ArchiveLayout.IsZipArchive(archivePath))
        {
            await ExtractZipAsync(archivePath, extractDir, extractRoot, status, folders, ct).ConfigureAwait(false);
            return;
        }

        ExtractWithSharpCompress(archivePath, extractDir, extractRoot, status, folders, ct);
    }

    //
    // The folder entries an archive lists, as full paths below extractDir; one that would land outside
    // it is dropped. Only collected when the caller asks for them.
    //
    private sealed class FolderEntries(string extractDir, string extractRoot, List<string>? into)
    {
        public void Add(string? key)
        {
            if (into is null || string.IsNullOrEmpty(key)) return;

            var full = Path.GetFullPath(Path.Combine(extractDir, key)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase)) into.Add(full);
        }
    }

    private static async Task ExtractZipAsync(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<ModInstallProgress>? status,
        FolderEntries folders,
        CancellationToken ct)
    {
        await using var file = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        foreach (var folder in zip.Entries.Where(e => string.IsNullOrEmpty(e.Name))) folders.Add(folder.FullName);

        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var extracted = 0;
        var reportClock = Stopwatch.StartNew();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var destination = ResolveEntryDestination(entry.FullName, extractDir, extractRoot);

            await using var source = entry.Open();
            await using var target = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
            await source.CopyToAsync(target, CopyBufferSize, ct).ConfigureAwait(false);

            extracted++;
            if (reportClock.Elapsed >= ProgressInterval)
            {
                status?.Report(new ModInstallProgress(
                    ModInstallStage.Extracting, Done: extracted, Total: entries.Count));
                reportClock.Restart();
            }
        }
    }

    //
    // Three paths, by what the archive is (D24):
    //   - solid archives and 7z: the forward-only reader, so a solid archive decompresses its blocks
    //     once here, instead of once per entry;
    //   - other archives (a non-solid RAR, a plain tar): the random-access entries - the reader throws
    //     for these, which failed every non-solid RAR install before v1.19.0;
    //   - a compressed tar (.tar.gz, .tar.bz2...), which SharpCompress can't open as an archive: the
    //     stream reader.
    //
    private static void ExtractWithSharpCompress(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<ModInstallProgress>? status,
        FolderEntries folders,
        CancellationToken ct)
    {
        IArchive archive;
        try
        {
            archive = ArchiveFactory.OpenArchive(archivePath);
        }
        catch (ArchiveOperationException)
        {
            ExtractWithStreamReader(archivePath, extractDir, extractRoot, status, folders, ct);
            return;
        }

        using (archive)
        {
            var total = TryCountEntries(archive);
            var progress = new ExtractProgress(status, total);

            if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
            {
                using var reader = archive.ExtractAllEntries();
                ExtractFromReader(reader, extractDir, extractRoot, progress, folders, ct);
                return;
            }

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();

                if (entry.IsDirectory)
                {
                    folders.Add(entry.Key);
                    continue;
                }

                if (entry.Key is not { Length: > 0 } key) continue;

                var destination = ResolveEntryDestination(key, extractDir, extractRoot);
                entry.WriteToFile(destination, new ExtractionOptions { Overwrite = true });
                progress.Step();
            }
        }
    }

    private static void ExtractWithStreamReader(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<ModInstallProgress>? status,
        FolderEntries folders,
        CancellationToken ct)
    {
        using var file = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(file);
        ExtractFromReader(reader, extractDir, extractRoot, new ExtractProgress(status, 0), folders, ct);
    }

    private static void ExtractFromReader(
        IReader reader, string extractDir, string extractRoot, ExtractProgress progress, FolderEntries folders, CancellationToken ct)
    {
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();

            if (reader.Entry.IsDirectory)
            {
                folders.Add(reader.Entry.Key);
                continue;
            }

            if (reader.Entry.Key is not { Length: > 0 } key) continue;

            var destination = ResolveEntryDestination(key, extractDir, extractRoot);
            reader.WriteEntryToFile(destination, new ExtractionOptions { Overwrite = true });
            progress.Step();
        }
    }

    // Reports extraction progress at most once per ProgressInterval.
    private sealed class ExtractProgress(IProgress<ModInstallProgress>? status, int total)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _done;

        public void Step()
        {
            _done++;
            if (_clock.Elapsed < ProgressInterval) return;

            status?.Report(new ModInstallProgress(ModInstallStage.Extracting, Done: _done, Total: total));
            _clock.Restart();
        }
    }

    private static int TryCountEntries(IArchive archive)
    {
        try { return archive.Entries.Count(e => !e.IsDirectory); }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return 0;
        }
    }

    // Resolves an archive entry's key to an absolute destination under
    // <paramref name="extractDir"/>, rejecting anything that would escape it, and creates the
    // containing directory.
    private static string ResolveEntryDestination(string entryKey, string extractDir, string extractRoot)
    {
        var destination = Path.GetFullPath(Path.Combine(extractDir, entryKey));
        if (!destination.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModInstallException(ModInstallFailure.UnsafeArchiveEntry) { ArchiveEntry = entryKey };
        }

        var destinationDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

        return destination;
    }
}
