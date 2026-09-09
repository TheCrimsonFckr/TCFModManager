using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What the user is told when the server, the game or a headless client can't be started.
// SptLaunchService reports a SptLaunchProblem plus the values behind it and stops there; the
// wording lives here, beside the rest of this app's prose, for the same reason ModInstallProblems
// and AppUpdateProblems do.
//
public static class SptLaunchProblems
{
    public static string Describe(SptLaunchResult result) => result.Problem switch
    {
        SptLaunchProblem.NoInstallFolder => AppMessages.NoSptInstallFolder,

        SptLaunchProblem.ExeNotFound =>
            $"No {What(result.Info.Target)} in {result.Info.InstallPath} - check the install folder "
            + "on the Options page.",

        // Not really a failure: the thing the button offers to start is already up.
        SptLaunchProblem.AlreadyRunning => result.Info.Target switch
        {
            SptLaunchTarget.Server => "The SPT server is already running.",
            SptLaunchTarget.Client => "The game is already running.",
            _ => "The headless launcher is already running.",
        },

        // The inner exception is the only thing that says why, so it is quoted rather than summarised.
        SptLaunchProblem.StartFailed =>
            $"Couldn't start {result.Info.ProcessName}: {result.Error?.Message}",

        // A restart of something that is not up. Said as what to do instead, since the button that
        // does it is the one right beside the one that was pressed.
        SptLaunchProblem.NotRunning => result.Info.Target switch
        {
            SptLaunchTarget.Server => "The server isn't running, so there's nothing to restart - start it instead.",
            _ => "The headless launcher isn't running, so there's nothing to restart - start it instead.",
        },

        //
        // Windows refuses a process this app has no right to touch - one started elevated, or under
        // another account - and that is far and away the likeliest reason, so it is said outright
        // rather than left as "something went wrong".
        //
        SptLaunchProblem.StopFailed =>
            $"Couldn't stop {result.Info.ProcessName}"
            + (result.Error is null ? "" : $": {result.Error.Message}")
            + ". It may be running as another user or as administrator - closing its window by hand"
            + " and then starting it again does the same job.",

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

        SptLaunchProblem.ExeNotFound => $"No {What(info.Target)} found in this install.",

        _ when info.IsRunning => info.Target switch
        {
            SptLaunchTarget.Server => "Running.",
            SptLaunchTarget.Client => "Running - the game or its launcher is already open.",
            _ => "Running.",
        },

        _ => "Not running.",
    };

    private static string What(SptLaunchTarget target) => target switch
    {
        SptLaunchTarget.Server => "SPT server executable",
        SptLaunchTarget.Client => "SPT launcher",
        _ => "Fika headless launcher",
    };
}
