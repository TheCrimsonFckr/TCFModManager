using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed partial class ModInstallService
{
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
        List<FileFingerprint> fingerprints,
        List<string>? emptyFolders = null)
    {
        var current = manifestService.Load();

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
            EmptyFolders = emptyFolders ?? [],
            Slug = target.IsAddon ? target.Slug ?? current.Mods.FirstOrDefault(target.Matches)?.Slug : null,
            FitOverride = target.IsAddon && !string.IsNullOrWhiteSpace(target.FitOverride) ? target.FitOverride.Trim() : null,
        };

        current.Mods.RemoveAll(target.Matches);
        current.Mods.Add(record);
        manifestService.Save(current);

        return record;
    }

    //
    // A kept BepInEx\config file keeps the PREVIOUS version's fingerprint rather than one taken
    // now. Taken now, the user's tuned copy would read as "exactly what this app placed", and the next
    // update would put its defaults over it, a removal take it out. With the old one it still reads as
    // changed since install, and stays; with none (a record from before fingerprints) it stays too.
    //
    private static List<FileFingerprint> KeepSettingsPrints(
        List<FileFingerprint> prints, PendingConfigs pending, InstalledModRecord? existing)
    {
        if (pending.KeptSettings.Count == 0) return prints;

        prints.RemoveAll(p => pending.KeptSettings.Contains(p.Path));
        foreach (var path in pending.KeptSettings)
        {
            if (existing?.FingerprintFor(path) is { } old) prints.Add(old);
        }

        return prints;
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
            // CheckPlacedPath: a file this install just put in a new-file-only area (R17) needs its
            // fingerprint more than any - it is the only thing that lets a removal take it back out.
            if (InstallPathGuard.CheckPlacedPath(installPath, path, out var full) is null
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

        // The GUIDs each affected mod folder declares, read once per folder while it still holds what
        // was there before this install.
        var declared = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

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

                var sameMod = IsEarlierCopyOfSameMod(installPath, target, forward, declared);

                kept.Add(new OverwrittenFile(forward, print.Size, print.Sha256, backupRelative.Replace('\\', '/'), sameMod));
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

    //
    // Whether the mod folder a replaced file sits in declares the GUID of the mod being installed -
    // proof that the file belongs to an earlier copy of the same mod (R16). False whenever that can't be
    // shown: an addon (sp-mod.com gives addons no GUID), a mod with no catalog GUID, a file outside any
    // mod folder, or a folder whose DLLs declare something else or nothing.
    //
    private static bool IsEarlierCopyOfSameMod(
        string installPath, InstallTarget target, string forward, Dictionary<string, IReadOnlySet<string>> declared)
    {
        if (target.IsAddon || string.IsNullOrWhiteSpace(target.Guid)) return false;
        if (InstallPathGuard.ModFolderOf(forward) is not { } folder) return false;

        if (!declared.TryGetValue(folder, out var guids))
        {
            guids = DeclaredGuids(Path.Combine(installPath, folder.Replace('/', Path.DirectorySeparatorChar)));
            declared[folder] = guids;
        }

        return guids.Contains(target.Guid);
    }

    // Every GUID the DLLs in a mod folder (or a loose DLL) declare, client and server alike.
    private static IReadOnlySet<string> DeclaredGuids(string modPath)
    {
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> dlls = File.Exists(modPath)
            ? [modPath]
            : Directory.Exists(modPath)
                ? Directory.EnumerateFiles(modPath, "*.dll", SearchOption.AllDirectories).Take(MaxDllsReadForIdentity)
                : [];

        foreach (var dll in dlls)
        {
            if (ModAssemblyMetadata.ReadPlugin(dll).Guid is { Length: > 0 } client) guids.Add(client);
            if (ModAssemblyMetadata.ReadServer(dll)?.Guid is { Length: > 0 } server) guids.Add(server);
        }

        return guids;
    }

    // A mod folder holding more DLLs than this is not read further for its identity.
    private const int MaxDllsReadForIdentity = 200;

    // Under the Data folder: where the originals an install replaced are kept (D22).
    public const string OverwrittenDirectoryName = "overwritten";
}
