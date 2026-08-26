using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TCFModManager.App.Converters;

// Maps ModCardViewModel.IsCompatibleWithInstalledSpt (true/false/null) to a
// green/red/neutral brush for the SPT version text on a Browse card.
public sealed class CompatibilityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        true => ThemeBrush.Resolve(ThemeBrush.Success, Brushes.LimeGreen),
        false => ThemeBrush.Resolve(ThemeBrush.Critical, Brushes.OrangeRed),
        _ => ThemeBrush.Resolve(ThemeBrush.Neutral, Brushes.Gray),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
