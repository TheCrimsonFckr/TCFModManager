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

public enum ThemeSurface
{
    Window,
    Page,
    Cards,
    Menus,
}

public sealed record ThemeCheck(ThemeProblem Problem, double? Ratio = null, ThemeSurface? Surface = null)
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
        var surfaces = Surfaces(resolved);

        AddContrast(checks, resolved.TextPrimary!.Value, surfaces, MinBodyTextContrast, ThemeProblem.LowContrastPrimaryText);
        AddContrast(checks, resolved.TextSecondary!.Value, surfaces, MinBodyTextContrast, ThemeProblem.LowContrastSecondaryText);
        AddContrast(checks, resolved.TextTertiary!.Value, surfaces, MinTertiaryTextContrast, ThemeProblem.LowContrastTertiaryText);

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

    public static IReadOnlyList<(ThemeSurface Surface, ThemeColor Color)> Surfaces(ThemeColors resolved)
    {
        var window = resolved.Background!.Value.WithAlpha(0xFF);
        var page = ThemeMath.CompositeOver(resolved.Page!.Value, window);

        return
        [
            (ThemeSurface.Window, window),
            (ThemeSurface.Page, page),
            (ThemeSurface.Cards, ThemeMath.CompositeOver(resolved.Surface!.Value, page)),
            (ThemeSurface.Menus, ThemeMath.CompositeOver(resolved.Flyout!.Value, window)),
        ];
    }

    private static void AddContrast(List<ThemeCheck> checks, ThemeColor text, IReadOnlyList<(ThemeSurface Surface, ThemeColor Color)> surfaces, double minimum, ThemeProblem problem)
    {
        var worst = surfaces
            .Select(s => (s.Surface, Ratio: ThemeMath.ContrastRatio(text, s.Color)))
            .MinBy(s => s.Ratio);

        if (worst.Ratio < minimum) checks.Add(new(problem, Math.Round(worst.Ratio, 2), worst.Surface));
    }
}
