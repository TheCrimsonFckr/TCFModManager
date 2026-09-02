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
// **The converters that used this are gone.** Every status colour is now a Style with DataTriggers
// carrying DynamicResource, which re-resolves on a theme change - see StatusIcon / WorstStatusIcon /
// CompatibilityText / ErrorText in App.xaml. That was the fix this comment used to describe as
// "worth doing if it ever grates".
//
// What remains here are the two places a brush genuinely has to be produced from code rather than
// markup: HtmlText building a FlowDocument, and ModDisableConfirmationWindow's severity colour.
// Both still freeze at the theme in force when they run. Neither has been a problem - the dialog is
// built fresh each time it opens - but prefer SetResourceReference over this when the target is a
// DependencyObject that outlives a theme switch.
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
