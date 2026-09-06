using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What the user is told when the server or the game can't be started. SptLaunchService reports a
// SptLaunchProblem plus the values behind it and stops there; the wording lives here, beside the
// rest of this app's prose, for the same reason ModInstallProblems and AppUpdateProblems do.
//
public static class SptLaunchProblems
{
    public static string Describe(SptLaunchResult result) => result.Problem switch
    {
        SptLaunchProblem.NoInstallFolder => AppMessages.NoSptInstallFolder,

        SptLaunchProblem.ExeNotFound => result.Info.Target == SptLaunchTarget.Server
            ? $"No SPT server executable in {result.Info.InstallPath} - check the install folder on "
              + "the Options page."
            : $"No launcher in {result.Info.InstallPath} - check the install folder on the Options page.",

        // Not really a failure: the thing the button offers to start is already up.
        SptLaunchProblem.AlreadyRunning => result.Info.Target == SptLaunchTarget.Server
            ? "The SPT server is already running."
            : "The game is already running.",

        // The inner exception is the only thing that says why, so it is quoted rather than summarised.
        SptLaunchProblem.StartFailed =>
            $"Couldn't start {result.Info.ProcessName}: {result.Error?.Message}",

        _ => "",
    };

    //
    // The resting line under each button - what this install can do for that target right now,
    // before anything is clicked. Kept here beside the failure wording so the page never says one
    // thing in its status line and another in its message.
    //
    public static string DescribeState(SptLaunchTargetInfo info) => info.Problem switch
    {
        SptLaunchProblem.NoInstallFolder => AppMessages.NoSptInstallFolder,

        SptLaunchProblem.ExeNotFound => info.Target == SptLaunchTarget.Server
            ? "No SPT server executable found in this install."
            : "No launcher found in this install.",

        _ when info.IsRunning => info.Target == SptLaunchTarget.Server
            ? "Running."
            : "Running - the game or its launcher is already open.",

        _ => "Not running.",
    };
}
