using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public enum ThemeProblem
{
    NameMissing,
    NameTooLong,
    FontFamilyTooLong,
    CornerRadiusOutOfRange,
    LowContrastPrimaryText,
    LowContrastSecondaryText,
    LowContrastTertiaryText,
}

public sealed record ThemeCheck(ThemeProblem Problem, double? Ratio = null)
{
    public bool BlocksSave => Problem is ThemeProblem.NameMissing
        or ThemeProblem.NameTooLong
        or ThemeProblem.FontFamilyTooLong
        or ThemeProblem.CornerRadiusOutOfRange;
}

public static class ThemeRules
{
    public const int NameMaxLength = 40;

    public const int FontFamilyMaxLength = 64;

    public const double CornerRadiusMax = 16;

    public const double MinBodyTextContrast = 4.5;

    public const double MinTertiaryTextContrast = 3.0;

    public static IReadOnlyList<ThemeCheck> Check(ThemeDefinition theme)
    {
        var checks = new List<ThemeCheck>();
        var name = theme.Name?.Trim() ?? "";

        if (name.Length == 0) checks.Add(new(ThemeProblem.NameMissing));
        else if (name.Length > NameMaxLength) checks.Add(new(ThemeProblem.NameTooLong));

        if (theme.FontFamily is { } font && font.Trim().Length > FontFamilyMaxLength)
        {
            checks.Add(new(ThemeProblem.FontFamilyTooLong));
        }

        if (theme.CornerRadius is { } radius && (double.IsNaN(radius) || radius < 0 || radius > CornerRadiusMax))
        {
            checks.Add(new(ThemeProblem.CornerRadiusOutOfRange));
        }

        var resolved = ThemeMath.Resolve(theme);
        var card = ThemeMath.CardColor(resolved);

        AddContrast(checks, resolved.TextPrimary!.Value, card, MinBodyTextContrast, ThemeProblem.LowContrastPrimaryText);
        AddContrast(checks, resolved.TextSecondary!.Value, card, MinBodyTextContrast, ThemeProblem.LowContrastSecondaryText);
        AddContrast(checks, resolved.TextTertiary!.Value, card, MinTertiaryTextContrast, ThemeProblem.LowContrastTertiaryText);

        return checks;
    }

    public static bool CanSave(ThemeDefinition theme) => !Check(theme).Any(c => c.BlocksSave);

    public static void Normalise(ThemeDefinition theme)
    {
        theme.Name = (theme.Name ?? "").Trim();
        theme.FontFamily = string.IsNullOrWhiteSpace(theme.FontFamily) ? null : theme.FontFamily.Trim();
        theme.Overrides ??= [];
        theme.Colors ??= new ThemeColors();
    }

    private static void AddContrast(List<ThemeCheck> checks, ThemeColor text, ThemeColor surface, double minimum, ThemeProblem problem)
    {
        var ratio = ThemeMath.ContrastRatio(text, surface);
        if (ratio < minimum) checks.Add(new(problem, Math.Round(ratio, 2)));
    }
}
