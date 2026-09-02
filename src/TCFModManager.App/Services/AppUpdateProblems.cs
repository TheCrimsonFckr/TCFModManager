using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What the user is told when a self-update can't go through.
//
// AppUpdateInstaller reports failures as an AppUpdateFailure plus the values behind it and stops
// there; the wording lives here, next to the rest of this app's prose, so it can be read and
// changed in one place rather than hunted for among the file operations.
//
// Every sentence says what happened, then what the user can do about it, then - where it's true -
// that nothing was changed. That last part matters more than it looks: a failed self-update is
// exactly the moment someone starts wondering whether their install is now half-replaced.
//
public static class AppUpdateProblems
{
    public static string Describe(AppUpdateException problem) => problem.Reason switch
    {
        AppUpdateFailure.NoDownloadFile =>
            $"sp-mod.com lists {Version(problem)} but has no file attached to it. "
            + "Open the mod page and download it manually.",

        AppUpdateFailure.DownloadNotReadable =>
            "The downloaded file isn't a readable zip - the download may have been interrupted. Try again.",

        AppUpdateFailure.ReleaseMissingExe =>
            $"The {Version(problem)} release doesn't contain {Exe(problem)} where this app expects it. "
            + "Download it from the mod page and copy it over by hand instead.",

        AppUpdateFailure.StagedBuildMissingExe =>
            $"The staged update is missing {Exe(problem)} - nothing was changed.",

        AppUpdateFailure.StagedBuildTooSmall => problem.StagedExeBytes is { } bytes
            ? $"The staged {Exe(problem)} is only {bytes / 1024}KB, which is far too small to be a real build "
                + "- nothing was changed."
            : $"The staged {Exe(problem)} is far too small to be a real build - nothing was changed.",

        AppUpdateFailure.UpdaterWouldNotStart =>
            "Couldn't start the updater script. The new version is downloaded and unpacked in "
            + $"{problem.Folder} - close the app and copy what's in there over this folder to finish by hand.",

        AppUpdateFailure.AppFolderNotWritable =>
            $"This app can't write to its own folder ({problem.Folder}), so it can't update itself in place. "
            + "Move it somewhere outside Program Files, or download the new version from the mod page and "
            + "replace it by hand.",

        AppUpdateFailure.NotEnoughFreeSpace => problem is { RequiredBytes: { } required, AvailableBytes: { } free }
            ? $"Not enough free space on {problem.DriveName} to download and unpack the update - "
                + $"about {required / (1024 * 1024)}MB is needed, {free / (1024 * 1024)}MB is free."
            : $"Not enough free space on {problem.DriveName} to download and unpack the update.",

        // Only reachable if a case is added to AppUpdateFailure without one being added here. The
        // fallback is the same advice as an unexpected failure, because that is what it is.
        _ => Unexpected(problem),
    };

    //
    // Anything that got out of the installer without being one of the cases above - an IO error
    // nobody anticipated, a permissions oddity. There is nothing specific to say, so this says the
    // one thing that is always true and always useful.
    //
    public static string Unexpected(Exception problem) =>
        $"The update failed: {problem.Message}. Nothing was changed - download it from the mod page "
        + "and replace this folder by hand if it keeps happening.";

    // The installer always fills these in for the cases that use them; the fallbacks exist so a
    // future case that forgets to still reads as a sentence.
    private static string Version(AppUpdateException problem) => problem.Version ?? "the new version";

    private static string Exe(AppUpdateException problem) => problem.ExeName ?? "the app executable";
}
