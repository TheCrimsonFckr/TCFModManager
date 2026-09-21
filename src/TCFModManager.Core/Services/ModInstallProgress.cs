namespace TCFModManager.Core.Services;

//
// Which part of an install is running. Core reports the stage and the numbers behind it; the App
// turns that into the sentence the user reads - see feedback_core_no_user_prose and D10.
//
public enum ModInstallStage
{
    Downloading,
    Extracting,
    RemovingPrevious,
    Installing,
    Done,
}

//
// One progress report from an install.
//
// Name, Version and the two counts are only filled in for the stages that have them: Downloading
// names the mod, RemovingPrevious names the version being replaced, and Extracting and Installing
// carry how far through the files they are. Total is 0 when the archive would not say how many
// entries it holds.
//
public readonly record struct ModInstallProgress(
    ModInstallStage Stage,
    string? Name = null,
    string? Version = null,
    int Done = 0,
    int Total = 0);
