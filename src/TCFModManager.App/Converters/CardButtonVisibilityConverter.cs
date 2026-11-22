using System.Globalization;
using System.Windows;
using System.Windows.Data;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Converters;

public sealed class CardButtonVisibilityConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var spec = parameter as string ?? string.Empty;
        var listOnly = spec.EndsWith("|list", StringComparison.Ordinal);
        var key = listOnly ? spec[..^"|list".Length] : spec;
        var inList = values.Length > 1 && values[1] is true;

        if (listOnly && !inList) return Visibility.Collapsed;
        if (values.Length == 0 || values[0] is not CardLayoutView layout) return Visibility.Visible;

        return (inList ? layout.ListHas(key) : layout.Has(key)) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
