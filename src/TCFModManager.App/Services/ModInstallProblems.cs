using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What the user is told when installing, removing, enabling or disabling a mod can't go ahead.
//
// ModInstallService and ModDisableService report a ModInstallFailure plus the values behind it and
// stop there; the wording lives here, beside the rest of this app's prose, for the same reason
// AppUpdateProblems exists - so it can be read and changed in one place rather than hunted for
// among file operations in a project that has no UI.
//
// Every sentence says what happened, then what the user can do about it.
//
public static class ModInstallProblems
{
    public static string Describe(ModInstallException problem) => problem.Reason switch
    {
        ModInstallFailure.InstallInUse => InstallInUse(problem.Running, problem.Action),

        ModInstallFailure.NoInstallFolder => AppMessages.NoSptInstallFolder,

        ModInstallFailure.NoDownloadLink =>
            $"{problem.ModName} {problem.Version} has no download link.",

        ModInstallFailure.UnrecognisedArchive =>
            $"{problem.ModName} {problem.Version}'s archive doesn't look like a normal SPT mod package "
            + "(no BepInEx/user/SPT folder found in it) - install it manually instead.",

        // The inner exception is what actually went wrong part way through, and it is the only
        // thing here that says why - so it is quoted rather than summarised.
        ModInstallFailure.PartlyInstalled =>
            $"{problem.ModName} {problem.Version} was only partly installed - {problem.PlacedFiles} of "
            + $"{problem.TotalFiles} files were placed before this failed: {problem.InnerException?.Message} "
            + "Close SPT and its server, then install it again.",

        ModInstallFailure.UnsafeArchiveEntry =>
            $"Archive entry \"{problem.ArchiveEntry}\" would extract outside the target folder - "
            + "refusing to extract it.",

        _ => $"Couldn't finish that: {problem.Reason}.",
    };

    //
    // Public because the pages check for a running install BEFORE asking the user anything, so a
    // locked install is reported up front rather than after they have answered a confirmation.
    // Those checks used to build their own version of this sentence, which is how the app ended up
    // with three wordings of it that did not agree.
    //
    public static string InstallInUse(IReadOnlyList<string> running, ModInstallAction action) =>
        $"Close {string.Join(" and ", running)} before {Doing(action)} - "
        + "files inside the SPT install are locked while it's running.";

    private static string Doing(ModInstallAction action) => action switch
    {
        ModInstallAction.Install => "installing a mod",
        ModInstallAction.Remove => "removing a mod",
        ModInstallAction.Disable => "disabling a mod",
        ModInstallAction.Enable => "enabling a mod",
        ModInstallAction.Undo => "undoing a change",
        ModInstallAction.SortOutDuplicate => "sorting out a duplicated mod",
        _ => "changing your mods",
    };
}
