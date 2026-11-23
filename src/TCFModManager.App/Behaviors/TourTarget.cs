using System.Windows;
using System.Windows.Media;

namespace TCFModManager.App.Behaviors;

public static class TourTarget
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(TourTarget), new PropertyMetadata(null));

    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

    public static FrameworkElement? Find(DependencyObject node, string key)
    {
        if (node is UIElement { IsVisible: false }) return null;

        if (node is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } element
            && string.Equals(GetKey(node), key, StringComparison.Ordinal))
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            if (Find(VisualTreeHelper.GetChild(node, i), key) is { } found) return found;
        }

        return null;
    }
}
