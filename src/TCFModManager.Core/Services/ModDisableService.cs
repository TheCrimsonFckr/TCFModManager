using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// One mod folder (or loose DLL) moved between a container and its ".disabled" sibling.
public sealed record ModMove(string From, string To);

// The same mod folder present in both a container and its ".disabled" sibling - one copy loaded by
// SPT, one not. Neither cleanly enabled nor cleanly disabled until one of them is set aside.
public sealed record ModDuplicatePair(InstalledMod Enabled, InstalledMod Disabled);

// A mod that couldn't be moved, and why - shown to the user rather than thrown.
public sealed record ModDisableFailure(string ModName, string Reason);

// What a disable/enable run actually did. Moved is the exact set to undo.
public sealed record ModDisableOutcome(IReadOnlyList<ModMove> Moved, IReadOnlyList<ModDisableFailure> Failed)
{
    public static ModDisableOutcome Empty { get; } = new([], []);
}

//
// Disables and re-enables installed mods by moving them between a container SPT loads from and a
// ".disabled" sibling of it - nothing is deleted, and a mod's own files (server configs included)
// travel with it. Client mod settings live in BepInEx\config, outside the mod folder, so they are
// never touched either way.
//
public static class ModDisableService
{
    // Hidden folder inside the SPT install holding copies set aside by ResolveDuplicate. Sits
    // outside every mod container, so nothing here is loaded by SPT or listed by the scanner.
    private const string DuplicatesFolderName = ".tcfmm-duplicates";

    //
    // Moves each mod into the requested state, skipping any already in it. Partial success is
    // normal: everything that could move is moved and reported, everything that couldn't is
    // returned as a failure rather than aborting the rest.
    //
    public static ModDisableOutcome Apply(IEnumerable<InstalledMod> mods, bool disable)
    {
        var targets = mods.Where(mod => mod.IsDisabled != disable).ToList();
        if (targets.Count == 0) return ModDisableOutcome.Empty;

        ModInstallService.EnsureInstallNotInUse(disable ? "disabling a mod" : "enabling a mod");

        var moved = new List<ModMove>();
        var failed = new List<ModDisableFailure>();

        foreach (var mod in targets)
        {
            var destination = DisabledModPaths.Counterpart(mod.FolderPath);

            if (destination is null)
            {
                failed.Add(new ModDisableFailure(mod.Name, "couldn't work out where to move it to"));
                continue;
            }

            if (Exists(destination))
            {
                failed.Add(new ModDisableFailure(
                    mod.Name,
                    $"something is already at {destination} - remove or rename it first"));
                continue;
            }

            if (TryMove(mod.FolderPath, destination, mod.Name, failed))
                moved.Add(new ModMove(mod.FolderPath, destination));
        }

        AppLog.Info("Disable", $"{(disable ? "disabled" : "enabled")} {moved.Count} mod(s), {failed.Count} failed");

        foreach (var move in moved) TryRemoveEmptyDisabledContainer(Path.GetDirectoryName(move.From));

        return new ModDisableOutcome(moved, failed);
    }

    //
    // Puts a previous run's moves back where they came from - the undo for Apply and
    // ResolveDuplicate. Walked in reverse, since a run that moved two things through the same path
    // (ResolveDuplicate keeping the disabled copy) only unwinds correctly last-move-first.
    //
    public static ModDisableOutcome Revert(IEnumerable<ModMove> moves)
    {
        var pending = moves.ToList();
        if (pending.Count == 0) return ModDisableOutcome.Empty;

        ModInstallService.EnsureInstallNotInUse("undoing a change");

        var moved = new List<ModMove>();
        var failed = new List<ModDisableFailure>();

        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var move = pending[i];
            var name = Path.GetFileName(move.To);

            if (!Exists(move.To) || Exists(move.From))
            {
                failed.Add(new ModDisableFailure(name, "it has changed on disk since - undo skipped"));
                continue;
            }

            if (TryMove(move.To, move.From, name, failed))
                moved.Add(new ModMove(move.To, move.From));
        }

        foreach (var move in moved) TryRemoveEmptyDisabledContainer(Path.GetDirectoryName(move.From));

