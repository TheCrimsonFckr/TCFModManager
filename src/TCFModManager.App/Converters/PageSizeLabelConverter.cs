using System.Globalization;
using System.Windows.Data;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Converters;

//
// The Per page entries: a size as "12 per page", and Endless (stored as 0) by name - OPEN-12 A8.
//
public sealed class PageSizeLabelConverter : IValueConverter
{
    public const int Endless = 0;

    public static string Label(int size) =>
        size == Endless ? Strings.Common_PageSizeEndless : LocalizationService.Text(Strings.Common_PageSizeFormat, size);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int size ? Label(size) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
