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

    public static readonly DependencyProperty InListProperty = DependencyProperty.RegisterAttached(
        "InList", typeof(bool), typeof(CardLayoutScope),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetInList(DependencyObject element, bool value) => element.SetValue(InListProperty, value);

    public static bool GetInList(DependencyObject element) => (bool)element.GetValue(InListProperty);
}
