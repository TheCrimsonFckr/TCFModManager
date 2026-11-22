using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-67 Q8: asked before an action that changes the install while SPT runs from it. The server
// alone is offered a stop; the game is only named, never closed from here.
//
public static class SptInUsePrompt
{
    public static async Task<bool> ClearAsync(string? installPath, ModInstallAction action)
    {
        var running = await Task.Run(() => ModInstallService.RunningBlockers(installPath));
        if (running.Count == 0) return true;

        var sentence = ModInstallProblems.InstallInUse(running, action);

        if (running.Any(IsGame))
        {
            AskWindow.Show(new AskRequest
            {
                Title = Strings.SptInUse_GameTitle,
                Lead = sentence,
                First = Strings.Common_Close,
                Cancel = null,
            });
            return false;
        }

        if (AskWindow.Show(new AskRequest
            {
                Title = Strings.SptInUse_StopTitle,
                Lead = sentence,
                Warning = Strings.Play_StopServerWarning,
                First = Strings.SptInUse_StopButton,
                Default = AskChoice.Cancel,
                Danger = AskChoice.First,
            }) != AskChoice.First)
        {
            return false;
        }

        AppLog.Info("Install", $"stopping {string.Join(", ", running)} for {action}");
        var result = await Task.Run(() => SptLaunchService.Stop(installPath, SptLaunchTarget.Server));

        if (result.Problem is not (SptLaunchProblem.None or SptLaunchProblem.NotRunning))
        {
            AppLog.Warn("Install", $"the server could not be stopped for {action}: {result.Problem}{(result.Error is null ? "" : " - " + result.Error.Message)}");
            ShowNotStopped(SptLaunchProblems.DescribeStop(result));
            return false;
        }

        var still = await Task.Run(() => ModInstallService.RunningBlockers(installPath));
        if (still.Count == 0) return true;

        AppLog.Warn("Install", $"still running after the stop: {string.Join(", ", still)}");
        ShowNotStopped(ModInstallProblems.InstallInUse(still, action));
        return false;
    }

    private static bool IsGame(string executable) =>
        executable.StartsWith("EscapeFromTarkov", StringComparison.OrdinalIgnoreCase);

    private static void ShowNotStopped(string lead) =>
        AskWindow.Show(new AskRequest
        {
            Title = Strings.SptInUse_NotStoppedTitle,
            Lead = lead,
            First = Strings.Common_Close,
            Cancel = null,
        });
}
