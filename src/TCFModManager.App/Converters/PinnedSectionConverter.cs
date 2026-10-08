using System.Globalization;
using System.Windows.Data;

namespace TCFModManager.App.Converters;

// True when values[1], a Filters panel section name, is in values[0], the page's pinned names
// (PinnedFilterSections.Names). False for anything else, so a section with no name never reads as pinned.
public sealed class PinnedSectionConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [IEnumerable<string> names, string section] && names.Contains(section);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
