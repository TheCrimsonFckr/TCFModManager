using System.Globalization;
using System.Windows.Data;
using TCFModManager.Core.Models;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Converters;

//
// A footprint level as a badge appearance. Returns the enum rather than a Brush on purpose: a
// converter that resolves a brush freezes it at whatever the theme was when the binding first ran,
// which is the outstanding status-icon bug from the v1.6.0 theme sweep. An appearance is re-themed
// by the badge itself.
//
public sealed class FootprintLevelToAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ModFootprintLevel level
            ? level switch
            {
                ModFootprintLevel.Heavy => ControlAppearance.Caution,
                ModFootprintLevel.Moderate => ControlAppearance.Info,
                ModFootprintLevel.Light => ControlAppearance.Success,
                _ => ControlAppearance.Secondary,
            }
            : ControlAppearance.Secondary;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
