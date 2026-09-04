using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What the user is told when applying a mod list stops before it finishes.
//
// The Core half reports a ModListStop and the values behind it; the wording lives here beside the
// rest of this app's prose, for the same reason AppUpdateProblems and ModInstallProblems do.
//
// Every sentence here also answers "what happened to my install?", because that is the question a
// stopped apply actually raises. The applier's ordering is what makes those answers true: nothing
// is ever disabled unless every download worked, so a stop can only ever have enabled things.
//
public static class ModListProblems
{
    public static string Describe(ModListApplyResult result) => result.Stopped switch
    {
        // Worded by ModInstallProblems rather than here, so a locked install reads the same on this
        // page as it does everywhere else in the app.
        ModListStop.InstallInUse =>
            ModInstallProblems.InstallInUse(result.Running, ModInstallAction.ApplyList),

        ModListStop.FetchCancelled =>
            "Cancelled before anything was disabled.",

        ModListStop.FetchFailed => result.FailedFetches == 1
            ? "One mod couldn't be downloaded, so nothing was disabled."
            : $"{result.FailedFetches} mods couldn't be downloaded, so nothing was disabled.",

        // The refusal already knows which operation was blocked and what is holding the install.
        ModListStop.MovesRefused when result.Refusal is { } refusal =>
            ModInstallProblems.Describe(refusal),

        _ => "Stopped before it finished.",
    };
}
