using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Converters;

// Colors a status icon by ModStatus, shared by the Browse, Installed and
// Dependencies pages so the same situation is always the same colour.
public sealed class ModStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ModStatus.Installed => ThemeBrush.Resolve(ThemeBrush.Success, Brushes.LimeGreen),
        ModStatus.UpdateAvailable => ThemeBrush.Resolve(ThemeBrush.Caution, Brushes.Goldenrod),
        ModStatus.NotInstalled => ThemeBrush.Resolve(ThemeBrush.Critical, Brushes.OrangeRed),
        ModStatus.NoCompatibleVersion => ThemeBrush.Resolve(ThemeBrush.Neutral, Brushes.Gray),
        ModStatus.Unknown => ThemeBrush.Resolve(ThemeBrush.Neutral, Brushes.Gray),
        ModStatus.Conflict => ThemeBrush.Resolve(ThemeBrush.Critical, Brushes.OrangeRed),
        ModStatus.Disabled => ThemeBrush.Resolve(ThemeBrush.Neutral, Brushes.Gray),
        _ => ThemeBrush.Resolve(ThemeBrush.Neutral, Brushes.Gray),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
