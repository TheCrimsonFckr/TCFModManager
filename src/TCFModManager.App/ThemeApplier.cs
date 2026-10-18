using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Wpf.Ui.Appearance;
using MediaColor = System.Windows.Media.Color;

namespace TCFModManager.App;

internal static class ThemeApplier
{
    private const string FontKey = "AppThemeFontFamily";
    private const double BadgeTextContrast = 3.0;
    private const double StatusBackgroundMix = 0.18;

    private static readonly (string Key, double Scale)[] RadiusKeys =
    [
        ("ControlCornerRadius", 1),
        ("OverlayCornerRadius", 2),
        ("PopupCornerRadius", 2),
        ("SmallCornerRadius", 0.75),
    ];

    private static readonly string[] AccentTintKeys = ["RowHoverBrush", "RowSelectedBrush", "SelectedRowStroke"];

    private static readonly HashSet<string> Written = new(StringComparer.Ordinal);
    private static ResourceDictionary? _shadow;
    private static bool _fontActive;
    private static bool _fontHooked;
    private static bool _backgroundHooked;

    public static ThemeDefinition? Active { get; private set; }

    public static void Apply(ThemeDefinition theme)
    {
        if (Application.Current is not { } app) return;

        RemoveWritten(app);

        var resolved = ThemeMath.Resolve(theme);
        var own = theme.Colors;

        ApplyAccent(theme);
        WriteSeeds(app, own, resolved);
        WriteStatusBrushes(app, theme, own, resolved);
        WriteFootprint(app, own);

        foreach (var (key, color) in theme.Overrides) WriteColor(app, key, color);

        ApplyRadius(app, theme.CornerRadius);
        ApplyFont(app, theme.FontFamily);

        MergeShadow(app, theme.Base);
        RefreshAccentTints(app, theme.Accent ?? theme.AccentShades?.Primary);

        Active = theme;
        ReferenceWindowBackgrounds(app);

        AppLog.Info("Theme", $"applied '{theme.Id}' on {theme.Base}, {Written.Count} keys");
    }

    public static void Clear()
    {
        if (Application.Current is not { } app || Active is null) return;

        RemoveWritten(app);

        if (_shadow is not null)
        {
            app.Resources.MergedDictionaries.Remove(_shadow);
            _shadow = null;
        }

        RefreshAccentTints(app, null);

        if (_fontActive)
        {
            foreach (Window window in app.Windows) window.ClearValue(Control.FontFamilyProperty);
            _fontActive = false;
        }

        Active = null;
        AppLog.Info("Theme", "custom theme cleared");
    }

