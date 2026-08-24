using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// One mod folder (or loose DLL) moved between a container and its ".disabled" sibling.
public sealed record ModMove(string From, string To);

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

    // Puts a previous run's moves back where they came from - the undo for Apply.
    public static ModDisableOutcome Revert(IEnumerable<ModMove> moves)
    {
        var pending = moves.ToList();
        if (pending.Count == 0) return ModDisableOutcome.Empty;

        ModInstallService.EnsureInstallNotInUse("undoing a change");

        var moved = new List<ModMove>();
        var failed = new List<ModDisableFailure>();

        foreach (var move in pending)
        {
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
    // Mod names present both in a live container and in its ".disabled" sibling - what a
    // half-completed move or a hand-edited install leaves behind. Such a mod is neither cleanly
    // enabled nor cleanly disabled and needs sorting out by hand.
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
