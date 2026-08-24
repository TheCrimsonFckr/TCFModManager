using System.Diagnostics;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Downloads, extracts, and installs a mod version's files into an SPT install, and records what it placed for later uninstall.
public sealed class ModInstallService(ModDownloadService downloadService, ModInstallManifestService manifestService)
{
    private static readonly HashSet<string> KnownRootFolders =
        new(StringComparer.OrdinalIgnoreCase) { "BepInEx", "user", "SPT", "SPT_Runtime" };

    // Scratch folder created inside the SPT install so extracted files can be moved into
    // place rather than copied across volumes. Falls back to %TEMP% when it can't be created.
    private const string WorkFolderName = ".tcfmm-work";

    private const int CopyBufferSize = 1 << 20;

    // Minimum gap between status reports during extract/install, so a several-thousand-file
    // archive doesn't post one UI update per file.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    // Processes that hold handles on files inside an SPT install. Placing or deleting a mod's
    // files while one of these is running fails partway through, which on an update leaves the old
    // version already removed - so both install and uninstall refuse to start until they're closed.
    private static readonly string[] BlockingProcessNames = ["EscapeFromTarkov", "SPT.Server", "Aki.Server"];

    // The blocking processes currently running, by display name, or an empty list when the install
    // is safe to modify.
    public static IReadOnlyList<string> RunningBlockers()
    {
        var running = new List<string>();

        foreach (var name in BlockingProcessNames)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0) running.Add(name + ".exe");
            }
            catch (InvalidOperationException)
            {
                // Process list unavailable - treated as nothing running rather than blocking the user.
            }
        }

        return running;
    }

    // Throws when a blocking process is running, naming it and the action being attempted.
    public static void EnsureInstallNotInUse(string action)
    {
        var running = RunningBlockers();
        if (running.Count == 0) return;

        throw new InvalidOperationException(
            $"Close {string.Join(" and ", running)} before {action} - files inside the SPT install are locked while it's running.");
    }

    // Downloads and installs <paramref name="version"/> of <paramref name="mod"/> into
    // <paramref name="installPath"/>. If a record already exists for this mod (an update), its old
    // files are removed once the new archive has downloaded and extracted successfully.
    // Cancellation is honoured up to the point the old version is removed; once files
    // start being placed into the install the operation runs to completion.
    public async Task<InstalledModRecord> InstallAsync(
        Mod mod,
        ModVersion version,
        string installPath,
        IProgress<string>? status = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            throw new InvalidOperationException("No SPT install folder set - configure it on the Options page first.");

        if (string.IsNullOrWhiteSpace(version.Link))
            throw new InvalidOperationException($"{mod.Name} {version.Version} has no download link.");

        EnsureInstallNotInUse("installing a mod");

        AppLog.Info("Install", $"{mod.Name} {version.Version} (mod {mod.Id}) -> {installPath}");

        var workDir = CreateWorkDirectory(installPath, out var canMoveIntoInstall);
        AppLog.Debug("Install", $"work dir {workDir} (move into install: {canMoveIntoInstall})");
        var archivePath = Path.Combine(workDir, "download.bin");
        var extractDir = Path.Combine(workDir, "extracted");

        try
        {
            ct.ThrowIfCancellationRequested();

            status?.Report($"Downloading {mod.Name} {version.Version}...");
            await downloadService.DownloadAsync(version.Link, archivePath, downloadProgress, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            var archiveBytes = new FileInfo(archivePath).Length;
            AppLog.Debug("Install", $"downloaded {archiveBytes:N0} bytes, zip={IsZipArchive(archivePath)}");

            status?.Report("Extracting...");
            // Auto-detects archive format from the file header rather than assuming zip.
            var extractTimer = System.Diagnostics.Stopwatch.StartNew();
            await ExtractArchiveAsync(archivePath, extractDir, status, ct).ConfigureAwait(false);
            AppLog.Debug("Install", $"extracted in {extractTimer.ElapsedMilliseconds}ms");

            ct.ThrowIfCancellationRequested();

            var contentRoot = FindContentRoot(extractDir);
            var topLevelNames = Directory.GetFileSystemEntries(contentRoot).Select(Path.GetFileName);
            if (!topLevelNames.Any(n => n is not null && KnownRootFolders.Contains(n)))
            {
                AppLog.Warn("Install",
                    $"{mod.Name} {version.Version} archive has no known root folder; top level: " +
                    string.Join(", ", Directory.GetFileSystemEntries(contentRoot).Select(Path.GetFileName)));

                throw new InvalidOperationException(
                    $"{mod.Name} {version.Version}'s archive doesn't look like a normal SPT mod package " +
                    "(no BepInEx/user/SPT folder found in it) - install it manually instead.");
            }

            // Archives package server-side content as "user/..."; remap it to wherever this install
            // actually keeps user/mods (e.g. nested under "SPT" or "SPT_Runtime"). BepInEx stays at
            // the install root. Falls back to no remapping if the server exe can't be found.
            SptInstallationService.TryGetServerRoot(installPath, out var serverRoot);

            var sourceFiles = Directory.GetFiles(contentRoot, "*", SearchOption.AllDirectories);

            ct.ThrowIfCancellationRequested();

            // Re-checked now the download is finished: SPT may have been started while it ran, and
            // everything past this point deletes or places files inside the install.
            EnsureInstallNotInUse("installing a mod");

            var manifest = manifestService.Load();
            var existing = manifest.Mods.FirstOrDefault(m => m.ModId == mod.Id);
            if (existing is not null)
            {
                status?.Report($"Removing the previously installed version ({existing.Version})...");
                await UninstallAsync(installPath, existing, ConfigAction.Keep, CancellationToken.None).ConfigureAwait(false);
            }

            status?.Report(FormatCount("Installing", 0, sourceFiles.Length));
            var placedFiles = new List<string>(sourceFiles.Length);
            var reportClock = Stopwatch.StartNew();

            try
            {
                for (var i = 0; i < sourceFiles.Length; i++)
                {
                    var file = sourceFiles[i];
                    var archiveRelative = Path.GetRelativePath(contentRoot, file);
                    var installRelative = RemapForServerRoot(archiveRelative, serverRoot);
                    // Forward-slash regardless of OS, matching InstalledModRecord.Files's documented format.
                    var installRelativeForward = installRelative.Replace('\\', '/');

                    var destination = Path.Combine(installPath, installRelative);
                    var destinationDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

                    if (canMoveIntoInstall) File.Move(file, destination, overwrite: true);
                    else File.Copy(file, destination, overwrite: true);

                    placedFiles.Add(installRelativeForward);

                    if (reportClock.Elapsed >= ProgressInterval)
                    {
                        status?.Report(FormatCount("Installing", i + 1, sourceFiles.Length));
                        reportClock.Restart();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // What was placed before the failure is recorded anyway, so those files stay
                // app-managed: a retry overwrites them and a removal cleans them up. Without this an
                // interrupted update leaves the old version deleted and the new one untracked.
                SaveRecord(mod, version, placedFiles, incomplete: true);

                AppLog.Error("Install",
                    $"{mod.Name} {version.Version} incomplete after {placedFiles.Count}/{sourceFiles.Length} file(s)", ex);

                throw new InvalidOperationException(
                    $"{mod.Name} {version.Version} was only partly installed - {placedFiles.Count} of {sourceFiles.Length} " +
                    $"files were placed before this failed: {ex.Message} Close SPT and its server, then install it again.",
                    ex);
            }

            var record = SaveRecord(mod, version, placedFiles, incomplete: false);

            AppLog.Info("Install",
                $"{mod.Name} {version.Version} placed {placedFiles.Count} file(s) in folders [{string.Join(", ", record.Folders)}]");

            status?.Report("Done.");
            return record;
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("Install", $"{mod.Name} {version.Version} cancelled");
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("Install", $"{mod.Name} {version.Version} failed", ex);
            throw;
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    // Writes the record for what an install placed, replacing any previous record for the same mod.
    // The manifest is reloaded rather than reusing an earlier copy, since UninstallAsync may have
    // saved a removal of the old record in between.
    private InstalledModRecord SaveRecord(Mod mod, ModVersion version, List<string> placedFiles, bool incomplete)
    {
        var record = new InstalledModRecord
        {
            ModId = mod.Id,
            Guid = mod.Guid,
            Name = mod.Name ?? $"Mod {mod.Id}",
            VersionId = version.Id,
            Version = version.Version ?? "unknown",
            InstalledAt = DateTimeOffset.UtcNow,
            Files = placedFiles,
            Folders = InstalledModFolders.FromPlacedFiles(placedFiles),
            Incomplete = incomplete,
        };

        var current = manifestService.Load();
        current.Mods.RemoveAll(m => m.ModId == mod.Id);
        current.Mods.Add(record);
        manifestService.Save(current);

        return record;
    }

    // Removes every file InstalledModRecord.Files lists, then deletes any directory left
    // empty (working bottom-up), then drops the record from the manifest. Files that can't be
    // deleted are collected into the result instead of aborting the rest of the removal.
    // <paramref name="configs"/> decides what happens to the mod's own config JSON files first.
    public Task<UninstallResult> UninstallAsync(
        string installPath,
        InstalledModRecord record,
        ConfigAction configs = ConfigAction.Keep,
        CancellationToken ct = default)
    {
        EnsureInstallNotInUse("removing a mod");

        var failed = new List<string>();
        var deleted = 0;
        var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Moved out before the delete loop runs, so the loop simply finds them gone.
        var kept = new KeptConfigs(0, null);
        List<string> configFiles = configs == ConfigAction.Keep ? ModConfigFiles.InRecord(record) : [];
        if (configFiles.Count > 0)
            kept = ModConfigFiles.MoveOut(installPath, configFiles, record.Name, DateTimeOffset.UtcNow);

        foreach (var relative in record.Files)
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    deleted++;
                }

                for (var dir = Path.GetDirectoryName(fullPath); IsUnderInstallPath(dir, installPath); dir = Path.GetDirectoryName(dir))
                    touchedDirectories.Add(dir!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(relative);
            }
        }

        foreach (var dir in touchedDirectories.OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Directory that won't delete is left alone.
            }
        }

        var manifest = manifestService.Load();
        manifest.Mods.RemoveAll(m => m.ModId == record.ModId);
        manifestService.Save(manifest);

        return Task.FromResult(new UninstallResult(deleted, failed, kept.Count, kept.Folder));
    }

    // Deletes a mod's whole folder (or, for a loose top-level DLL, just that file) - the
    // removal path for a mod this app didn't install itself, since there's no per-file manifest
    // record to work from. Callers should confirm the exact path with the user before calling this.
    public static void RemoveLegacyPath(string path)
    {
        EnsureInstallNotInUse("removing a mod");

        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }

    // The config files a hand-installed mod keeps in its own folder, as install-relative paths.
    // Read off disk rather than from a record, since this path has none.
    public static List<string> FindLegacyConfigs(string installPath, IEnumerable<string> modFolderPaths) =>
        modFolderPaths.SelectMany(folder => ModConfigFiles.InFolder(installPath, folder)).Distinct().ToList();

    // Moves a hand-installed mod's config files out of the install before its folder is deleted.
    public static KeptConfigs KeepLegacyConfigs(string installPath, IEnumerable<string> relativeFiles, string modName) =>
        ModConfigFiles.MoveOut(installPath, relativeFiles, modName, DateTimeOffset.UtcNow);

    // Some mod archives wrap their real content ("BepInEx/...", "user/...") inside an
    // extra top-level folder (e.g. "HollywoodFX-1.8.4/"). Descends through single-directory wrapper
    // levels (capped at 4) until it finds the real content root.
    private static string FindContentRoot(string extractDir)
    {
        var current = extractDir;

        for (var depth = 0; depth < 4; depth++)
        {
            var entries = Directory.GetFileSystemEntries(current);
            if (entries.Length != 1 || !Directory.Exists(entries[0])) break;

            var name = Path.GetFileName(entries[0]);
            if (name is not null && KnownRootFolders.Contains(name)) break;

            current = entries[0];
        }

        return current;
    }

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

    private static string FormatCount(string verb, int done, int total) =>
        total > 0 ? $"{verb} {done}/{total} files..." : $"{verb} {done} files...";

    // Extracts every file entry in the archive at <paramref name="archivePath"/> into
    // <paramref name="extractDir"/>. Zip archives go through System.IO.Compression; every other
    // format goes through SharpCompress's forward-only reader. Zip-slip protection: any entry whose
    // resolved destination would land outside extractDir is rejected before anything is written.
    private static async Task ExtractArchiveAsync(
        string archivePath,
        string extractDir,
        IProgress<string>? status,
        CancellationToken ct)
    {
        Directory.CreateDirectory(extractDir);
        var extractRoot = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        if (IsZipArchive(archivePath))
        {
            await ExtractZipAsync(archivePath, extractDir, extractRoot, status, ct).ConfigureAwait(false);
            return;
        }

        ExtractWithSharpCompress(archivePath, extractDir, extractRoot, status, ct);
    }

    // Reads the local-file-header magic rather than trusting the file extension, matching
    // how the previous SharpCompress-only path detected format.
    private static bool IsZipArchive(string archivePath)
    {
        try
        {
            using var stream = File.OpenRead(archivePath);
            Span<byte> header = stackalloc byte[4];
            if (stream.ReadAtLeast(header, 4, throwOnEndOfStream: false) < 4) return false;

            return header[0] == 0x50 && header[1] == 0x4B
                && ((header[2] == 0x03 && header[3] == 0x04)
                    || (header[2] == 0x05 && header[3] == 0x06)
                    || (header[2] == 0x07 && header[3] == 0x08));
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task ExtractZipAsync(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<string>? status,
        CancellationToken ct)
    {
        await using var file = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

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
                status?.Report(FormatCount("Extracting", extracted, entries.Count));
                reportClock.Restart();
            }
        }
    }

    private static void ExtractWithSharpCompress(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<string>? status,
        CancellationToken ct)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);

        var total = TryCountEntries(archive);

        // Forward-only reader rather than random-access Entries: a solid archive decompresses its
        // blocks once here, instead of once per entry.
        using var reader = archive.ExtractAllEntries();
        var extracted = 0;
        var reportClock = Stopwatch.StartNew();

        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();

            if (reader.Entry.IsDirectory) continue;
            if (reader.Entry.Key is not { Length: > 0 } key) continue;

            var destination = ResolveEntryDestination(key, extractDir, extractRoot);
            reader.WriteEntryToFile(destination, new ExtractionOptions { Overwrite = true });

            extracted++;
            if (reportClock.Elapsed >= ProgressInterval)
            {
                status?.Report(FormatCount("Extracting", extracted, total));
                reportClock.Restart();
            }
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
            throw new InvalidOperationException(
                $"Archive entry \"{entryKey}\" would extract outside the target folder - refusing to extract it.");
        }

        var destinationDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

        return destination;
    }

    // Remaps the "user" top-level folder to <paramref name="serverRoot"/>. No-op when
    // <paramref name="serverRoot"/> is "".
    private static string RemapForServerRoot(string archiveRelative, string serverRoot)
    {
        if (string.IsNullOrEmpty(serverRoot)) return archiveRelative;

        var firstSegment = archiveRelative.Split(Path.DirectorySeparatorChar, 2)[0];
        return string.Equals(firstSegment, "user", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(serverRoot, archiveRelative)
            : archiveRelative;
    }

    private static bool IsUnderInstallPath(string? dir, string installPath)
    {
        if (string.IsNullOrEmpty(dir)) return false;

        var fullDir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        var fullInstall = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar);
        return fullDir.Length > fullInstall.Length
            && fullDir.StartsWith(fullInstall, StringComparison.OrdinalIgnoreCase);
    }
}

// Result of ModInstallService.UninstallAsync. FailedFiles lists files that couldn't be
// deleted; the mod is still removed from the manifest regardless. ConfigsKept/ConfigsFolder
// describe the mod's own config files when they were moved out rather than deleted.
public sealed record UninstallResult(int FilesDeleted, List<string> FailedFiles, int ConfigsKept = 0, string? ConfigsFolder = null);

// What to do with a mod's own config JSON files when removing it.
public enum ConfigAction
{
    // Move them into AppPaths.LegacyConfigsDirectory instead of deleting them.
    Keep,

    // Delete them along with the rest of the mod's files.
    Delete,
}
