using System.Diagnostics;

namespace TCFModManager.Core.Services;

public enum SptLaunchTarget
{
    // SPT.Server.exe, wherever this install's layout keeps it.
    Server,

    // SPT.Launcher.exe - the launcher a player picks a profile in and starts the game from.
    Client,

    // A Fika install's own launcher, which is only present on a setup running a headless client.
    Headless,
}

//
// Why a target could not be started. The App words it; this reports what happened and the values
// behind it, the same split ModInstallService and the self-updater use.
//
public enum SptLaunchProblem
{
    None,

    // No install folder is configured, or it is not there any more.
    NoInstallFolder,

    // The folder holds no executable for this target in any layout this app knows.
    // Carries InstallPath.
    ExeNotFound,

    // Already running, so there is nothing to start. Carries ProcessName.
    AlreadyRunning,

    // Starting the process threw. Carries ExePath and Error.
    StartFailed,
}

// What this install can currently do for one target.
public sealed record SptLaunchTargetInfo
{
    public required SptLaunchTarget Target { get; init; }

    public string? InstallPath { get; init; }

    // Null when Problem says why it could not be found.
    public string? ExePath { get; init; }

    // The exe's own name without its extension, which is also how it appears in the process list.
    public string? ProcessName { get; init; }

    public bool IsRunning { get; init; }

    public SptLaunchProblem Problem { get; init; }

    public bool Exists => ExePath is not null;

    public bool CanLaunch => ExePath is not null && !IsRunning;
}

public sealed record SptLaunchResult
{
    public required SptLaunchTargetInfo Info { get; init; }

    public bool Started { get; init; }

    public SptLaunchProblem Problem { get; init; }

    public Exception? Error { get; init; }
}

//
// Starts an install's server, its game launcher, and - where one exists - its Fika headless
// launcher, and reports which of them is already up. Nothing here ever stops a process: the server
// holds a live profile, and this app is not the thing that should decide to kill it.
//
public static class SptLaunchService
{
    // Beside the server exe, in whichever folder that turned out to be.
    private static readonly string[] ClientLauncherCandidates =
    [
        "SPT.Launcher.exe",
        "Aki.Launcher.exe",
    ];

    //
    // What starts a Fika headless client, at the install root.
    //
    // A headless install still has SPT.Server.exe, BepInEx and a game client exactly like a player's,
    // so nothing else about the folder tells the two apart. This exe is the whole signal, which is
    // why it has to be the RIGHT exe.
    //
    // "HEADLESS" IN THE NAME IS THE DISCRIMINATOR, and the only one. Two wrong versions of this have
    // now shipped, in opposite directions:
    //
    //   - "*Fika*Launcher*.exe" alone, which does NOT match FikaHeadlessManager.exe - so the Play
    //     page's headless card never once appeared on the machine it exists for.
    //   - the same pattern kept on as a fallback, which matches "SPT-Fika Launcher.exe" - the
    //     ordinary Fika PLAYER launcher, present on every Fika player install. That is the far worse
    //     of the two: it makes a player's machine look like a headless, and answering the setup
    //     prompt on the back of it would have a server's mod list arrive stripped of the mods only a
    //     player needs.
    //
    // So: the confirmed name first, and one pattern behind it that still requires the word Headless.
    // A player-facing launcher will never carry it. Do not widen this to match "Fika" and "Launcher"
    // again - that is the bug, not the safety net.
    //
    private static readonly string[] HeadlessLauncherCandidates =
    [
        "FikaHeadlessManager.exe",
    ];

    private static readonly string[] HeadlessLauncherWildcards =
    [
        "*Headless*.exe",
    ];

    // Other processes that mean this target is already up, whatever the exe on disk is called.
    private static readonly string[] ServerProcessNames = ["SPT.Server", "Aki.Server"];

    private static readonly string[] ClientProcessNames = ["EscapeFromTarkov"];

