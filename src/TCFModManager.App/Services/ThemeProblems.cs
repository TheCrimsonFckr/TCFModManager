using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

public static class ThemeProblems
{
    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);

    public static string Describe(ThemeCheck check) => check.Problem switch
    {
        ThemeProblem.NameMissing => Strings.ThemeEditor_ProblemNameMissing,
        ThemeProblem.NameTooLong => Text(Strings.ThemeEditor_ProblemNameTooLongFormat, ThemeRules.NameMaxLength),
        ThemeProblem.FontFamilyTooLong => Text(Strings.ThemeEditor_ProblemFontTooLongFormat, ThemeRules.FontFamilyMaxLength),
        ThemeProblem.CornerRadiusOutOfRange => Text(Strings.ThemeEditor_ProblemRadiusFormat, ThemeRules.CornerRadiusMax),
        ThemeProblem.LowContrastPrimaryText => Contrast(Strings.ThemeEditor_ProblemContrastPrimaryFormat, check, ThemeRules.MinBodyTextContrast),
        ThemeProblem.LowContrastSecondaryText => Contrast(Strings.ThemeEditor_ProblemContrastSecondaryFormat, check, ThemeRules.MinBodyTextContrast),
        ThemeProblem.LowContrastTertiaryText => Contrast(Strings.ThemeEditor_ProblemContrastTertiaryFormat, check, ThemeRules.MinTertiaryTextContrast),
        _ => check.Problem.ToString(),
    };

    public static string Describe(ThemeFileProblem problem) => problem switch
    {
        ThemeFileProblem.NotATheme => Strings.ThemeEditor_ImportNotATheme,
        ThemeFileProblem.NewerVersion => Strings.ThemeEditor_ImportNewer,
        _ => Strings.ThemeEditor_ImportUnreadable,
    };

    private static string Contrast(string format, ThemeCheck check, double target) =>
        Text(format, (check.Ratio ?? 0).ToString("0.0"), target.ToString("0.0"));
}
