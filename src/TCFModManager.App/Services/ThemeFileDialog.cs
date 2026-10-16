using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

public static class ThemeFileDialog
{
    public static string Filter =>
        $"{Strings.ThemeFile_TypeName} ({ThemeFile.FilePattern})|{ThemeFile.FilePattern}"
        + $"|{Strings.ModListFile_AllFiles} ({ThemeFile.AllFilesPattern})|{ThemeFile.AllFilesPattern}";
}