        return new ModDisableOutcome(moved, failed);
    }

    //
    // The mod folders sitting in both a container and its ".disabled" sibling, matched on the exact
    // path rather than the name - so a client+server mod with only one half disabled, which is a
    // normal thing to do, isn't reported as a duplicate.
    //
    public static List<ModDuplicatePair> DuplicatePairs(IEnumerable<InstalledMod> mods)
    {
        var all = mods.ToList();
        var pairs = new List<ModDuplicatePair>();

        foreach (var enabled in all.Where(m => !m.IsDisabled))
        {
            if (DisabledModPaths.Counterpart(enabled.FolderPath) is not { } counterpart) continue;

            var disabled = all.FirstOrDefault(m =>
                m.IsDisabled && string.Equals(m.FolderPath, counterpart, StringComparison.OrdinalIgnoreCase));

            if (disabled is not null) pairs.Add(new ModDuplicatePair(enabled, disabled));
        }

        return pairs;
    }

    //
    // Settles a duplicate by keeping one copy and moving the other into a hidden
    // ".tcfmm-duplicates" folder in the install, stamped with the time and the container it came
    // from. Nothing is deleted, and the set-aside copy sits outside every mod container so SPT
    // ignores it and the scanner stops listing it. The returned moves undo through Revert.
    //
    public static ModDisableOutcome ResolveDuplicate(
        string installPath, ModDuplicatePair pair, bool keepEnabled, DateTimeOffset timestamp)
    {
        ModInstallService.EnsureInstallNotInUse("sorting out a duplicated mod");

        var moved = new List<ModMove>();
        var failed = new List<ModDisableFailure>();

        var keep = keepEnabled ? pair.Enabled : pair.Disabled;
        var discard = keepEnabled ? pair.Disabled : pair.Enabled;

        var setAside = SetAsidePath(installPath, discard.FolderPath, timestamp);

        if (!TryMove(discard.FolderPath, setAside, discard.Name, failed))
            return new ModDisableOutcome(moved, failed);

        moved.Add(new ModMove(discard.FolderPath, setAside));
        TryHide(Path.Combine(installPath, DuplicatesFolderName));

        // Keeping the disabled copy means moving it into the container the discarded one just left.
        if (!keepEnabled && DisabledModPaths.Counterpart(keep.FolderPath) is { } destination)
        {
            if (TryMove(keep.FolderPath, destination, keep.Name, failed))
                moved.Add(new ModMove(keep.FolderPath, destination));
        }

        AppLog.Info("Disable", $"resolved duplicate {keep.Name}; set aside {setAside}");

        return new ModDisableOutcome(moved, failed);
    }

    // "<install>\.tcfmm-duplicates\20260824-121500_plugins_SomeMod" - the timestamp and container
    // keep two rounds of the same duplicate apart.
    private static string SetAsidePath(string installPath, string modPath, DateTimeOffset timestamp)
    {
        var trimmed = modPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        var container = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? string.Empty);
        if (container.Length > 0) container = DisabledModPaths.Enabled(container);

        var prefix = $"{timestamp.ToLocalTime():yyyyMMdd-HHmmss}";
        var folder = container.Length > 0 ? $"{prefix}_{container}_{name}" : $"{prefix}_{name}";

        return Path.Combine(installPath, DuplicatesFolderName, folder);
    }

    private static void TryHide(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) File.SetAttributes(directory, FileAttributes.Hidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // A visible folder is only untidy, not broken.
        }
    }

    //
    // Mod names present both in a live container and in its ".disabled" sibling - what a
    // half-completed move or a hand-edited install leaves behind.
    //
    public static IReadOnlyList<string> DuplicatedNames(IEnumerable<InstalledMod> mods) =>
        mods.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(m => m.IsDisabled) && g.Any(m => !m.IsDisabled))
            .Select(g => g.Key)
            .ToList();

    private static bool TryMove(string source, string destination, string modName, List<ModDisableFailure> failed)
    {
        try
        {
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Disable", $"couldn't move {source}: {ex.Message}");
            failed.Add(new ModDisableFailure(modName, ex.Message));
            return false;
        }
    }

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    // A ".disabled" container left behind with nothing in it is removed, so an install with no
    // disabled mods doesn't keep the folders around.
    private static void TryRemoveEmptyDisabledContainer(string? container)
    {
        if (container is null || !DisabledModPaths.IsDisabled(container) || !Directory.Exists(container)) return;

        try
        {
            if (!Directory.EnumerateFileSystemEntries(container).Any()) Directory.Delete(container);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leaving an empty folder behind is harmless.
        }
    }
}
