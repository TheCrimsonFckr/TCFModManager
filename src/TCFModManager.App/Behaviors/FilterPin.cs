using System.Windows;
using System.Windows.Controls;

namespace TCFModManager.App.Behaviors;

//
// Pinning a Filters panel section to the row under the search bar (OPEN-24). Each section's
// Expander names itself with FilterPin.Section, which turns on the pin button in the FilterSection
// template; the style works out IsPinned from the page view model's FilterPins.Names and the template's
// triggers read it.
//
// FilterPin.Body on a ContentControl shows a section's body by name: it looks up the page resource
// "FilterBody.<section>" - the DataTemplate the panel's Expander uses too - so a pinned dropdown and
// its panel section are the same controls over the same view model.
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

    public const string BodyKeyPrefix = "FilterBody.";

    public static readonly DependencyProperty BodyProperty = DependencyProperty.RegisterAttached(
        "Body", typeof(string), typeof(FilterPin), new PropertyMetadata(null, OnBodyChanged));

    public static string? GetBody(DependencyObject element) => (string?)element.GetValue(BodyProperty);

    public static void SetBody(DependencyObject element, string? value) => element.SetValue(BodyProperty, value);

    private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentControl host) return;

        host.Loaded -= Host_Loaded;
        host.Loaded += Host_Loaded;
        ApplyBody(host);
    }

    private static void Host_Loaded(object sender, RoutedEventArgs e) => ApplyBody((ContentControl)sender);

    // Before the host is in the tree the page's resources can't be reached yet; Loaded tries again.
    private static void ApplyBody(ContentControl host)
    {
        if (GetBody(host) is not { Length: > 0 } section) return;

        if (FindBody(host, BodyKeyPrefix + section) is { } template && host.ContentTemplate != template)
            host.ContentTemplate = template;
    }

    //
    // Inside a dropdown the host sits in a Popup, whose window is outside the page; if the lookup
    // doesn't climb out of it on its own, it goes again from the button the popup opens under.
    //
    private static DataTemplate? FindBody(FrameworkElement host, string key)
    {
        if (host.TryFindResource(key) is DataTemplate found) return found;

        for (DependencyObject? at = host; at is not null; at = LogicalTreeHelper.GetParent(at))
        {
            if (at is System.Windows.Controls.Primitives.Popup { PlacementTarget: FrameworkElement target })
                return target.TryFindResource(key) as DataTemplate;
        }

        return null;
    }
}
