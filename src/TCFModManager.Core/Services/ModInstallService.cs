using System.ComponentModel;
using System.Diagnostics;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Downloads, extracts, and installs a mod version's files into an SPT install, and records what it placed for later uninstall.
public sealed partial class ModInstallService(
    ModDownloadService downloadService,
    ModInstallManifestService manifestService,
    ConfigCarryOver? configCarryOver = null,
    ConfigUpdateLog? configUpdateLog = null,
    ModConfigOptionsStore? configOptions = null,
    RemovedMods? removedMods = null,
    InstallJournal? installJournal = null,
    ProfileBackups? profileBackups = null)
{
    private readonly RemovedMods _removed = removedMods ?? new RemovedMods();

    private readonly InstallJournal _journal = installJournal ?? InstallJournal.Beside(manifestService);

    private readonly ConfigCarryOver _configs = configCarryOver ?? new ConfigCarryOver();
    private readonly ModConfigOptionsStore _options = configOptions ?? new ModConfigOptionsStore();
    private readonly ConfigUpdateLog _configLog = configUpdateLog ?? new ConfigUpdateLog();

    // Scratch folder created inside the SPT install so extracted files can be moved into
    // place rather than copied across volumes. Falls back to %TEMP% when it can't be created.
    internal const string WorkFolderName = ".tcfmm-work";

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
}

//
// What an install placed, and what it did to the mod's own config files.
//
// Configs is null when the mod has none - most client-only mods, and anything whose settings live in
// BepInEx\config rather than inside its own folder.
//
//
// SkippedProtected lists the archive's files that were not placed because the install's own copy is
// SPT's, BepInEx's or the game's (D3-D5). SkippedAppFolder lists the ones that would have landed on
// this app's own files or folders.
//
public sealed record ModInstallResult(
    InstalledModRecord Record,
    ConfigUpdateReport? Configs,
    IReadOnlyList<string>? SkippedProtected = null,
    IReadOnlyList<string>? SkippedAppFolder = null)
{
    // Files no record owned that this install replaced, kept in Data to put back on removal (D22).
    // Only the ones this install kept - originals an earlier version kept aren't counted again.
    public int OriginalsKept { get; init; }

    // BepInEx\config files the archive ships that were already there, left as they are - the
    // user's plugin settings (see ConfigCarryOver.Prepare).
    public IReadOnlyList<string> KeptSettings { get; init; } = [];
}

// Result of ModInstallService.UninstallAsync. FailedFiles lists files that couldn't be
// deleted; the mod is still removed from the manifest regardless. ConfigsKept/ConfigsFolder
// describe the mod's own config files when they were moved out rather than deleted.
// RefusedFiles lists recorded paths that failed InstallPathGuard's checks and were left untouched.
//
// Since v1.19.0 FilesDeleted counts files MOVED into the holding folder - nothing is deleted outright.
// KeptChanged: left because they changed since install (D21). KeptOwned: left because another mod's
// record lists them (D9). OriginalsRestored: files this mod had replaced, put back (D22).
// HoldingFolder: where this removal is held, or null when the setting deleted it straight away.
public sealed record UninstallResult(
    int FilesDeleted,
    List<string> FailedFiles,
    int ConfigsKept = 0,
    string? ConfigsFolder = null,
    List<string>? RefusedFiles = null)
{
    public List<string> KeptChanged { get; init; } = [];
    public List<string> KeptOwned { get; init; } = [];
    public int OriginalsRestored { get; init; }
    public string? HoldingFolder { get; init; }

    // The mod's folders that stayed because they hold files it didn't install.
    public List<FolderLeft> FoldersLeft { get; init; } = [];
}

// A mod folder a removal left in place, install-relative, and how many files in it the mod didn't install.
public sealed record FolderLeft(string Folder, int Files);

// What Undo did: whether it ran at all, how many files and folders came back, and the paths it left in
// the holding folder because something else now occupies them.
public sealed record UndoResult(bool Ran, int PutBack, List<string> Blocked);

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
