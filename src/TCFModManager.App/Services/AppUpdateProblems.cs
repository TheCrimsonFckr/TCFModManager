using System.Globalization;
using TCFModManager.App.Localization;
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
        AppUpdateFailure.NoDownloadFile => string.Format(
            CultureInfo.CurrentCulture,
            Strings.AppUpdate_NoDownloadFileFormat,
            Version(problem)),

        AppUpdateFailure.DownloadNotReadable => Strings.AppUpdate_DownloadNotReadable,

        AppUpdateFailure.ReleaseMissingExe => string.Format(
            CultureInfo.CurrentCulture,
            Strings.AppUpdate_ReleaseMissingExeFormat,
            Version(problem),
            Exe(problem)),

        AppUpdateFailure.StagedBuildMissingExe => string.Format(
            CultureInfo.CurrentCulture,
            Strings.AppUpdate_StagedMissingExeFormat,
            Exe(problem)),

        AppUpdateFailure.StagedBuildTooSmall => problem.StagedExeBytes is { } bytes
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.AppUpdate_StagedTooSmallSizedFormat,
                Exe(problem),
                bytes / 1024)
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.AppUpdate_StagedTooSmallFormat,
                Exe(problem)),

        AppUpdateFailure.UpdaterWouldNotStart => string.Format(
            CultureInfo.CurrentCulture,
            Strings.AppUpdate_UpdaterWouldNotStartFormat,
            problem.Folder),

        AppUpdateFailure.AppFolderNotWritable => string.Format(
            CultureInfo.CurrentCulture,
            Strings.AppUpdate_FolderNotWritableFormat,
            problem.Folder),

        AppUpdateFailure.NotEnoughFreeSpace => problem is { RequiredBytes: { } required, AvailableBytes: { } free }
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.AppUpdate_NoSpaceSizedFormat,
                problem.DriveName,
                required / (1024 * 1024),
                free / (1024 * 1024))
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.AppUpdate_NoSpaceFormat,
                problem.DriveName),

        // Only reachable if a case is added to AppUpdateFailure without one being added here. The
        // fallback is the same advice as an unexpected failure, because that is what it is.
        _ => Unexpected(problem),
    };

    //
    // Anything that got out of the installer without being one of the cases above - an IO error
    // nobody anticipated, a permissions oddity. There is nothing specific to say, so this says the
    // one thing that is always true and always useful.
    //
    public static string Unexpected(Exception problem) => string.Format(
        CultureInfo.CurrentCulture,
        Strings.AppUpdate_UnexpectedFormat,
        problem.Message);

    // The installer always fills these in for the cases that use them; the fallbacks exist so a
    // future case that forgets to still reads as a sentence.
    //
    // Both fallbacks are keyed rather than left in English, and both sit in the slot a version
    // number or a file name would occupy - a noun phrase standing in for a value, not a fragment of
    // the sentence around it, which is why these are spliced where a verb phrase would not be.
    //
    private static string Version(AppUpdateException problem) => problem.Version ?? Strings.AppUpdate_TheNewVersion;

    private static string Exe(AppUpdateException problem) => problem.ExeName ?? Strings.AppUpdate_TheAppExecutable;
}
