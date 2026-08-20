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
        ModStatus.Installed => Brushes.LimeGreen,
        ModStatus.UpdateAvailable => Brushes.Goldenrod,
        ModStatus.NotInstalled => Brushes.OrangeRed,
        ModStatus.NoCompatibleVersion => Brushes.Gray,
        ModStatus.Unknown => Brushes.Gray,
        ModStatus.Conflict => Brushes.OrangeRed,
        _ => Brushes.Gray,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