    public static void ReferenceWindowBackgrounds(Application app)
    {
        if (Active is null) return;

        if (!_backgroundHooked)
        {
            _backgroundHooked = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) =>
                {
                    if (Active is not null && s is Window window)
                        window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
                }), true);
        }

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (Active is null) return;
            foreach (Window window in app.Windows)
            {
                window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
            }
        });
    }

    private static void ApplyAccent(ThemeDefinition theme)
    {
        if (theme.Accent is null && theme.AccentShades is null) return;

        var shades = ThemeMath.AccentShadesFor(theme);
        var accent = theme.Accent ?? shades.Primary;

        ApplicationAccentColorManager.Apply(ToMedia(accent), ToMedia(shades.Primary), ToMedia(shades.Secondary), ToMedia(shades.Tertiary));
    }

    private static void WriteSeeds(Application app, ThemeColors own, ThemeColors resolved)
    {
        if (own.Background is { } background)
        {
            var solid = background.WithAlpha(0xFF);
            WriteColor(app, "ApplicationBackgroundColor", solid);
            WriteColor(app, "SolidBackgroundFillColorBase", solid);
            WriteColor(app, "SolidBackgroundFillColorSecondary", ThemeMath.Mix(solid, ThemeMath.Black, 0.15));
            WriteColor(app, "SolidBackgroundFillColorTertiary", ThemeMath.Mix(solid, ThemeMath.White, 0.05));
            WriteColor(app, "SolidBackgroundFillColorQuarternary", ThemeMath.Mix(solid, ThemeMath.White, 0.08));
            WriteColor(app, "SolidBackgroundFillColorBaseAlt", ThemeMath.Mix(solid, ThemeMath.Black, 0.5));
        }

        Write(app, own.Page, "LayerFillColorDefault");
        Write(app, own.Surface, "CardBackgroundFillColorDefault");
        Write(app, own.SurfaceSecondary, "CardBackgroundFillColorSecondary");
        Write(app, own.Flyout, "AcrylicBackgroundFillColorDefault", "LayerOnMicaBaseAltFillColorTertiary", "ControlOnImageFillColorSecondary");

        Write(app, own.TextPrimary, "TextFillColorPrimary");
        Write(app, own.TextSecondary, "TextFillColorSecondary");
        Write(app, own.TextTertiary, "TextFillColorTertiary", "TextPlaceholderColor");
        Write(app, own.TextDisabled, "TextFillColorDisabled");

        if (own.Stroke is { } stroke)
        {
            WriteColor(app, "ControlStrokeColorDefault", stroke);
            WriteColor(app, "ControlStrokeColorSecondary", stroke.WithAlpha((byte)Math.Min(255, stroke.A * 4 / 3)));
        }

        Write(app, own.Divider, "DividerStrokeColorDefault");

        var page = ThemeMath.CompositeOver(resolved.Page!.Value, resolved.Background!.Value);
        WriteStatus(app, own.Success, page, "SystemFillColorSuccess");
        WriteStatus(app, own.Caution, page, "SystemFillColorCaution");
        WriteStatus(app, own.Critical, page, "SystemFillColorCritical");
        WriteStatus(app, own.Attention, page, "SystemFillColorAttention");
    }

    private static void WriteStatus(Application app, ThemeColor? color, ThemeColor page, string key)
    {
        if (color is not { } c) return;

        WriteColor(app, key, c);
        WriteColor(app, key + "Background", ThemeMath.Mix(page, c.WithAlpha(0xFF), StatusBackgroundMix));
    }

    private static void WriteStatusBrushes(Application app, ThemeDefinition theme, ThemeColors own, ThemeColors resolved)
    {
        if (own.Attention is { } attention) WriteBrush(app, "SystemFillColorAttentionBrush", attention);

        var badgeText = theme.Accent is not null || theme.AccentShades is not null
            ? ThemeMath.PreferredTextOn(ThemeMath.AccentShadesFor(theme).Secondary)
            : ThemeMath.White;

        var info = theme.Accent is not null || theme.AccentShades is not null
            ? ThemeMath.AccentShadesFor(theme).Primary
            : own.Attention;

        if (info is { } i) WriteBrush(app, "PaletteLightBlueBrush", ThemeMath.ReadableAgainst(i, badgeText, BadgeTextContrast));
        if (own.Caution is { } caution) WriteBrush(app, "PaletteOrangeBrush", ThemeMath.ReadableAgainst(caution, badgeText, BadgeTextContrast));
        if (own.Critical is { } critical) WriteBrush(app, "PaletteRedBrush", ThemeMath.ReadableAgainst(critical, badgeText, BadgeTextContrast));
        if (own.Success is { } success) WriteBrush(app, "PaletteGreenBrush", ThemeMath.ReadableAgainst(success, badgeText, BadgeTextContrast));
    }

    private static void WriteFootprint(Application app, ThemeColors own)
    {
        Write(app, own.FootprintCpu, "FootprintCpuColor");
        Write(app, own.FootprintGpu, "FootprintGpuColor");
        Write(app, own.FootprintMemory, "FootprintMemoryColor");
        Write(app, own.FootprintStartup, "FootprintStartupColor");
        Write(app, own.FootprintDisk, "FootprintDiskColor");
        Write(app, own.FootprintNeutral, "FootprintNeutralColor");
    }

    private static void ApplyRadius(Application app, double? radius)
    {
        if (radius is not { } r) return;

        foreach (var (key, scale) in RadiusKeys)
        {
            app.Resources[key] = new CornerRadius(r * scale);
            Written.Add(key);
        }
    }

    private static void ApplyFont(Application app, string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            if (_fontActive)
            {
                foreach (Window window in app.Windows) window.ClearValue(Control.FontFamilyProperty);
                _fontActive = false;
            }

            return;
        }

        app.Resources[FontKey] = new FontFamily(family);
        Written.Add(FontKey);
        _fontActive = true;

        foreach (Window window in app.Windows) BindFont(window);

        if (_fontHooked) return;
        _fontHooked = true;

        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => BindFont((FrameworkElement)s)), true);
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent,
            new RoutedEventHandler((s, _) => BindFont((FrameworkElement)s)), true);
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler((s, _) => BindFont((FrameworkElement)s)), true);
    }

    private static void BindFont(FrameworkElement element)
    {
        if (_fontActive) element.SetResourceReference(Control.FontFamilyProperty, FontKey);
        else if (element is ToolTip or ContextMenu) element.ClearValue(Control.FontFamilyProperty);
    }

    private static void MergeShadow(Application app, ThemeBase themeBase)
    {
        var file = themeBase == ThemeBase.Light ? "Light" : "Dark";
        var fresh = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/ThemeBrushes.{file}.xaml") };

        if (_shadow is not null) app.Resources.MergedDictionaries.Remove(_shadow);
        app.Resources.MergedDictionaries.Add(fresh);
        _shadow = fresh;
    }

    private static void RefreshAccentTints(Application app, ThemeColor? accent)
    {
        foreach (var key in AccentTintKeys)
        {
            if (app.Resources[key] is not SolidColorBrush { IsFrozen: false } brush) continue;

            if (accent is { } c) brush.Color = ToMedia(c.WithAlpha(0xFF));
            else brush.SetValue(SolidColorBrush.ColorProperty, new DynamicResourceExtension("SystemAccentColor").ProvideValue(null));
        }
    }

    private static void Write(Application app, ThemeColor? color, params string[] keys)
    {
        if (color is not { } c) return;
        foreach (var key in keys) WriteColor(app, key, c);
    }

    private static void WriteColor(Application app, string key, ThemeColor color)
    {
        app.Resources[key] = ToMedia(color);
        Written.Add(key);
    }

    private static void WriteBrush(Application app, string key, ThemeColor color)
    {
        var brush = new SolidColorBrush(ToMedia(color));
        brush.Freeze();
        app.Resources[key] = brush;
        Written.Add(key);
    }

    private static void RemoveWritten(Application app)
    {
        foreach (var key in Written) app.Resources.Remove(key);
        Written.Clear();
    }

    private static MediaColor ToMedia(ThemeColor color) => MediaColor.FromArgb(color.A, color.R, color.G, color.B);
}
