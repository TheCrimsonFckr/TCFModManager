using System.IO;
using System.Windows;
using System.Windows.Threading;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-54 N30: watches this install's server, game and Fika headless, and raises the crash bar when
// one goes down by itself in a way that counts as a crash (CrashSigns.Decide). Every exit is logged
// with its code.
//
public static class CrashWatcher
{
    private const string ToastTag = "crash";
    private const int WindowsRecordTries = 4;

    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private static readonly Dictionary<(int Id, DateTime StartedUtc), WatchedProcess> Watched = new();

    private static string? _install;
    private static bool _checking;

    // Called once the main window has loaded.
    public static void Start()
    {
        Timer.Tick += async (_, _) => await CheckAsync();
        Timer.Start();
    }

    private static async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;

        try
        {
            var install = AppServices.SptEnvironment.InstallPath;
            if (!SameInstall(install))
            {
                foreach (var watched in Watched.Values) watched.Dispose();
                Watched.Clear();
                _install = install;
            }

            var running = await Task.Run(() => SptLaunchService.RunningProcesses(install));
            if (!SameInstall(install)) return;

            foreach (var process in running)
            {
                var key = (process.Id, process.StartedUtc);
                if (Watched.TryGetValue(key, out var known))
                {
                    if (process.HasWindow) known.ShownWindow = true;
                }
                else if (WatchedProcess.Open(process) is { } opened)
                {
                    Watched[key] = opened;
                    AppLog.Debug("Crash", $"watching {Path.GetFileName(process.ExePath)} (pid {process.Id})");
                }
            }

            foreach (var (key, watched) in Watched.ToList())
            {
                if (!watched.TryGetExit(out var exitCode)) continue;

                Watched.Remove(key);
                _ = DecideAsync(watched, exitCode, DateTime.UtcNow, install);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Crash", $"crash watch failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }

    private static async Task DecideAsync(WatchedProcess watched, int exitCode, DateTime exitedUtc, string? install)
    {
        using (watched)
        {
            var process = watched.Process;
            if (process.IsServer && AppServices.BrokenModFinder.IsRunning)
            {
                AppLog.Debug("Crash", $"server pid {process.Id} exited with {CrashSigns.Hex(exitCode)} during Find the broken mod - not judged");
                return;
            }

            var byApp = SptLaunchService.WasStoppedByApp(process.Id, process.StartedUtc);

            WatchedProcessKind kind;
            ProcessCrash? crash;
            try
            {
                (kind, crash) = await Task.Run(() => JudgeAsync(process, watched.ShownWindow, exitCode, exitedUtc, byApp, install));
            }
            catch (Exception ex)
            {
                AppLog.Warn("Crash", $"couldn't decide how pid {process.Id} ended: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            AppLog.Info("Crash", $"{kind} {Path.GetFileName(process.ExePath)} (pid {process.Id}) exited with {CrashSigns.Hex(exitCode)}"
                + (byApp ? " - stopped by the app" : string.Empty)
                + (crash is null ? string.Empty : $" - a crash ({crash.Cause}{(crash.Module is null ? "" : ", " + crash.Module)}{(crash.Mod is null ? "" : ", in " + crash.Mod.Name)})"));

            if (crash is null || !SameInstall(install)) return;

            AppServices.CrashBar.Show(crash);
            if (!WindowShowing())
                UpdateToasts.ShowMessage(ToastTag, AppServices.CrashBar.Title, AppServices.CrashBar.Message);
        }
    }

    private static async Task<(WatchedProcessKind, ProcessCrash?)> JudgeAsync(
        RunningSptProcess process, bool shownWindow, int exitCode, DateTime exitedUtc, bool byApp, string? install)
    {
        var kind = process.IsServer ? WatchedProcessKind.Server
            : shownWindow ? WatchedProcessKind.Game
            : SptLaunchService.Describe(install, SptLaunchTarget.Headless, new SettingsService().Load().HeadlessLauncherPath).Exists ? WatchedProcessKind.Headless
            : WatchedProcessKind.Game;

        if (byApp) return (kind, null);

        WindowsCrash? windows = null;
        DotNetCrash? dotnet = null;
        for (var attempt = 0; attempt < WindowsRecordTries; attempt++)
        {
            (windows, dotnet) = WindowsCrashLog.Find(process, exitedUtc);
            if (windows is not null || exitCode == 0) break;
            await Task.Delay(TimeSpan.FromSeconds(1.5));
        }

        var seenByWindows = windows is not null || dotnet is not null;
        var mods = seenByWindows || process.IsServer ? LogModLocator.Build(InstalledScan.Run(install), install) : null;
        var gaveUp = !seenByWindows && process.IsServer && mods is not null ? ServerGaveUp(install, process.StartedUtc, mods) : null;

        return (kind, CrashSigns.Decide(kind, exitCode, byApp, windows, dotnet, gaveUp, mods));
    }

    private static LogFinding? ServerGaveUp(string? install, DateTime startedUtc, LogModLocator mods)
    {
        var files = LogFiles.Find(install);
        if (files.Server is null || File.GetLastWriteTimeUtc(files.Server) < startedUtc) return null;

        var diagnosis = LogDiagnoser.Diagnose(files with { BepInEx = null, GameErrors = null, Launcher = null }, mods);
        return diagnosis.Findings.FirstOrDefault(f => f.Kind == LogFindingKind.ServerStopped);
    }

    private static bool SameInstall(string? install) => string.Equals(install, _install, StringComparison.OrdinalIgnoreCase);

    private static bool WindowShowing() =>
        Application.Current?.MainWindow is { IsVisible: true, IsActive: true } window
        && window.WindowState != WindowState.Minimized;
}
