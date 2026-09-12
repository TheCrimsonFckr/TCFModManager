using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// What happens to a server mod's own config files when a new version of it is installed over the
// old one.
//
// Two steps, either side of the install placing its files:
//
//   Prepare - before anything is removed or placed, copy every config that is there now out to
//             Data\LegacyConfigs. Copied rather than moved, so a file the new version ships is still
//             in place when the placement loop overwrites it, and a file that was never tracked (a
//             mod installed by hand) is rescued too - that one used to be overwritten with nothing
//             kept anywhere.
//
//   Settle  - after the new files are placed, record what each version shipped as a baseline and
//             decide, per file, what the update did to it.
//
// Nothing here fails an install. A file that cannot be copied aside is left exactly as it was and
// reported, which is the one case where the update deliberately does less than it was asked to.
//
public sealed class ConfigCarryOver(ConfigBaselineStore? baselines = null, string? archiveRoot = null)
{
    private readonly ConfigBaselineStore _baselines = baselines ?? new ConfigBaselineStore();
    private readonly string _archiveRoot = archiveRoot ?? AppPaths.LegacyConfigsDirectory;

    public ConfigBaselineStore Baselines => _baselines;

    //
    // Copies aside the config files this install already holds for the mod - the ones the old
    // version's record lists, plus the ones the new version is about to place over. Both, because
    // neither list is the whole answer: a record's list misses anything the mod or the user created
    // after the install, and the incoming list misses a file the new version has stopped shipping.
    //
    public PendingConfigs Prepare(
        string installPath,
        InstalledModRecord? existing,
        IEnumerable<string> incomingRelativePaths,
        string modName,
        DateTimeOffset timestamp)
    {
        var candidates = new List<string>();
        if (existing is not null) candidates.AddRange(ModConfigFiles.InRecord(existing));
        candidates.AddRange(incomingRelativePaths.Where(ModConfigFiles.IsServerModConfig));

        var archived = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var untouchable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var archiveFolder = ModConfigFiles.ArchiveFolder(_archiveRoot, modName, timestamp);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in candidates)
        {
            if (!seen.Add(relative)) continue;

            var source = Path.Combine(installPath, ToNative(relative));
            if (!File.Exists(source)) continue;

            var destination = Path.Combine(archiveFolder, ToNative(relative));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                archived[relative] = destination;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nowhere to put a copy means the file is not safe to replace or delete, so it is
                // left where it is and the update says so.
                AppLog.Warn("Configs", $"couldn't copy {relative} aside; leaving it alone: {ex.Message}");
                untouchable.Add(relative);
            }
        }

        if (archived.Count > 0)
            AppLog.Info("Configs", $"copied {archived.Count} config file(s) from {modName} into {archiveFolder}");

        return new PendingConfigs(archived.Count > 0 ? archiveFolder : null, archived, untouchable);
    }

    //
    // Records the new version's shipped copies and reports what the update did to each file. Run
    // after the placement loop, so the files on disk are the new version's.
    //
    public ConfigUpdateReport Settle(
        PendingConfigs pending,
        string installPath,
        InstallTarget target,
        InstalledModRecord? existing,
        string newVersion,
        IEnumerable<string> placedRelativePaths,
        DateTimeOffset timestamp)
    {
        var placed = placedRelativePaths.Where(ModConfigFiles.IsServerModConfig).ToList();
        var outcomes = new List<ConfigFileOutcome>();

        // The new version's shipped copies, taken from what was just placed rather than out of the
        // extract folder - these are the exact bytes the install now holds.
        _baselines.Capture(installPath, target.Id, target.IsAddon, newVersion,
            placed.Where(p => !pending.Untouchable.Contains(p)));

        foreach (var relative in placed)
        {
            if (pending.Untouchable.Contains(relative))
            {
                outcomes.Add(new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.NotUpdated });
                continue;
            }

            outcomes.Add(Decide(pending, installPath, target, existing, relative));
        }

        // Archived files the new version no longer ships. Their copy is the only one left.
        foreach (var relative in pending.Archived.Keys)
        {
            if (placed.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;

            outcomes.Add(new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.Removed });
        }

        // One the new version does not ship AND could not be copied: left in the install untouched,
        // which is worth saying since nothing now tracks it.
        foreach (var relative in pending.Untouchable)
        {
            if (placed.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;

            outcomes.Add(new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.NotUpdated });
        }

        _baselines.Prune(target.Id, target.IsAddon, existing?.Version, newVersion);

        return new ConfigUpdateReport
        {
            ModId = target.Id,
            IsAddon = target.IsAddon,
            ModName = target.Name,
            FromVersion = existing?.Version,
            ToVersion = newVersion,
            At = timestamp,
            ArchiveFolder = pending.ArchiveFolder,
            Files = [.. outcomes.OrderBy(o => o.Path, StringComparer.OrdinalIgnoreCase)],
        };
    }

    //
    // One file's outcome. The three inputs are the file as it stood (U, now in the archive), the
    // file the old version shipped (B, the baseline) and the file now on disk (N).
    //
    // Without a baseline the only honest answer is that the file was replaced: U differing from N
    // says nothing about who changed it, and claiming the user's edits were carried when nothing
    // knows what their edits were would be worse than saying so.
    //
    private ConfigFileOutcome Decide(
        PendingConfigs pending,
        string installPath,
        InstallTarget target,
        InstalledModRecord? existing,
        string relative)
    {
        if (!pending.Archived.TryGetValue(relative, out var userCopy))
            return new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.Added };

        var installed = Path.Combine(installPath, ToNative(relative));

        if (SameBytes(userCopy, installed))
            return new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.Unchanged };

        var baseline = _baselines.Find(target.Id, target.IsAddon, existing?.Version, relative);

        if (baseline is null)
        {
            return new ConfigFileOutcome
            {
                Path = relative,
                Kind = ConfigOutcomeKind.Replaced,
                Reason = ConfigReplaceReason.NoBaseline,
            };
        }

        // The user never touched it, so the new defaults are simply the newer answer and nothing of
        // theirs is lost.
        if (SameBytes(userCopy, baseline))
            return new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.DefaultsUpdated };

        return new ConfigFileOutcome { Path = relative, Kind = ConfigOutcomeKind.Replaced };
    }

    private static bool SameBytes(string left, string right)
    {
        try
        {
            var a = new FileInfo(left);
            var b = new FileInfo(right);
            if (!a.Exists || !b.Exists || a.Length != b.Length) return false;

            return File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ToNative(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);
}

//
// The config files copied aside before an update, by install-relative path.
//
// Untouchable holds the ones that could not be copied: the install path must not delete or overwrite
// those, which is the one thing an install has to read back out of here.
//
public sealed record PendingConfigs(
    string? ArchiveFolder,
    IReadOnlyDictionary<string, string> Archived,
    IReadOnlySet<string> Untouchable)
{
    public static PendingConfigs None { get; } = new(
        null,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}
