using System.Windows;
using System.Windows.Threading;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-45: stops this install's SPT server once the game has been closed for
// AppSettings.StopServerAfterGameSeconds, when AppSettings.StopServerWithGame is on.
//
// The game counts as closed once it was seen running and then seen down on two checks in a row, a
// second apart. The countdown is called off when the game is seen again, the server goes down some
// other way, the install changes, the switch is turned off, or Keep it running is pressed. While
// this install's Fika headless runs, the countdown doesn't start; it starts in full once the headless
// is down.
//
public static class ServerStopper
{
    public const int DefaultSeconds = 60;
    public const int MaxSeconds = 3600;

    private const string ToastTag = "server-stop";
    private const int DownChecksToCount = 2;

    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(1) };

    private static string? _watchedInstall;
    private static bool _gameWasUp;
    private static int _downChecks;
    private static bool _checking;
    private static bool _stopping;
    private static bool _toastShown;

    /// <summary>When the server will be stopped; null when no countdown is running.</summary>
    public static DateTime? StopsAt { get; private set; }

    /// <summary>The game has closed and the countdown waits for this install's headless to go.</summary>
    public static bool WaitingForHeadless { get; private set; }

    /// <summary>Raised on the UI thread whenever StopsAt or WaitingForHeadless changes, and each second of a countdown.</summary>
    public static event EventHandler? Changed;

    /// <summary>Raised on the UI thread after the server was stopped, with the line for the Play page.</summary>
    public static event EventHandler<GameClosedEventArgs>? Stopped;

    /// <summary>Raised when the switch or the delay is changed, so every page showing them can re-read.</summary>
    public static event EventHandler? SettingChanged;

    public static int Clamp(int seconds) => Math.Clamp(seconds, 0, MaxSeconds);

    // Called once the main window has loaded.
    public static void Start()
    {
        Timer.Tick += async (_, _) => await CheckAsync();
        if (new SettingsService().Load().StopServerWithGame) Timer.Start();
    }

    public static void SetEnabled(bool enabled)
    {
        var service = new SettingsService();
        var settings = service.Load();
        if (settings.StopServerWithGame != enabled)
        {
            settings.StopServerWithGame = enabled;
            service.Save(settings);
            AppLog.Info("Launch", enabled ? "the server stops after the game closes" : "the server no longer stops after the game closes");
        }

        Reset();
        if (enabled) Timer.Start();
        else Timer.Stop();

        SettingChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void SetSeconds(int seconds)
    {
        seconds = Clamp(seconds);

        var service = new SettingsService();
        var settings = service.Load();
        if (settings.StopServerAfterGameSeconds == seconds) return;

        settings.StopServerAfterGameSeconds = seconds;
        service.Save(settings);

        SettingChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Keep it running: calls off this countdown. The next time the game closes counts again.</summary>
    public static void KeepRunning()
    {
        if (StopsAt is null && !WaitingForHeadless) return;

        AppLog.Info("Launch", "server stop after the game closed called off - kept running, as asked");
        Reset();
    }

    private static void Reset()
    {
        _gameWasUp = false;
        _downChecks = 0;
        EndCountdown();
    }

    private static void EndCountdown()
    {
        var changed = StopsAt is not null || WaitingForHeadless;
        StopsAt = null;
        WaitingForHeadless = false;

        if (_toastShown)
        {
            _toastShown = false;
            UpdateToasts.RemoveMessage(ToastTag);
        }

        if (changed) Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool WindowShowing() =>
        Application.Current?.MainWindow is { IsVisible: true, IsActive: true } window
        && window.WindowState != WindowState.Minimized;

    private static async Task CheckAsync()
    {
        if (_stopping || _checking) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (!string.Equals(installPath, _watchedInstall, StringComparison.OrdinalIgnoreCase))
        {
            _watchedInstall = installPath;
            Reset();
        }

        var settings = new SettingsService().Load();

        bool gameUp, serverUp, headlessUp;
        _checking = true;
        try
        {
            (gameUp, serverUp, headlessUp) = await Task.Run(() =>
            {
                var spares = GameCloser.SparesWindowless(settings, installPath);
                return (
                    SptLaunchService.IsGameRunning(installPath, spares),
                    SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning,
                    SptLaunchService.Describe(installPath, SptLaunchTarget.Headless, settings.HeadlessLauncherPath).IsRunning);
            });
        }
        finally
        {
            _checking = false;
        }

        if (!Timer.IsEnabled || !string.Equals(installPath, _watchedInstall, StringComparison.OrdinalIgnoreCase)) return;

        if (gameUp)
        {
            if (StopsAt is not null || WaitingForHeadless)
                AppLog.Info("Launch", "the game is back; the server is left running");

            _gameWasUp = true;
            _downChecks = 0;
            EndCountdown();
            return;
        }

        if (!_gameWasUp) return;

        if (!serverUp)
        {
            Reset();
            return;
        }

        if (++_downChecks < DownChecksToCount) return;

        if (headlessUp)
        {
            if (!WaitingForHeadless)
            {
                AppLog.Info("Launch", "the game closed; the server waits for the headless client to close");
                StopsAt = null;
                WaitingForHeadless = true;
                Changed?.Invoke(null, EventArgs.Empty);
            }

            return;
        }

        if (StopsAt is null)
        {
            var seconds = Clamp(settings.StopServerAfterGameSeconds);
            StopsAt = DateTime.UtcNow.AddSeconds(seconds);
            WaitingForHeadless = false;
            AppLog.Info("Launch", $"the game closed; the server stops in {seconds} s");

            if (seconds > 0 && !WindowShowing())
            {
                UpdateToasts.ShowServerStopCountdown(
                    ToastTag,
                    Strings.ServerStopToast_Title,
                    LocalizationService.Text(Strings.ServerStopToast_CountdownFormat, seconds),
                    Strings.Play_KeepServerRunning);
                _toastShown = true;
            }
        }

        if (DateTime.UtcNow < StopsAt)
        {
            Changed?.Invoke(null, EventArgs.Empty);
            return;
        }

        await StopAsync(installPath);
    }

    private static async Task StopAsync(string? installPath)
    {
        _stopping = true;
        try
        {
            AppLog.Info("Launch", "stopping the server, the game having closed");
            var result = await Task.Run(() => SptLaunchService.Stop(installPath, SptLaunchTarget.Server));

            var stopped = result.Problem is SptLaunchProblem.None or SptLaunchProblem.NotRunning;
            if (stopped)
                AppLog.Info("Launch", result.Problem == SptLaunchProblem.None ? "the server is stopped" : "the server had already gone");
            else
                AppLog.Warn("Launch", $"the server could not be stopped: {result.Problem}{(result.Error is null ? "" : " - " + result.Error.Message)}");

            var message = stopped ? Strings.Play_ServerStoppedAfterGame : SptLaunchProblems.DescribeStop(result);
            var hadToast = _toastShown;

            _toastShown = false;
            _gameWasUp = false;
            _downChecks = 0;
            StopsAt = null;
            WaitingForHeadless = false;
            Changed?.Invoke(null, EventArgs.Empty);

            if (hadToast || !WindowShowing())
                UpdateToasts.ShowMessage(ToastTag, Strings.ServerStopToast_Title, message);

            Stopped?.Invoke(null, new GameClosedEventArgs(message, IsError: !stopped));
        }
        finally
        {
            _stopping = false;
        }
    }
}
