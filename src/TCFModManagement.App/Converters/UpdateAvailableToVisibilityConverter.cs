using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TCFModManagement.App.Converters;

// Shows an element only when the bound value is true - false and null both collapse it.
public sealed class UpdateAvailableToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
