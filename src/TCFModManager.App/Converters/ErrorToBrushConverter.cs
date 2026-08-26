using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TCFModManager.App.Converters;

// Maps DataFilesViewModel.HasError to a red/neutral brush for the status line in DataFilesWindow.
public sealed class ErrorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true
            ? ThemeBrush.Resolve(ThemeBrush.Critical, Brushes.OrangeRed)
            : ThemeBrush.Resolve(ThemeBrush.Primary, Brushes.White);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