    public static SptLaunchTargetInfo Describe(string? installPath, SptLaunchTarget target)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return new SptLaunchTargetInfo { Target = target, Problem = SptLaunchProblem.NoInstallFolder };
        }

        var exePath = FindExe(installPath!, target);

        if (exePath is null)
        {
            return new SptLaunchTargetInfo
            {
                Target = target,
                InstallPath = installPath,
                Problem = SptLaunchProblem.ExeNotFound,
            };
        }

        var processName = Path.GetFileNameWithoutExtension(exePath);

        return new SptLaunchTargetInfo
        {
            Target = target,
            InstallPath = installPath,
            ExePath = exePath,
            ProcessName = processName,
            IsRunning = IsRunning(target, processName),
        };
    }

    public static SptLaunchResult Launch(string? installPath, SptLaunchTarget target)
    {
        var info = Describe(installPath, target);

        if (info.Problem != SptLaunchProblem.None)
        {
            return new SptLaunchResult { Info = info, Problem = info.Problem };
        }

        if (info.IsRunning)
        {
            return new SptLaunchResult { Info = info, Problem = SptLaunchProblem.AlreadyRunning };
        }

        try
        {
            //
            // UseShellExecute starts it as the shell would - detached, so closing this app does not
            // take the server down with it, and with the console window SPT.Server draws.
            //
            // WorkingDirectory is the exe's own folder because SPT resolves its data folders
            // relative to it; started from this app's directory it looks for them here.
            //
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = info.ExePath!,
                WorkingDirectory = Path.GetDirectoryName(info.ExePath!),
                UseShellExecute = true,
            });

            return new SptLaunchResult { Info = info, Started = true };
        }
        catch (Exception ex)
        {
            return new SptLaunchResult { Info = info, Problem = SptLaunchProblem.StartFailed, Error = ex };
        }
    }

    private static string? FindExe(string installPath, SptLaunchTarget target)
    {
        switch (target)
        {
            case SptLaunchTarget.Server:
                return SptInstallationService.TryFindServerExe(installPath, out var server) ? server : null;

            case SptLaunchTarget.Client:
                return TryFindClientLauncherExe(installPath, out var client) ? client : null;

            case SptLaunchTarget.Headless:
                return FindHeadlessLauncher(installPath);

            default:
                return null;
        }
    }

    // The launcher a player starts the game from. It sits beside the server exe in every layout.
    public static bool TryFindClientLauncherExe(string installPath, out string exePath)
    {
        exePath = "";

        if (!SptInstallationService.TryGetServerRoot(installPath, out var serverRoot)) return false;

        var searchDirectory = string.IsNullOrEmpty(serverRoot)
            ? installPath
            : Path.Combine(installPath, serverRoot);

        foreach (var name in ClientLauncherCandidates)
        {
            var candidate = Path.Combine(searchDirectory, name);
            if (File.Exists(candidate))
            {
                exePath = candidate;
                return true;
            }
        }

        return false;
    }

    // True when this install has a Fika headless launcher, which is what makes it a headless setup.
    public static bool TryFindHeadlessLauncherExe(string installPath, out string exePath)
    {
        exePath = FindHeadlessLauncher(installPath) ?? "";
        return exePath.Length > 0;
    }

    private static string? FindHeadlessLauncher(string installPath)
    {
        foreach (var name in HeadlessLauncherCandidates)
        {
            var candidate = Path.Combine(installPath, name);
            if (File.Exists(candidate)) return candidate;
        }

        foreach (var pattern in HeadlessLauncherWildcards)
        {
            if (FindFirstFile(installPath, pattern) is { } found) return found;
        }

        return null;
    }

    private static string? FindFirstFile(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsRunning(SptLaunchTarget target, string processName)
    {
        //
        // The headless launcher is checked by its own name only. A headless client runs
        // EscapeFromTarkov like any other, so folding that in would make this read as running
        // whenever the player's own game was open on the same machine.
        //
        var known = target switch
        {
            SptLaunchTarget.Server => ServerProcessNames,
            SptLaunchTarget.Client => ClientProcessNames,
            _ => [],
        };

        foreach (var name in known.Append(processName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            Process[] found;
            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch (InvalidOperationException)
            {
                // Process list unavailable - read as nothing running rather than blocking the user.
                continue;
            }

            try
            {
                if (found.Length > 0) return true;
            }
            finally
            {
                foreach (var process in found) process.Dispose();
            }
        }

        return false;
    }
}
