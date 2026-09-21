using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Turns an install's progress reports into the line shown under a queued download.
//
// Core reports which stage it is in and the numbers behind it; the sentence is written here, which
// is the same split every other Core failure already follows - see D10.
//
public static class ModInstallWording
{
    public static string Describe(ModInstallProgress progress) => progress.Stage switch
    {
        ModInstallStage.Downloading => Text(
            Strings.Install_StageDownloadingFormat, progress.Name, progress.Version),

        // The first extract report arrives before any entry has been read, and an archive that
        // will not say how many entries it holds never gets a total.
        ModInstallStage.Extracting => progress.Total > 0
            ? Text(Strings.Install_StageExtractingCountFormat, progress.Done, progress.Total)
            : progress.Done > 0
                ? Text(Strings.Install_StageExtractingUnknownFormat, progress.Done)
                : Strings.Install_StageExtracting,

        ModInstallStage.RemovingPrevious => Text(
            Strings.Install_StageRemovingFormat, progress.Version),

        ModInstallStage.Installing => progress.Total > 0
            ? Text(Strings.Install_StageInstallingCountFormat, progress.Done, progress.Total)
            : Text(Strings.Install_StageInstallingUnknownFormat, progress.Done),

        _ => Strings.Install_StageDone,
    };

    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);
}
