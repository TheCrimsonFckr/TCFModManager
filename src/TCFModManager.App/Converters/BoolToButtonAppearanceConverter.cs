using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Converters;

// True -> Primary (reads as the selected/active option, tinted with the theme accent), false ->
// Secondary. Backs the Cards/Groups view-switcher buttons on Installed so whichever mode is active
// is visually obvious rather than living in a small toggle switch easy to miss.
public sealed class BoolToButtonAppearanceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ControlAppearance.Primary : ControlAppearance.Secondary;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
