using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TCFModManagement.App.Converters;

// Maps ModCardViewModel.IsCompatibleWithInstalledSpt (true/false/null) to a
// green/red/neutral brush for the SPT version text on a Browse card.
public sealed class CompatibilityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        true => Brushes.LimeGreen,
        false => Brushes.OrangeRed,
        _ => Brushes.Gray,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
