using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TCFModManagement.App.Converters;

// Visible when a bound int count is greater than zero, Collapsed when it's zero.
// Pass ConverterParameter="Invert" to flip that (Visible only when the count is zero).
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasItems = value is int count && count > 0;
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        return (hasItems != invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
