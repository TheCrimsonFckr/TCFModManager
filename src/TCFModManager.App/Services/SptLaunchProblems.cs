using System.Globalization;
using TCFModManager.App.Localization;
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

        //
        // One whole sentence per target rather than one sentence with the name of the thing dropped
        // into it. A noun spliced into a sentence is the fragment problem in D8: the article, the
        // case and the word order all move with the noun in most languages, and a translator handed
        // "No {0} in {1}" cannot make any of that agree.
        //
        SptLaunchProblem.ExeNotFound => string.Format(
            CultureInfo.CurrentCulture,
            result.Info.Target switch
            {
                SptLaunchTarget.Server => Strings.SptLaunch_ServerExeNotFoundFormat,
                SptLaunchTarget.Client => Strings.SptLaunch_ClientExeNotFoundFormat,
                _ => Strings.SptLaunch_HeadlessExeNotFoundFormat,
            },
            result.Info.InstallPath),

        // Not really a failure: the thing the button offers to start is already up.
        SptLaunchProblem.AlreadyRunning => result.Info.Target switch
        {
            SptLaunchTarget.Server => Strings.SptLaunch_ServerAlreadyRunning,
            SptLaunchTarget.Client => Strings.SptLaunch_ClientAlreadyRunning,
            _ => Strings.SptLaunch_HeadlessAlreadyRunning,
        },

        // The inner exception is the only thing that says why, so it is quoted rather than summarised.
        SptLaunchProblem.StartFailed => string.Format(
            CultureInfo.CurrentCulture,
            Strings.SptLaunch_StartFailedFormat,
            result.Info.ProcessName,
            result.Error?.Message),

        // A restart of something that is not up. Said as what to do instead, since the button that
        // does it is the one right beside the one that was pressed.
        SptLaunchProblem.NotRunning => result.Info.Target switch
        {
            SptLaunchTarget.Server => Strings.SptLaunch_ServerNotRunning,
            _ => Strings.SptLaunch_HeadlessNotRunning,
        },

        //
        // Windows refuses a process this app has no right to touch - one started elevated, or under
        // another account - and that is far and away the likeliest reason, so it is said outright
        // rather than left as "something went wrong".
        //
        //
        // Two whole sentences rather than one built by concatenation around the reason. The reason
        // sits mid-sentence, and a language that puts it elsewhere - or needs different punctuation
        // around it - cannot be served by gluing a fragment into the middle.
        //
        SptLaunchProblem.StopFailed => result.Error is null
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.SptLaunch_StopFailedFormat,
                result.Info.ProcessName)
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.SptLaunch_StopFailedReasonFormat,
                result.Info.ProcessName,
                result.Error.Message),

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

        SptLaunchProblem.ExeNotFound => info.Target switch
        {
            SptLaunchTarget.Server => Strings.SptLaunch_ServerNotFoundHere,
            SptLaunchTarget.Client => Strings.SptLaunch_ClientNotFoundHere,
            _ => Strings.SptLaunch_HeadlessNotFoundHere,
        },

        _ when info.IsRunning => info.Target switch
        {
            SptLaunchTarget.Client => Strings.SptLaunch_ClientRunning,
            _ => Strings.SptLaunch_Running,
        },

        _ => Strings.SptLaunch_NotRunning,
    };
}
