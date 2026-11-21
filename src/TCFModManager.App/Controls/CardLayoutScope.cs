using System.Windows;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Controls;

public static class CardLayoutScope
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.RegisterAttached(
        "Layout", typeof(CardLayoutView), typeof(CardLayoutScope),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetLayout(DependencyObject element, CardLayoutView? value) => element.SetValue(LayoutProperty, value);

    public static CardLayoutView? GetLayout(DependencyObject element) => (CardLayoutView?)element.GetValue(LayoutProperty);
}
