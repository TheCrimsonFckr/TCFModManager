using System.Globalization;
using System.Windows.Data;

namespace TCFModManager.App.Converters;

public sealed class PictureWhenShownConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [bool shown, string url] && shown && !string.IsNullOrWhiteSpace(url) ? url : null;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
