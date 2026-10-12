using System.Globalization;
using System.Windows.Data;

namespace TCFModManager.App.Converters;

//
// A height that fills what the window shows: the page's visible height (bound from the scroller the
// page sits in) less ConverterParameter, the room left for the header and buttons around it, and
// never under 300. The Play page's server log, so it grows and shrinks with the window (Chris,
// 2026-10-12: "fill whatever space the window has").
//
public sealed class ViewportFillConverter : IValueConverter
{
    private const double Minimum = 300;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var reserve = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
        return value is double height && height > 0 ? Math.Max(Minimum, height - reserve) : 520d;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
