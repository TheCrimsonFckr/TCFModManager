using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The filter the Save and Open dialogs are given when a mod list is exported or imported.
//
// Half of what a Win32 filter string contains is a wildcard the dialog matches on and half is a
// name the user reads, which is why the whole of it cannot live in Core: the patterns are data and
// stay there with the format, the two names are prose and belong here with the rest of it.
//
public static class ModListFileDialog
{
    public static string Filter =>
        $"{Strings.ModListFile_TypeName} ({ModListFile.FilePattern})|{ModListFile.FilePattern}"
        + $"|{Strings.ModListFile_AllFiles} ({ModListFile.AllFilesPattern})|{ModListFile.AllFilesPattern}";
}
