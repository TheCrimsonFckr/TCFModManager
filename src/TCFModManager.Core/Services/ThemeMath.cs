using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public static class ThemeMath
{
    public static readonly ThemeColor White = ThemeColor.FromRgb(0xFF, 0xFF, 0xFF);

    public static readonly ThemeColor Black = ThemeColor.FromRgb(0x00, 0x00, 0x00);

    public static readonly double[] AccentShadeSteps = [0.15, 0.30, 0.45];

    public static ThemeColor Mix(ThemeColor from, ThemeColor to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);

        return new ThemeColor(
            Lerp(from.A, to.A, amount),
            Lerp(from.R, to.R, amount),
            Lerp(from.G, to.G, amount),
            Lerp(from.B, to.B, amount));
    }

    public static ThemeAccentShades AccentShades(ThemeColor accent, ThemeBase themeBase)
    {
        var toward = themeBase == ThemeBase.Dark ? White : Black;
        var solid = accent.WithAlpha(0xFF);

        return new ThemeAccentShades(
            Mix(solid, toward, AccentShadeSteps[0]),
            Mix(solid, toward, AccentShadeSteps[1]),
            Mix(solid, toward, AccentShadeSteps[2]));
    }

    public static ThemeAccentShades AccentShadesFor(ThemeDefinition theme) =>
        theme.AccentShades
        ?? (theme.Accent is { } accent ? AccentShades(accent, theme.Base) : null)
        ?? throw new InvalidOperationException("The theme has no accent of its own.");

    public static ThemeColor CompositeOver(ThemeColor top, ThemeColor bottom)
    {
        var below = bottom.IsOpaque ? bottom : CompositeOver(bottom, Black);
        var alpha = top.A / 255.0;

        return ThemeColor.FromRgb(
            Blend(top.R, below.R, alpha),
            Blend(top.G, below.G, alpha),
            Blend(top.B, below.B, alpha));
    }

    public static double RelativeLuminance(ThemeColor color)
    {
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }

    public static double ContrastRatio(ThemeColor foreground, ThemeColor background)
    {
        var bottom = background.IsOpaque ? background : CompositeOver(background, Black);
        var top = CompositeOver(foreground, bottom);

        var lighter = Math.Max(RelativeLuminance(top), RelativeLuminance(bottom));
        var darker = Math.Min(RelativeLuminance(top), RelativeLuminance(bottom));
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static ThemeColor PreferredTextOn(ThemeColor fill) =>
        ContrastRatio(Black, fill) >= ContrastRatio(White, fill) ? Black : White;

    public static ThemeColors BaseColors(ThemeBase themeBase) => themeBase == ThemeBase.Dark
        ? new ThemeColors
        {
            Background = ThemeColor.Parse("#202020"),
            Page = ThemeColor.Parse("#4C3A3A3A"),
            Surface = ThemeColor.Parse("#0DFFFFFF"),
            SurfaceSecondary = ThemeColor.Parse("#08FFFFFF"),
            Flyout = ThemeColor.Parse("#2C2C2C"),
            TextPrimary = ThemeColor.Parse("#FFFFFF"),
            TextSecondary = ThemeColor.Parse("#C5FFFFFF"),
            TextTertiary = ThemeColor.Parse("#87FFFFFF"),
            TextDisabled = ThemeColor.Parse("#5DFFFFFF"),
            Stroke = ThemeColor.Parse("#12FFFFFF"),
            Divider = ThemeColor.Parse("#15FFFFFF"),
            Success = ThemeColor.Parse("#6CCB5F"),
            Caution = ThemeColor.Parse("#FCE100"),
            Critical = ThemeColor.Parse("#FF99A4"),
            Attention = ThemeColor.Parse("#4CC2FF"),
        }
        : new ThemeColors
        {
            Background = ThemeColor.Parse("#FAFAFA"),
            Page = ThemeColor.Parse("#80FFFFFF"),
            Surface = ThemeColor.Parse("#B3FFFFFF"),
            SurfaceSecondary = ThemeColor.Parse("#80F6F6F6"),
            Flyout = ThemeColor.Parse("#F9F9F9"),
            TextPrimary = ThemeColor.Parse("#E4000000"),
            TextSecondary = ThemeColor.Parse("#9E000000"),
            TextTertiary = ThemeColor.Parse("#72000000"),
            TextDisabled = ThemeColor.Parse("#5C000000"),
            Stroke = ThemeColor.Parse("#0F000000"),
            Divider = ThemeColor.Parse("#0F000000"),
            Success = ThemeColor.Parse("#0F7B0F"),
            Caution = ThemeColor.Parse("#9D5D00"),
            Critical = ThemeColor.Parse("#C42B1C"),
            Attention = ThemeColor.Parse("#0078D4"),
        };

    public static ThemeColors Resolve(ThemeDefinition theme)
    {
        var fallback = BaseColors(theme.Base);
        var own = theme.Colors;

        return own with
        {
            Background = own.Background ?? fallback.Background,
            Page = own.Page ?? fallback.Page,
            Surface = own.Surface ?? fallback.Surface,
            SurfaceSecondary = own.SurfaceSecondary ?? fallback.SurfaceSecondary,
            Flyout = own.Flyout ?? fallback.Flyout,
            TextPrimary = own.TextPrimary ?? fallback.TextPrimary,
            TextSecondary = own.TextSecondary ?? fallback.TextSecondary,
            TextTertiary = own.TextTertiary ?? fallback.TextTertiary,
            TextDisabled = own.TextDisabled ?? fallback.TextDisabled,
            Stroke = own.Stroke ?? fallback.Stroke,
            Divider = own.Divider ?? fallback.Divider,
            Success = own.Success ?? fallback.Success,
            Caution = own.Caution ?? fallback.Caution,
            Critical = own.Critical ?? fallback.Critical,
            Attention = own.Attention ?? fallback.Attention,
        };
    }

    public static ThemeColor CardColor(ThemeColors resolved)
    {
        var page = CompositeOver(resolved.Page!.Value, resolved.Background!.Value);
        return CompositeOver(resolved.Surface!.Value, page);
    }

    private static byte Lerp(byte from, byte to, double amount) =>
        (byte)Math.Round(from + (to - from) * amount);

    private static byte Blend(byte top, byte bottom, double alpha) =>
        (byte)Math.Round(top * alpha + bottom * (1 - alpha), MidpointRounding.AwayFromZero);
}
