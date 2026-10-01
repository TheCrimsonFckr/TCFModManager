using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Downloads, extracts, and installs a mod version's files into an SPT install, and records what it placed for later uninstall.
public sealed class ModInstallService(
    ModDownloadService downloadService,
    ModInstallManifestService manifestService,
    ConfigCarryOver? configCarryOver = null,
    ConfigUpdateLog? configUpdateLog = null,
    ModConfigOptionsStore? configOptions = null)
{
    private readonly ConfigCarryOver _configs = configCarryOver ?? new ConfigCarryOver();
    private readonly ModConfigOptionsStore _options = configOptions ?? new ModConfigOptionsStore();
    private readonly ConfigUpdateLog _configLog = configUpdateLog ?? new ConfigUpdateLog();

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

    //
    // The blocking processes running OUT OF THIS INSTALL, or an empty list when it is safe to modify.
    //
    // Scoped to the install on purpose. More than one SPT lives on a machine as soon as anyone runs
    // a second version or keeps a dedicated server apart from the copy they play - and the two share
    // nothing but the name of an executable. A server running from D:\ holds no handle anywhere in
    // an install on E:\, so refusing to touch E:\ because of it blocks work that was never at risk,
    // with a message telling the user to close the one thing they cannot close: the server they are
    // modding the other install FOR.
    //
    // Passing no path keeps the old machine-wide behaviour, for callers that genuinely have no
    // install in hand.
    //
    public static IReadOnlyList<string> RunningBlockers(string? installPath = null)
    {
        var running = new List<string>();

        foreach (var name in BlockingProcessNames)
        {
            Process[] found;

            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch (InvalidOperationException)
            {
                // Process list unavailable - treated as nothing running rather than blocking the user.
                continue;
            }

            try
            {
                if (found.Any(p => BlocksInstall(p, installPath))) running.Add(name + ".exe");
            }
            finally
            {
                foreach (var process in found) process.Dispose();
            }
        }

        return running;
    }

    //
    // Whether one running process is a reason not to touch this install.
    //
    // An unreadable path counts as blocking. Windows refuses MainModule for a process this one has
    // no right to inspect - another user's, or an elevated one - and "I could not tell" is not
    // "it is fine": guessing wrong the other way corrupts an install mid-update, which is the exact
    // thing this guard exists to prevent. The old behaviour was to block on every match, so this
    // costs nothing that was ever available.
    //
    private static bool BlocksInstall(Process process, string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return true;

        string? executable;

        try
        {
            executable = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            AppLog.Debug("Install",
                $"could not read the path of {process.ProcessName}; treating it as in use");
            return true;
        }

        return executable is null || IsInside(executable, installPath);
    }

    //
    // Whether a file sits inside a folder.
    //
    // Compared as full paths with a trailing separator, so "E:\SPT Server" is not read as containing
    // "E:\SPT Server 4.1\...". Case-insensitively, which is right on Windows and near enough
    // everywhere this runs.
    //
    internal static bool IsInside(string filePath, string folder)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;

            return Path.GetFullPath(filePath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path neither side can resolve is not one this can reason about; the caller treats
            // that as still blocking.
            return true;
        }
    }

    //
    // Throws when a blocking process is running, carrying what to close and which operation was
    // refused. It takes the operation rather than a verb phrase: the caller says what it was doing,
    // App/Services/ModInstallProblems says it in English.
    //
    public static void EnsureInstallNotInUse(ModInstallAction action, string? installPath = null)
    {
        var running = RunningBlockers(installPath);
        if (running.Count == 0) return;

        throw new ModInstallException(ModInstallFailure.InstallInUse)
        {
            Running = running,
            Action = action,
        };
    }

    // Downloads and installs <paramref name="version"/> of <paramref name="target"/> into
    // <paramref name="installPath"/>. If a record already exists for this target (an update), its
    // old files are removed once the new archive has downloaded and extracted successfully.
    // Cancellation is honoured up to the point the old version is removed; once files
    // start being placed into the install the operation runs to completion.
    //
    // A mod and an addon are installed by exactly the same path: an addon's archive is an ordinary
    // SPT mod package, and its download link and size come from the same fields.
    //
    // The result carries the record plus what the update did to the mod's own config files - see
    // ConfigCarryOver. Null configs means there were none to have an opinion about.
    public async Task<ModInstallResult> InstallAsync(
        InstallTarget target,
        ModVersion version,
        string installPath,
        IProgress<ModInstallProgress>? status = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            throw new ModInstallException(ModInstallFailure.NoInstallFolder);

        if (string.IsNullOrWhiteSpace(version.Link))
            throw new ModInstallException(ModInstallFailure.NoDownloadLink)
            {
                ModName = target.Name,
                Version = version.Version,
            };

        EnsureInstallNotInUse(ModInstallAction.Install, installPath);

        AppLog.Info("Install",
            $"{target.Name} {version.Version} ({(target.IsAddon ? "addon" : "mod")} {target.Id}) -> {installPath}");

        var workDir = CreateWorkDirectory(installPath, out var canMoveIntoInstall);
        AppLog.Debug("Install", $"work dir {workDir} (move into install: {canMoveIntoInstall})");
        var archivePath = Path.Combine(workDir, "download.bin");
        var extractDir = Path.Combine(workDir, "extracted");

        try
        {
            ct.ThrowIfCancellationRequested();

            status?.Report(new ModInstallProgress(
                ModInstallStage.Downloading, target.Name, version.Version));
            await downloadService.DownloadAsync(version.Link, archivePath, downloadProgress, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            var archiveBytes = new FileInfo(archivePath).Length;
            AppLog.Debug("Install", $"downloaded {archiveBytes:N0} bytes, zip={ArchiveLayout.IsZipArchive(archivePath)}");

            status?.Report(new ModInstallProgress(ModInstallStage.Extracting));
            // Auto-detects archive format from the file header rather than assuming zip.
            var extractTimer = System.Diagnostics.Stopwatch.StartNew();
            await ExtractArchiveAsync(archivePath, extractDir, status, ct).ConfigureAwait(false);
            AppLog.Debug("Install", $"extracted in {extractTimer.ElapsedMilliseconds}ms");

            //
            // Neither extractor is trusted to have refused links (SharpCompress 0.50.4 writes 7z links
            // as plain files; RAR was never tested), so the tree is checked before anything in the
            // install is touched (D16).
            //
            if (InstallPathGuard.FirstLink(extractDir) is { } link)
            {
                AppLog.Warn("Install", $"{target.Name} {version.Version} archive holds a link: {link}");

                throw new ModInstallException(ModInstallFailure.ArchiveContainsLink)
                {
                    ModName = target.Name,
                    Version = version.Version,
                    ArchiveEntry = link,
                };
            }

            ct.ThrowIfCancellationRequested();

            var contentRoot = ArchiveLayout.FindContentRoot(extractDir);
            var topLevelNames = Directory.GetFileSystemEntries(contentRoot).Select(Path.GetFileName);
            if (!topLevelNames.Any(ArchiveLayout.IsKnownRoot))
            {
                AppLog.Warn("Install",
                    $"{target.Name} {version.Version} archive has no known root folder; top level: " +
                    string.Join(", ", Directory.GetFileSystemEntries(contentRoot).Select(Path.GetFileName)));

                throw new ModInstallException(ModInstallFailure.UnrecognisedArchive)
                {
                    ModName = target.Name,
                    Version = version.Version,
                };
            }

            // Archives package server-side content as "user/..."; remap it to wherever this install
            // actually keeps user/mods (e.g. nested under "SPT" or "SPT_Runtime"). BepInEx stays at
            // the install root. Falls back to no remapping if the server exe can't be found.
            SptInstallationService.TryGetServerRoot(installPath, out var serverRoot);

            var sourceFiles = Directory.GetFiles(contentRoot, "*", SearchOption.AllDirectories);

            ct.ThrowIfCancellationRequested();

            // Re-checked now the download is finished: SPT may have been started while it ran, and
            // everything past this point deletes or places files inside the install.
            EnsureInstallNotInUse(ModInstallAction.Install, installPath);

            var manifest = manifestService.Load();
            var existing = manifest.Mods.FirstOrDefault(target.Matches);

            //
            // Where each source file is going, worked out before anything is removed: the config
            // files the archive is about to place over have to be known while they are still there.
            //
            var allPlacements = sourceFiles
                .Select(file =>
                {
                    var installRelative = ArchiveLayout.RemapForServerRoot(Path.GetRelativePath(contentRoot, file), serverRoot);
                    // Forward-slash regardless of OS, matching InstalledModRecord.Files's documented format.
                    return (File: file, Relative: installRelative, Forward: installRelative.Replace('\\', '/'));
                })
                .ToList();

            //
            // Every destination is judged before anything in the install is removed or placed (D3, D7).
            // SPT's, BepInEx's and the game's own files - and this app's own folder - are skipped and
            // never recorded, so nothing can later remove them. A destination reached through a link
            // would write into whatever the link points at, so the whole install is refused instead.
            //
            var skippedProtected = new List<string>();
            var placements = new List<(string File, string Relative, string Forward)>(allPlacements.Count);

            foreach (var placement in allPlacements)
            {
                switch (InstallPathGuard.CheckRecordedPath(installPath, placement.Forward, out _))
                {
                    case null:
                        placements.Add(placement);
                        break;

                    case PathRefusal.Protected or PathRefusal.AppFolder:
                        skippedProtected.Add(placement.Forward);
                        AppLog.Warn("Install", $"{target.Name} {version.Version}: kept the install's own {placement.Forward}; the archive's copy was not placed");
                        break;

                    case PathRefusal.Link:
                        throw new ModInstallException(ModInstallFailure.InstallThroughLink)
                        {
                            ModName = target.Name,
                            Version = version.Version,
                            Folder = placement.Forward,
                        };

                    default:
                        throw new ModInstallException(ModInstallFailure.UnsafeArchiveEntry) { ArchiveEntry = placement.Forward };
                }
            }

            var timestamp = DateTimeOffset.UtcNow;

            var pending = _configs.Prepare(
                installPath, existing, placements.Select(p => p.Forward), target.Name, timestamp);

            //
            // Files this install will place over that no record owns - a hand install's, or a game file
            // outside the protected set - are copied into Data before anything is touched (D22). One
            // that can't be copied stops the install here, while the install is still as it was.
            //
            var overwrote = KeepOriginals(installPath, target, version, existing, manifest, placements, pending, timestamp);

            if (existing is not null)
            {
                status?.Report(new ModInstallProgress(
                    ModInstallStage.RemovingPrevious, Version: existing.Version));

                //
                // Preserve, not Keep: Prepare has already copied every config aside, and moving them
                // again from here would leave the archive holding two copies of the same file. What
                // Preserve adds over Delete is the user's own documents - a mod's presets are not
                // reinstalled, so the removal half of an update must not take them out either.
                //
                // The files Prepare could not copy are named separately and left exactly as they are.
                //
                RemoveRecordedFiles(
                    installPath,
                    existing,
                    ConfigAction.Preserve,
                    pending.Protected.ToHashSet(StringComparer.OrdinalIgnoreCase),
                    CancellationToken.None);
            }

            status?.Report(new ModInstallProgress(
                ModInstallStage.Installing, Total: placements.Count));
            var placedFiles = new List<string>(placements.Count);
            var reportClock = Stopwatch.StartNew();

            try
            {
                for (var i = 0; i < placements.Count; i++)
                {
                    var (file, installRelative, installRelativeForward) = placements[i];

                    //
                    // Two kinds of file the archive does not get to place: a config that could not be
                    // copied aside, and one of the user's own documents that is already there. Both
                    // keep the version on disk, and both are still recorded as this install's files so
                    // a later removal knows about them.
                    //
                    if (pending.Untouchable.Contains(installRelativeForward)
                        || pending.Preserved.Contains(installRelativeForward))
                    {
                        placedFiles.Add(installRelativeForward);
                        continue;
                    }

                    var destination = Path.Combine(installPath, installRelative);
                    var destinationDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

                    if (canMoveIntoInstall) File.Move(file, destination, overwrite: true);
                    else File.Copy(file, destination, overwrite: true);

                    placedFiles.Add(installRelativeForward);

                    if (reportClock.Elapsed >= ProgressInterval)
                    {
                        status?.Report(new ModInstallProgress(
                            ModInstallStage.Installing, Done: i + 1, Total: placements.Count));
                        reportClock.Restart();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // What was placed before the failure is recorded anyway, so those files stay
                // app-managed: a retry overwrites them and a removal cleans them up. Without this an
                // interrupted update leaves the old version deleted and the new one untracked. The
                // originals kept so far are recorded too - they are owed back whatever happens next.
                SaveRecord(target, version, placedFiles, incomplete: true, installPath, overwrote,
                    Fingerprint(installPath, placedFiles));

                AppLog.Error("Install",
                    $"{target.Name} {version.Version} incomplete after {placedFiles.Count}/{placements.Count} file(s)", ex);

                throw new ModInstallException(ModInstallFailure.PartlyInstalled, ex)
                {
                    ModName = target.Name,
                    Version = version.Version,
                    PlacedFiles = placedFiles.Count,
                    TotalFiles = placements.Count,
                };
            }

            var record = SaveRecord(target, version, placedFiles, incomplete: false, installPath, overwrote, fingerprints: []);

            AppLog.Info("Install",
                $"{target.Name} {version.Version} placed {placedFiles.Count} file(s) in folders [{string.Join(", ", record.Folders)}]"
                + (skippedProtected.Count > 0 ? $"; kept {skippedProtected.Count} of the install's own file(s)" : "")
                + (overwrote.Count > 0 ? $"; kept {overwrote.Count} original(s) it replaced" : ""));

            var report = _configs.Settle(pending, installPath, target, existing, record, timestamp);

            if (report.Files.Count > 0)
            {
                _configLog.Add(report);
                AppLog.Info("Configs",
                    $"{target.Name} {record.Version}: " +
                    string.Join(", ", report.Files.Select(f => $"{f.Path} {f.Kind}{(f.Reason is { } r ? $" ({r})" : "")}")));
            }

            //
            // Fingerprinted last, after Settle has merged or restored configs, so what is recorded is
            // what is actually on disk now (D21).
            //
            var fingerprintClock = Stopwatch.StartNew();
            record = SaveRecord(target, version, placedFiles, incomplete: false, installPath, overwrote,
                Fingerprint(installPath, placedFiles));
            AppLog.Debug("Install", $"fingerprinted {record.Fingerprints.Count} file(s) in {fingerprintClock.ElapsedMilliseconds}ms");

            status?.Report(new ModInstallProgress(ModInstallStage.Done));
            return new ModInstallResult(record, report.Files.Count > 0 ? report : null, skippedProtected);
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("Install", $"{target.Name} {version.Version} cancelled");
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("Install", $"{target.Name} {version.Version} failed", ex);
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
    private InstalledModRecord SaveRecord(
        InstallTarget target,
        ModVersion version,
        List<string> placedFiles,
        bool incomplete,
        string installPath,
        List<OverwrittenFile> overwrote,
        List<FileFingerprint> fingerprints)
    {
        var record = new InstalledModRecord
        {
            ModId = target.Id,
            IsAddon = target.IsAddon,
            Guid = target.Guid,
            Name = target.Name,
            VersionId = version.Id,
            Version = version.Version ?? "unknown",
            InstalledAt = DateTimeOffset.UtcNow,
            Files = placedFiles,
            Folders = InstalledModFolders.FromPlacedFiles(placedFiles),
            Incomplete = incomplete,
            Fingerprints = fingerprints,
            InstallPath = InstallStamp.Of(installPath),
            Overwrote = overwrote,
        };

        var current = manifestService.Load();
        current.Mods.RemoveAll(target.Matches);
        current.Mods.Add(record);
        manifestService.Save(current);

        return record;
    }

    //
    // The size and SHA-256 of every recorded file still on disk. A file that can't be read gets no
    // fingerprint, and removal falls back to the path checks for it - never a guessed one.
    //
    private static List<FileFingerprint> Fingerprint(string installPath, IEnumerable<string> recorded)
    {
        var prints = new List<FileFingerprint>();

        foreach (var path in recorded)
        {
            if (InstallPathGuard.CheckRecordedPath(installPath, path, out var full) is null
                && FileFingerprint.Compute(full, path) is { } print)
            {
                prints.Add(print);
            }
        }

        return prints;
    }

    //
    // Copies every file this install is about to place over, that no record owns, into
    // Data\overwritten\<mod>\<time>\<path> and returns them, together with the originals an earlier
    // version of this mod already kept (still owed back). Skipped: files another record lists (that's
    // shared-file ownership, D9/D10), files the earlier version of this mod placed (its own old copy,
    // removed by the update), configs Prepare already copied aside, files that keep their version on
    // disk, and anything already kept by an earlier install of this mod.
    //
    private static List<OverwrittenFile> KeepOriginals(
        string installPath,
        InstallTarget target,
        ModVersion version,
        InstalledModRecord? existing,
        ModInstallManifest manifest,
        IReadOnlyList<(string File, string Relative, string Forward)> placements,
        PendingConfigs pending,
        DateTimeOffset timestamp)
    {
        var kept = new List<OverwrittenFile>(existing?.Overwrote ?? []);
        var alreadyKept = kept.Select(k => k.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var owned = manifest.Mods
            .SelectMany(m => m.Files)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ownFolders = InstalledModFolders.FromPlacedFiles(placements.Select(p => p.Forward))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var key = target.IsAddon ? $"{target.Id}-addon" : target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var folder = Path.Combine(OverwrittenDirectoryName, key, $"{timestamp.ToLocalTime():yyyyMMdd-HHmmss}");

        foreach (var (_, relative, forward) in placements)
        {
            if (owned.Contains(forward) || alreadyKept.Contains(forward)) continue;
            if (pending.Archived.ContainsKey(forward)) continue;
            if (pending.Untouchable.Contains(forward) || pending.Preserved.Contains(forward)) continue;

            var destination = Path.Combine(installPath, relative);
            if (!File.Exists(destination)) continue;

            var backupRelative = Path.Combine(folder, relative);
            var backup = Path.Combine(AppPaths.DataDirectory, backupRelative);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, overwrite: false);

                var print = FileFingerprint.Compute(backup, forward)
                    ?? throw new IOException($"couldn't read back {backup}");

                var inOwnFolder = InstalledModFolders.FromPlacedFiles([forward]) is [var name] && ownFolders.Contains(name);

                kept.Add(new OverwrittenFile(forward, print.Size, print.Sha256, backupRelative.Replace('\\', '/'), inOwnFolder));
                alreadyKept.Add(forward);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("Install", $"{target.Name} {version.Version}: couldn't keep the original {forward} before replacing it", ex);

                throw new ModInstallException(ModInstallFailure.OriginalNotKept, ex)
                {
                    ModName = target.Name,
                    Version = version.Version,
                    Folder = forward,
                };
            }
        }

        if (kept.Count > (existing?.Overwrote.Count ?? 0))
            AppLog.Info("Install", $"{target.Name} {version.Version}: kept {kept.Count - (existing?.Overwrote.Count ?? 0)} original file(s) in {folder}");

        return kept;
    }

    // Under the Data folder: where the originals an install replaced are kept (D22).
    public const string OverwrittenDirectoryName = "overwritten";

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
        EnsureInstallNotInUse(ModInstallAction.Remove, installPath);

        var result = RemoveRecordedFiles(installPath, record, configs, null, ct);

        // A mod that is gone has no shipped copies worth keeping. An update does not come through
        // here, which is why this is safe to do unconditionally - see InstallAsync.
        _configs.Baselines.Remove(record.ModId, record.IsAddon);

        return Task.FromResult(result);
    }

    //
    // The removal itself, shared by a real uninstall and the update path.
    //
    // <paramref name="keep"/> names files this must not touch whatever else it is told - the configs
    // ConfigCarryOver could not copy aside. Null on the removal path, which has nothing to protect.
    //
    private UninstallResult RemoveRecordedFiles(
        string installPath,
        InstalledModRecord record,
        ConfigAction configs,
        IReadOnlySet<string>? keep,
        CancellationToken ct)
    {
        var failed = new List<string>();
        var refused = new List<string>();
        var deleted = 0;
        var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Moved out before the delete loop runs, so the loop simply finds them gone.
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

        foreach (var relative in record.Files)
        {
            ct.ThrowIfCancellationRequested();

            if (keep is not null && keep.Contains(relative)) continue;
            if (preserve.Contains(relative)) continue;

            //
            // A record is only as trustworthy as whatever last wrote it - the Data files page edits
            // it by hand - so every path is checked against the disk as it is now before anything is
            // deleted (D13). A refused path is left exactly as it is and reported.
            //
            if (InstallPathGuard.CheckRecordedPath(installPath, relative, out var fullPath) is { } refusal)
            {
                refused.Add(relative);
                AppLog.Warn("Remove", $"{record.Name}: left {relative} in place ({refusal})");
                continue;
            }

            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    deleted++;
                }

                // Only a mod's own folder and what's below it are ever tidied away (D14).
                for (var dir = Path.GetDirectoryName(fullPath);
                     dir is not null && InstallPathGuard.MayRemoveEmptyFolder(installPath, dir);
                     dir = Path.GetDirectoryName(dir))
                {
                    touchedDirectories.Add(dir);
                }
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

        var manifest = manifestService.Load();
        manifest.Mods.RemoveAll(m => m.ModId == record.ModId && m.IsAddon == record.IsAddon);
        manifestService.Save(manifest);

        return new UninstallResult(deleted, failed, kept.Count, kept.Folder, refused);
    }

    // Deletes a mod's whole folder (or, for a loose top-level DLL, just that file) - the
    // removal path for a mod this app didn't install itself, since there's no per-file manifest
    // record to work from. Callers should confirm the exact path with the user before calling this.
    //
    // Refused, before anything is touched, unless the path is a mod folder directly inside one of
    // this install's mod containers, isn't SPT's, and is neither a link nor holds one (D15).
    // Callers check every path with InstallPathGuard.CheckModFolder first, so a refusal here is the
    // second line, not the first.
    public static void RemoveLegacyPath(string path, string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            throw new ModInstallException(ModInstallFailure.NoInstallFolder);

        EnsureInstallNotInUse(ModInstallAction.Remove, installPath);

        if (InstallPathGuard.CheckModFolder(installPath, path) is { } refusal)
        {
            AppLog.Warn("Remove", $"refused to remove {path} ({refusal})");
            throw new ModInstallException(ModInstallFailure.RemovalRefused) { Folder = path, Refusal = refusal };
        }

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

    // Extracts every file entry in the archive at <paramref name="archivePath"/> into
    // <paramref name="extractDir"/>. Zip archives go through System.IO.Compression; every other
    // format goes through SharpCompress's forward-only reader. Zip-slip protection: any entry whose
    // resolved destination would land outside extractDir is rejected before anything is written.
    private static async Task ExtractArchiveAsync(
        string archivePath,
        string extractDir,
        IProgress<ModInstallProgress>? status,
        CancellationToken ct)
    {
        Directory.CreateDirectory(extractDir);
        var extractRoot = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        if (ArchiveLayout.IsZipArchive(archivePath))
        {
            await ExtractZipAsync(archivePath, extractDir, extractRoot, status, ct).ConfigureAwait(false);
            return;
        }

        ExtractWithSharpCompress(archivePath, extractDir, extractRoot, status, ct);
    }

    private static async Task ExtractZipAsync(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<ModInstallProgress>? status,
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
                status?.Report(new ModInstallProgress(
                    ModInstallStage.Extracting, Done: extracted, Total: entries.Count));
                reportClock.Restart();
            }
        }
    }

    private static void ExtractWithSharpCompress(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<ModInstallProgress>? status,
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
                status?.Report(new ModInstallProgress(
                    ModInstallStage.Extracting, Done: extracted, Total: total));
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
            throw new ModInstallException(ModInstallFailure.UnsafeArchiveEntry) { ArchiveEntry = entryKey };
        }

        var destinationDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

        return destination;
    }
}

