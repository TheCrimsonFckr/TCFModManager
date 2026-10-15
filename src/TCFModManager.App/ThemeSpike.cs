using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCFModManager.Core.Services;
using Wpf.Ui.Appearance;

namespace TCFModManager.App;

// SPIKE OPEN-29 S0 - throwaway. Delete with its two calls in AppTheme once K1, K2 and K4 are answered.
internal static class ThemeSpike
{
    private const string Area = "ThemeSpike";
    private const string FontKey = "TcfSpikeFontFamily";

    private static readonly string[] RadiusKeys = ["ControlCornerRadius", "OverlayCornerRadius", "PopupCornerRadius"];

    private static readonly string[] Probes =
    [
        "ApplicationBackgroundBrush", "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
        "CardBackgroundFillColorDefaultBrush", "ButtonBackground", "ControlStrokeColorDefaultBrush",
        "SystemFillColorCautionBrush", "AccentFillColorDefaultBrush", "BadgeBackground",
        "RowHoverBrush", "SelectedRowStroke", "PaletteLightBlueBrush",
    ];

    private sealed record Preset(ApplicationTheme Base, Color? Accent, Dictionary<string, Color> Colors);

    private static readonly Dictionary<string, Preset> Presets = new()
    {
        ["shadow"] = new(ApplicationTheme.Dark, null, new()),
        ["shadowlight"] = new(ApplicationTheme.Light, null, new()),
        ["oled"] = new(ApplicationTheme.Dark, null, new()
        {
            ["ApplicationBackgroundColor"] = C("#FF000000"),
            ["SolidBackgroundFillColorBase"] = C("#000000"),
            ["SolidBackgroundFillColorSecondary"] = C("#000000"),
            ["SolidBackgroundFillColorTertiary"] = C("#0B0B0B"),
            ["SolidBackgroundFillColorQuarternary"] = C("#121212"),
            ["SolidBackgroundFillColorBaseAlt"] = C("#000000"),
            ["LayerFillColorDefault"] = C("#4C000000"),
            ["TextFillColorSecondary"] = C("#C8FFFFFF"),
            ["TextFillColorTertiary"] = C("#8AFFFFFF"),
        }),
        ["tarkov"] = new(ApplicationTheme.Dark, C("#B4A46B"), new()
        {
            ["ApplicationBackgroundColor"] = C("#FF12120F"),
            ["SolidBackgroundFillColorBase"] = C("#12120F"),
            ["SolidBackgroundFillColorSecondary"] = C("#0E0E0C"),
            ["SolidBackgroundFillColorTertiary"] = C("#1C1B17"),
            ["SolidBackgroundFillColorQuarternary"] = C("#23221D"),
            ["SolidBackgroundFillColorBaseAlt"] = C("#0A0A08"),
            ["LayerFillColorDefault"] = C("#4C2A2820"),
            ["CardBackgroundFillColorDefault"] = C("#0FE8DFC0"),
            ["CardBackgroundFillColorSecondary"] = C("#08E8DFC0"),
            ["TextFillColorPrimary"] = C("#D9D4C3"),
            ["TextFillColorSecondary"] = C("#A39E8B"),
            ["TextFillColorTertiary"] = C("#77735F"),
            ["ControlStrokeColorDefault"] = C("#1AD9CFA8"),
            ["DividerStrokeColorDefault"] = C("#1FD9CFA8"),
            ["SystemFillColorSuccess"] = C("#8BAA55"),
            ["SystemFillColorCaution"] = C("#D6A845"),
            ["SystemFillColorCritical"] = C("#C8604F"),
        }),
        ["hc"] = new(ApplicationTheme.Dark, C("#FFD400"), new()
        {
            ["ApplicationBackgroundColor"] = C("#FF000000"),
            ["SolidBackgroundFillColorBase"] = C("#000000"),
            ["SolidBackgroundFillColorSecondary"] = C("#000000"),
            ["SolidBackgroundFillColorTertiary"] = C("#101010"),
            ["SolidBackgroundFillColorQuarternary"] = C("#181818"),
            ["CardBackgroundFillColorDefault"] = C("#14FFFFFF"),
            ["TextFillColorSecondary"] = C("#F0FFFFFF"),
            ["TextFillColorTertiary"] = C("#D0FFFFFF"),
            ["TextFillColorDisabled"] = C("#90FFFFFF"),
            ["ControlStrokeColorDefault"] = C("#80FFFFFF"),
            ["ControlStrokeColorSecondary"] = C("#A0FFFFFF"),
            ["CardStrokeColorDefault"] = C("#80FFFFFF"),
            ["DividerStrokeColorDefault"] = C("#80FFFFFF"),
        }),
        ["soft"] = new(ApplicationTheme.Dark, C("#7AA2D6"), new()
        {
            ["ApplicationBackgroundColor"] = C("#FF2A2C30"),
            ["SolidBackgroundFillColorBase"] = C("#2A2C30"),
            ["SolidBackgroundFillColorSecondary"] = C("#26282B"),
            ["SolidBackgroundFillColorTertiary"] = C("#33363B"),
            ["SolidBackgroundFillColorQuarternary"] = C("#383B40"),
            ["TextFillColorPrimary"] = C("#DADBDD"),
            ["TextFillColorSecondary"] = C("#A9ABAF"),
            ["TextFillColorTertiary"] = C("#7F8186"),
        }),
    };

