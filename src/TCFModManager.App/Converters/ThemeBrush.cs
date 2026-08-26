using System.Windows;
using System.Windows.Media;

namespace TCFModManager.App.Converters;

//
// Looks up one of WPF-UI's theme brushes by key, for the converters that have to return a Brush
// rather than being able to say {DynamicResource ...} in XAML.
//
// The literal fallbacks are what these colours were before theming, so a key that ever went missing
// from a future WPF-UI release degrades to the old appearance instead of to nothing.
//
// **Known limitation:** a brush handed back from a converter is whatever the theme was when the
// binding last ran, so switching theme while the app is open leaves these particular icons on the
// previous theme's colour until their binding re-evaluates. They stay perfectly legible either way -
// these are saturated status colours, not body text - so it is a cosmetic lag rather than the
// white-on-white problem the XAML sweep was fixing. The proper fix is styles with DataTriggers on
// ModStatus using DynamicResource at each usage site; worth doing if it ever grates.
//
internal static class ThemeBrush
{
    public const string Success = "SystemFillColorSuccessBrush";
    public const string Caution = "SystemFillColorCautionBrush";
    public const string Critical = "SystemFillColorCriticalBrush";
    public const string Neutral = "TextFillColorTertiaryBrush";
    public const string Primary = "TextFillColorPrimaryBrush";

    public static Brush Resolve(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