//
// What an install placed, and what it did to the mod's own config files.
//
// Configs is null when the mod has none - most client-only mods, and anything whose settings live in
// BepInEx\config rather than inside its own folder.
//
//
// SkippedProtected lists the archive's files that were not placed because the install's own copy is
// SPT's, BepInEx's or the game's (D3-D5).
//
public sealed record ModInstallResult(
    InstalledModRecord Record,
    ConfigUpdateReport? Configs,
    IReadOnlyList<string>? SkippedProtected = null);

// Result of ModInstallService.UninstallAsync. FailedFiles lists files that couldn't be
// deleted; the mod is still removed from the manifest regardless. ConfigsKept/ConfigsFolder
// describe the mod's own config files when they were moved out rather than deleted.
// RefusedFiles lists recorded paths that failed InstallPathGuard's checks and were left untouched.
public sealed record UninstallResult(
    int FilesDeleted,
    List<string> FailedFiles,
    int ConfigsKept = 0,
    string? ConfigsFolder = null,
    List<string>? RefusedFiles = null);

// What to do with a mod's own config and user-data files when its files are being removed.
public enum ConfigAction
{
    // Move them into AppPaths.LegacyConfigsDirectory instead of deleting them. A real removal.
    Keep,

    // Delete them along with the rest of the mod's files.
    Delete,

    //
    // Delete the configs but leave the user's own documents where they are - the update path, which
    // has already copied the configs aside and is about to place new ones over them.
    //
    // Update and removal want opposite things here, which is why this is its own member rather than a
    // flag: on an update a preset folder stays exactly as it is, and on a removal it is rescued.
    //
    Preserve,
}