    private static ResourceDictionary? _shadow;
    private static ApplicationTheme _shadowBase;
    private static readonly List<string> Written = [];
    private static bool _applying;
    private static bool _hooked;

    public static void ApplyIfRequested()
    {
        var name = Environment.GetEnvironmentVariable("TCFMM_THEME_SPIKE")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name) || _applying || Application.Current is not { } app) return;

        if (!Presets.TryGetValue(name, out var preset))
        {
            AppLog.Warn(Area, $"unknown preset '{name}' - expected one of {string.Join(", ", Presets.Keys)}");
            return;
        }

        _applying = true;
        try
        {
            if (ApplicationThemeManager.GetAppTheme() != preset.Base)
            {
                ApplicationThemeManager.Apply(preset.Base, Wpf.Ui.Controls.WindowBackdropType.None);
            }

            if (preset.Accent is { } accent)
            {
                var toward = preset.Base == ApplicationTheme.Dark ? Colors.White : Colors.Black;
                ApplicationAccentColorManager.Apply(accent, Mix(accent, toward, 0.15), Mix(accent, toward, 0.30), Mix(accent, toward, 0.45));
            }

            MergeShadow(app, preset.Base);

            foreach (var key in Written) app.Resources.Remove(key);
            Written.Clear();

            foreach (var (key, color) in preset.Colors)
            {
                app.Resources[key] = color;
                Written.Add(key);
            }

            ApplyRadius(app);
            ApplyFont(app);

            app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                foreach (Window window in app.Windows)
                {
                    window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
                }

                AppLog.Info(Area, $"deferred: window background {Describe(app.MainWindow?.Background)}, "
                    + $"RowHoverBrush {Describe(app.TryFindResource("RowHoverBrush"))}, "
                    + $"SystemAccentColor {app.TryFindResource("SystemAccentColor")}");
            });

            AppLog.Info(Area, $"applied '{name}' on {preset.Base}, {preset.Colors.Count} colours, merged: "
                + string.Join(" | ", app.Resources.MergedDictionaries.Select(d => d.Source?.ToString() ?? "(inline)")));
            foreach (var key in Probes)
            {
                AppLog.Info(Area, $"  {key} = {Describe(app.TryFindResource(key))}");
            }
            AppLog.Info(Area, $"  SystemAccentColor = {app.TryFindResource("SystemAccentColor")}, primary {app.TryFindResource("SystemAccentColorPrimary")}, secondary {app.TryFindResource("SystemAccentColorSecondary")}, tertiary {app.TryFindResource("SystemAccentColorTertiary")}");
            AppLog.Info(Area, $"  ControlCornerRadius = {app.TryFindResource("ControlCornerRadius")}");
        }
        catch (Exception ex)
        {
            AppLog.Warn(Area, $"failed: {ex}");
        }
        finally
        {
            _applying = false;
        }
    }

    private static void MergeShadow(Application app, ApplicationTheme theme)
    {
        if (_shadow is not null && _shadowBase == theme && app.Resources.MergedDictionaries.Contains(_shadow)) return;

        if (_shadow is not null) app.Resources.MergedDictionaries.Remove(_shadow);

        var file = theme == ApplicationTheme.Light ? "Light" : "Dark";
        _shadow = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/ThemeBrushes.{file}.xaml") };
        _shadowBase = theme;
        app.Resources.MergedDictionaries.Add(_shadow);
    }

    private static void ApplyRadius(Application app)
    {
        var raw = Environment.GetEnvironmentVariable("TCFMM_THEME_SPIKE_RADIUS");
        if (!double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var r)) return;

        foreach (var key in RadiusKeys)
        {
            app.Resources[key] = new CornerRadius(key == "ControlCornerRadius" ? r : r * 2);
            if (!Written.Contains(key)) Written.Add(key);
        }
    }

    private static void ApplyFont(Application app)
    {
        var raw = Environment.GetEnvironmentVariable("TCFMM_THEME_SPIKE_FONT");
        if (string.IsNullOrWhiteSpace(raw)) return;

        app.Resources[FontKey] = new FontFamily(raw);
        if (!Written.Contains(FontKey)) Written.Add(FontKey);

        foreach (Window window in app.Windows) Bind(window);

        if (_hooked) return;
        _hooked = true;

        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => Bind((FrameworkElement)s)), true);
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent,
            new RoutedEventHandler((s, _) => Bind((FrameworkElement)s)), true);
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler((s, _) => Bind((FrameworkElement)s)), true);
    }

    private static void Bind(FrameworkElement element) =>
        element.SetResourceReference(Control.FontFamilyProperty, FontKey);

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static string Describe(object? value) => value switch
    {
        null => "missing",
        SolidColorBrush b => $"{b.Color} op={b.Opacity} (frozen={b.IsFrozen}, hash={b.GetHashCode():X})",
        _ => value.GetType().Name,
    };
}
