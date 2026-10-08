using System.Windows;

namespace TCFModManager.App.Behaviors;

//
// Pinning a Filters panel section to the row under the search bar (OPEN-24). Each section's
// Expander names itself with FilterPin.Section, which turns on the pin button in the FilterSection
// template; the style works out IsPinned from the page view model's FilterPins.Names and the template's
// triggers read it.
//
public static class FilterPin
{
    public static readonly DependencyProperty SectionProperty = DependencyProperty.RegisterAttached(
        "Section", typeof(string), typeof(FilterPin), new PropertyMetadata(null));

    public static string? GetSection(DependencyObject element) => (string?)element.GetValue(SectionProperty);

    public static void SetSection(DependencyObject element, string? value) => element.SetValue(SectionProperty, value);

    public static readonly DependencyProperty IsPinnedProperty = DependencyProperty.RegisterAttached(
        "IsPinned", typeof(bool), typeof(FilterPin), new PropertyMetadata(false));

    public static bool GetIsPinned(DependencyObject element) => (bool)element.GetValue(IsPinnedProperty);

    public static void SetIsPinned(DependencyObject element, bool value) => element.SetValue(IsPinnedProperty, value);
}
