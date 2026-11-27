using System.Diagnostics;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed partial class ModInstallService
{
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
    //
    // OPEN-12 F7 - <paramref name="downloadedArchive"/>: the version's archive, already downloaded
    // (the queue downloads up to three ahead of the one installing). Read where it is - not moved or
    // deleted, that is the caller's - and nothing is downloaded.
    public async Task<ModInstallResult> InstallAsync(
        InstallTarget target,
        ModVersion version,
        string installPath,
        IProgress<ModInstallProgress>? status = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken ct = default,
        string? downloadedArchive = null)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            throw new ModInstallException(ModInstallFailure.NoInstallFolder);

        if (downloadedArchive is null && string.IsNullOrWhiteSpace(version.Link))
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
        var archivePath = downloadedArchive ?? Path.Combine(workDir, "download.bin");
        var extractDir = Path.Combine(workDir, "extracted");
        InstallJournalEntry? journalEntry = null;
        var broughtBack = BroughtBack.None;

        try
        {
            ct.ThrowIfCancellationRequested();

            if (downloadedArchive is null)
            {
                status?.Report(new ModInstallProgress(
                    ModInstallStage.Downloading, target.Name, version.Version));
                await downloadService.DownloadAsync(version.Link!, archivePath, downloadProgress, ct).ConfigureAwait(false);
            }
            else
            {
                // Off the caller's thread for the rest, as the download's ConfigureAwait(false) would
                // have put it - the queue calls this from the UI thread, and extracting and placing
                // would otherwise freeze the window for the whole install.
                await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            }

            ct.ThrowIfCancellationRequested();

            var archiveBytes = new FileInfo(archivePath).Length;
            AppLog.Debug("Install", $"downloaded {archiveBytes:N0} bytes, zip={ArchiveLayout.IsZipArchive(archivePath)}");

            status?.Report(new ModInstallProgress(ModInstallStage.Extracting));
            // Auto-detects archive format from the file header rather than assuming zip.
            var extractTimer = System.Diagnostics.Stopwatch.StartNew();
            var folderEntries = new List<string>();
            await ExtractArchiveAsync(archivePath, extractDir, status, ct, folderEntries).ConfigureAwait(false);
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
            var topFolders = Directory.GetDirectories(contentRoot).Select(d => Path.GetFileName(d)!).ToList();
            var topFiles = Directory.GetFiles(contentRoot).Select(f => Path.GetFileName(f)!).ToList();
            if (!ArchiveLayout.IsRecognised(topFolders, topFiles))
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

            // plugins\ and patchers\ without BepInEx\ around them go under BepInEx\.
            var bareBepInEx = ArchiveLayout.IsBareBepInEx(topFolders, topFiles);
            if (bareBepInEx) AppLog.Info("Install", $"{target.Name} {version.Version}: plugins/patchers packed without BepInEx; placed under BepInEx");

            ct.ThrowIfCancellationRequested();

            // Re-checked now the download is finished: SPT may have been started while it ran, and
            // everything past this point deletes or places files inside the install.
            EnsureInstallNotInUse(ModInstallAction.Install, installPath);
            RecoverInterruptedInstalls(installPath);

            var manifest = manifestService.Load();
            var existing = manifest.Mods.FirstOrDefault(target.Matches);

            if (existing is not null)
            {
                EnsureRecordBelongsHere(existing, installPath);
                EnsureNotOnDiskTwice(existing, installPath, ModInstallAction.Install);
            }

            //
            // Where each source file is going, worked out before anything is removed: the config
            // files the archive is about to place over have to be known while they are still there.
            //
            // Read-me files, licences and pictures at the top of the content are left out (they would
            // land in the SPT folder itself), logged so the log shows why the archive had more files.
            var allPlacements = new List<(string File, string Relative, string Forward)>(sourceFiles.Length);
            var leftBeside = new List<string>();

            foreach (var file in sourceFiles)
            {
                var contentRelative = Path.GetRelativePath(contentRoot, file).Replace('\\', '/');
                if (ArchiveLayout.Place(contentRelative, bareBepInEx) is not { } placed)
                {
                    leftBeside.Add(contentRelative);
                    continue;
                }

                var installRelative = ArchiveLayout.RemapForServerRoot(placed.Replace('/', Path.DirectorySeparatorChar), serverRoot);
                // Forward-slash regardless of OS, matching InstalledModRecord.Files's documented format.
                allPlacements.Add((file, installRelative, installRelative.Replace('\\', '/')));
            }

            if (leftBeside.Count > 0)
                AppLog.Info("Install", $"{target.Name} {version.Version}: not placed (beside the content): {string.Join(", ", leftBeside)}");

            //
            // Every destination is judged before anything in the install is removed or placed (D3, D7).
            // SPT's, BepInEx's and the game's own files - and this app's own folder - are skipped and
            // never recorded, so nothing can later remove them. A destination reached through a link
            // would write into whatever the link points at, so the whole install is refused instead.
            //
            var skippedProtected = new List<string>();
            var skippedAppFolder = new List<string>();
            var placements = new List<(string File, string Relative, string Forward)>(allPlacements.Count);

            foreach (var placement in allPlacements)
            {
                var refusal = InstallPathGuard.CheckPlacedPath(installPath, placement.Forward, out var destinationPath);

                //
                // In a new-file-only area (R17) the archive's file goes in only where nothing is there yet -
                // or where what is there is the previous version's own copy, proven by its fingerprint,
                // which the removal half below takes into holding first.
                //
                if (refusal is null
                    && ProtectedInstallPaths.IsNewFileOnly(placement.Forward)
                    && (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                    && !(existing is not null && InstallPathGuard.ProvenPlacedFile(installPath, existing, placement.Forward, out _)))
                {
                    refusal = PathRefusal.Protected;
                }

                switch (refusal)
                {
                    case null:
                        placements.Add(placement);
                        break;

                    case PathRefusal.Protected:
                        skippedProtected.Add(placement.Forward);
                        AppLog.Warn("Install", $"{target.Name} {version.Version}: kept the install's own {placement.Forward}; the archive's copy was not placed");
                        break;

                    case PathRefusal.AppFolder:
                        skippedAppFolder.Add(placement.Forward);
                        AppLog.Warn("Install", $"{target.Name} {version.Version}: {placement.Forward} is part of this app; the archive's copy was not placed");
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

            //
            // Every file in the archive was refused. Going on would record an install of nothing and,
            // on an update, swap the previous version's record for an empty one - leaving its files
            // on disk with nothing tracking them - so it stops here, before anything is touched.
            //
            if (placements.Count == 0 && skippedProtected.Count + skippedAppFolder.Count > 0)
            {
                AppLog.Warn("Install", $"{target.Name} {version.Version}: every file in the archive was refused; nothing placed, nothing changed");

                throw new ModInstallException(ModInstallFailure.NothingToPlace)
                {
                    ModName = target.Name,
                    Version = version.Version,
                    TotalFiles = skippedProtected.Count + skippedAppFolder.Count,
                    ArchiveEntry = skippedProtected.Concat(skippedAppFolder).First(),
                };
            }

            //
            // A copy of the SPT profiles from before the change, when they changed since the last one
            // (OPEN-12 F4). Past every refusal above, so an install that is refused takes none.
            //
            profileBackups?.BackupIfChanged(installPath, ProfileBackups.BeforeInstall);

            var timestamp = DateTimeOffset.UtcNow;

            //
            // A previous version with files in ".disabled" places is brought back first, so the update
            // replaces it rather than placing a second, live copy (OPEN-46 Q6, OPEN-50). A half set
            // aside stays back: a mod is updated whole (OPEN-44 Q14, OPEN-50 Q4). A mod that was wholly
            // disabled is set aside again once the update is in. A failed install sets back what came
            // back, leaving the install as it was.
            //
            var liveBefore = placements
                .Select(p => InstallPathGuard.ModFolderOf(p.Forward))
                .OfType<string>()
                .Where(f => Exists(Path.Combine(installPath, f.Replace('/', Path.DirectorySeparatorChar))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (existing is not null) broughtBack = BringBackDisabled(installPath, existing, target, version);

            var pending = _configs.Prepare(
                installPath, existing, placements.Select(p => p.Forward), target.Name, timestamp);

            //
            // Files this install will place over that no record owns - a hand install's, or a game file
            // outside the protected set - are copied into Data before anything is touched (D22). One
            // that can't be copied stops the install here, while the install is still as it was.
            //
            var overwrote = KeepOriginals(installPath, target, version, existing, manifest, placements, pending, timestamp);

            //
            // The last moment the install is still exactly as it was: from here the previous version's
            // record goes and the new files land. Noted first, so a process that ends part way leaves
            // nothing untracked - see InstallJournal (OPEN-12 F1).
            //
            journalEntry = new InstallJournalEntry
            {
                InstallPath = InstallStamp.Of(installPath),
                ModId = target.Id,
                IsAddon = target.IsAddon,
                Guid = target.Guid,
                Name = target.Name,
                VersionId = version.Id,
                Version = version.Version ?? "unknown",
                StartedAt = timestamp,
                Planned = [.. placements.Select(p => p.Forward)],
                Overwrote = [.. overwrote],
                ConfigsFolder = pending.ArchiveFolder,
            };
            _journal.Write(journalEntry);

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
                    placements.Select(p => p.Forward).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    RemovalKind.ReplacedByUpdate,
                    CancellationToken.None);
            }

            status?.Report(new ModInstallProgress(
                ModInstallStage.Installing, Total: placements.Count));
            var placedFiles = new List<string>(placements.Count);
            var createdFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var claimedFolders = manifestService.Load().Mods
                .Where(m => !target.Matches(m))
                .SelectMany(m => m.CreatedFolders)
                .Concat(existing?.CreatedFolders ?? [])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keptSettings = new List<string>();
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
                    // The user's plugin settings already in BepInEx\config - see ConfigCarryOver.
                    if (pending.KeptSettings.Contains(installRelativeForward))
                    {
                        keptSettings.Add(installRelativeForward);
                        if (existing?.Files.Contains(installRelativeForward, StringComparer.OrdinalIgnoreCase) == true)
                            placedFiles.Add(installRelativeForward);
                        continue;
                    }

                    if (pending.Untouchable.Contains(installRelativeForward)
                        || pending.Preserved.Contains(installRelativeForward))
                    {
                        placedFiles.Add(installRelativeForward);
                        continue;
                    }

                    var destination = Path.Combine(installPath, installRelative);

                    // Checked again at the moment of placing: a new-file-only path (R17) is never
                    // written over, whatever was decided before the previous version was removed.
                    if (ProtectedInstallPaths.IsNewFileOnly(installRelativeForward)
                        && (File.Exists(destination) || Directory.Exists(destination)))
                    {
                        skippedProtected.Add(installRelativeForward);
                        AppLog.Warn("Install", $"{target.Name} {version.Version}: {installRelativeForward} appeared before it was placed; left as it is");
                        continue;
                    }

                    var destinationDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        NoteCreatedFolders(installPath, destinationDir, createdFolders, claimedFolders);
                        Directory.CreateDirectory(destinationDir);
                    }

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
                    KeepSettingsPrints(Fingerprint(installPath, placedFiles), pending, existing), createdFolders: createdFolders);
                _journal.Delete(journalEntry);

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

            var emptyFolders = PlaceEmptyFolders(folderEntries, contentRoot, bareBepInEx, serverRoot, installPath, existing, target, version);

            var record = SaveRecord(target, version, placedFiles, incomplete: false, installPath, overwrote, fingerprints: [], emptyFolders, createdFolders);

            AppLog.Info("Install",
                $"{target.Name} {version.Version} placed {placedFiles.Count} file(s) in folders [{string.Join(", ", record.Folders)}]"
                + (skippedProtected.Count > 0 ? $"; kept {skippedProtected.Count} of the install's own file(s)" : "")
                + (skippedAppFolder.Count > 0 ? $"; left out {skippedAppFolder.Count} file(s) that are part of this app" : "")
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
                KeepSettingsPrints(Fingerprint(installPath, placedFiles), pending, existing), emptyFolders, createdFolders);
            _journal.Delete(journalEntry);
            AppLog.Debug("Install", $"fingerprinted {record.Fingerprints.Count} file(s) in {fingerprintClock.ElapsedMilliseconds}ms");

            bool? setAsideAgain = broughtBack.Whole
                ? SetAsideAfterUpdate(installPath, record, broughtBack, liveBefore, target, version)
                : null;

            status?.Report(new ModInstallProgress(ModInstallStage.Done));
            if (keptSettings.Count > 0)
                AppLog.Info("Install", $"{target.Name} {version.Version}: kept the settings already in {string.Join(", ", keptSettings)}");

            return new ModInstallResult(record, report.Files.Count > 0 ? report : null, skippedProtected, skippedAppFolder)
            {
                OriginalsKept = overwrote.Count - (existing?.Overwrote.Count ?? 0),
                KeptSettings = keptSettings,
                SetAsideAgain = setAsideAgain,
            };
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("Install", $"{target.Name} {version.Version} cancelled");
            if (journalEntry is null && broughtBack.Any) PutBackAside(installPath, broughtBack, target, version);
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("Install", $"{target.Name} {version.Version} failed", ex);
            if (journalEntry is null && broughtBack.Any) PutBackAside(installPath, broughtBack, target, version);
            throw;
        }
        finally
        {
            if (journalEntry is not null) _journal.Release(journalEntry);
            TryDeleteDirectory(workDir);
        }
    }

    //
    // What an update brought back from ".disabled" places before it ran, in their live form: mod folders
    // that were away whole, and files from folders that stayed live (a mod sharing its folder, OPEN-28).
    // Whole is true when none of the record's files in a mod container was live.
    //
    private sealed record BroughtBack(IReadOnlyList<string> Folders, IReadOnlyList<string> Files, bool Whole)
    {
        public static BroughtBack None { get; } = new([], [], false);

        public bool Any => Folders.Count > 0 || Files.Count > 0;
    }

    private static BroughtBack BringBackDisabled(
        string installPath, InstalledModRecord existing, InstallTarget target, ModVersion version)
    {
        var parked = new List<string>();
        var anyLive = false;

        foreach (var file in existing.Files)
        {
            if (!DisabledModPaths.TryGetRelativeCounterpart(file, out var aside) || DisabledModPaths.IsRelativePathDisabled(file))
                continue;

            if (File.Exists(Path.Combine(installPath, file.Replace('/', Path.DirectorySeparatorChar)))) anyLive = true;
            else if (File.Exists(Path.Combine(installPath, aside.Replace('/', Path.DirectorySeparatorChar)))) parked.Add(file);
        }

        if (parked.Count == 0) return BroughtBack.None;

        var folders = parked
            .Select(InstallPathGuard.ModFolderOf)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !Exists(Path.Combine(installPath, f.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        var files = parked
            .Where(f => InstallPathGuard.ModFolderOf(f) is not { } folder || !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var backFolders = ModDisableService.MoveFolders(installPath, folders, disable: false, target.Name);
        var backFiles = backFolders.Failed.Count == 0
            ? ModDisableService.MoveFiles(installPath, files, disable: false, target.Name)
            : ModDisableOutcome.Empty;

        if (backFolders.Failed.Count > 0 || backFiles.Failed.Count > 0)
        {
            if (backFiles.Failed.Count > 0) ModDisableService.MoveFolders(installPath, folders, disable: true, target.Name);

            AppLog.Warn("Install",
                $"{target.Name} {version.Version}: couldn't bring back what was in .disabled before updating " +
                $"({string.Join("; ", backFolders.Failed.Concat(backFiles.Failed).Select(f => f.Reason))}); updating without it");
            return BroughtBack.None;
        }

        AppLog.Info("Install",
            $"{target.Name} {version.Version}: brought back {folders.Count} folder(s) and {files.Count} file(s) from .disabled to update " +
            (anyLive ? "(part was live, so it stays back)" : "(it was disabled, so it is set aside again after)"));

        return new BroughtBack(folders, files, Whole: !anyLive);
    }

    private static void PutBackAside(string installPath, BroughtBack broughtBack, InstallTarget target, ModVersion version)
    {
        var folders = ModDisableService.MoveFolders(installPath, broughtBack.Folders, disable: true, target.Name);
        var files = ModDisableService.MoveFiles(installPath, broughtBack.Files, disable: true, target.Name);

        if (folders.Failed.Count > 0 || files.Failed.Count > 0)
            AppLog.Warn("Install",
                $"{target.Name} {version.Version}: couldn't set back aside what the update brought back " +
                $"({string.Join("; ", folders.Failed.Concat(files.Failed).Select(f => f.Reason))}); it is live");
        else
            AppLog.Info("Install", $"{target.Name} {version.Version}: set back aside what the update brought back");
    }

    //
    // A wholly disabled mod, once updated, goes back into ".disabled" places (OPEN-50): every mod folder
    // that came back whole or that the update made, and the update's own files in folders that stayed
    // live. True when all of it moved.
    //
    private static bool SetAsideAfterUpdate(
        string installPath, InstalledModRecord record, BroughtBack broughtBack, IReadOnlySet<string> liveBefore,
        InstallTarget target, ModVersion version)
    {
        var came = broughtBack.Folders.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var folders = record.Files
            .Select(InstallPathGuard.ModFolderOf)
            .OfType<string>()
            .Concat(broughtBack.Folders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => DisabledModPaths.TryGetRelativeCounterpart(f, out _))
            .Where(f => came.Contains(f) || !liveBefore.Contains(f))
            .Where(f => Exists(Path.Combine(installPath, f.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        var files = record.Files
            .Where(f => DisabledModPaths.TryGetRelativeCounterpart(f, out _))
            .Where(f => InstallPathGuard.ModFolderOf(f) is not { } folder || !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var asideFolders = ModDisableService.MoveFolders(installPath, folders, disable: true, target.Name);
        var asideFiles = ModDisableService.MoveFiles(installPath, files, disable: true, target.Name);

        if (asideFolders.Failed.Count == 0 && asideFiles.Failed.Count == 0)
        {
            AppLog.Info("Install", $"{target.Name} {version.Version}: was disabled; set aside again");
            return true;
        }

        AppLog.Warn("Install",
            $"{target.Name} {version.Version}: was disabled, but couldn't set the update aside again " +
            $"({string.Join("; ", asideFolders.Failed.Concat(asideFiles.Failed).Select(f => f.Reason))}); it is live");
        return false;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    //
    // The folders an archive ships with nothing in them (SVM's Presets\, which its own tool refuses to
    // run without). Neither extractor writes folder entries, so they arrive as the entry list the
    // extraction collected; one that holds anything in the archive exists in extractDir already and is
    // not empty. Created only below a mod's own folder and where the path checks allow. Returned are
    // the ones this install owns - created now, or carried from the previous version's record - so a
    // removal can tidy them away while they are still empty.
    //
    private static void NoteCreatedFolders(string installPath, string directory, HashSet<string> created, HashSet<string> claimed)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));
        var prefix = root + Path.DirectorySeparatorChar;

        for (var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
             dir.Length > prefix.Length && dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
             dir = Path.GetDirectoryName(dir)!)
        {
            var relative = Path.GetRelativePath(root, dir).Replace('\\', '/');
            if (Directory.Exists(dir) && !claimed.Contains(relative)) break;
            created.Add(relative);
        }
    }

    private static List<string> PlaceEmptyFolders(
        IReadOnlyList<string> folderEntries, string contentRoot, bool bareBepInEx, string serverRoot,
        string installPath, InstalledModRecord? existing, InstallTarget target, ModVersion version)
    {
        var owned = new List<string>();
        var leaves = folderEntries
            .Where(f => !Directory.Exists(f) && !folderEntries.Any(o => IsInside(o, f)))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in leaves)
        {
            var relative = Path.GetRelativePath(contentRoot, folder);
            if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) continue;
            if (ArchiveLayout.Place(relative.Replace('\\', '/') + "/", bareBepInEx) is not { } placed) continue;

            var forward = ArchiveLayout.RemapForServerRoot(placed.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar), serverRoot).Replace('\\', '/');
            if (InstallPathGuard.ModFolderOf(forward) is not { } modFolder || modFolder.Length >= forward.Length) continue;
            if (InstallPathGuard.CheckPlacedPath(installPath, forward, out var full) is not null) continue;

            try
            {
                if (!Directory.Exists(full))
                {
                    Directory.CreateDirectory(full);
                    owned.Add(forward);
                }
                else if (existing?.EmptyFolders.Contains(forward, StringComparer.OrdinalIgnoreCase) == true)
                {
                    owned.Add(forward);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Install", $"{target.Name} {version.Version}: couldn't create the archive's empty folder {forward}: {ex.Message}");
            }
        }

        if (owned.Count > 0)
            AppLog.Info("Install", $"{target.Name} {version.Version}: empty folder(s) from the archive: {string.Join(", ", owned)}");

        return owned;
    }
}
