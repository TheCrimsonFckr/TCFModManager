using System.Windows.Input;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Before the app really ends - its window closed, or Quit from the tray - with this install's server
// still up: it keeps running without the app, and with no window of its own nothing else would say
// so. Asked once per exit; Stop stops it (as the Play page does) before the app goes.
//
public static class ServerExitCheck
{
    private static bool _answered;

    /// <summary>False when the person chose Cancel: the app should stay open.</summary>
    public static bool AllowExit()
    {
        if (_answered) return true;

        var installPath = AppServices.SptEnvironment.InstallPath;
        var server = SptLaunchService.Describe(installPath, SptLaunchTarget.Server);
        if (!server.IsRunning) return true;

        var hidden = new SettingsService().Load().HideServerWindow;
        var choice = ServerStillRunningWindow.Ask(closing: true, server.ExePath ?? "", hidden);
        if (choice == ServerRunningChoice.Cancel) return false;

        _answered = true;
        if (choice != ServerRunningChoice.Stop)
        {
            AppLog.Info("Launch", "closing with the server left running, as asked");
            return true;
        }

        var previous = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var result = SptLaunchService.Stop(installPath, SptLaunchTarget.Server);
            AppLog.Info("Launch", $"closing: server stop {(result.Problem == SptLaunchProblem.None ? "done" : result.Problem.ToString())}");
        }
        finally
        {
            Mouse.OverrideCursor = previous;
        }

        return true;
    }
}
