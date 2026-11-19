using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TCFModManager.App.Behaviors;

public enum PageKeyRole
{
    None,
    Search,
    Refresh,
}

public static class PageKeys
{
    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role", typeof(PageKeyRole), typeof(PageKeys), new PropertyMetadata(PageKeyRole.None));

    public static PageKeyRole GetRole(DependencyObject element) => (PageKeyRole)element.GetValue(RoleProperty);

    public static void SetRole(DependencyObject element, PageKeyRole value) => element.SetValue(RoleProperty, value);

    public static bool TryHandle(DependencyObject root, KeyEventArgs e)
    {
        var role = (e.Key, Keyboard.Modifiers) switch
        {
            (Key.F, ModifierKeys.Control) => PageKeyRole.Search,
            (Key.F5, ModifierKeys.None) => PageKeyRole.Refresh,
            _ => PageKeyRole.None,
        };

        if (role == PageKeyRole.None || Find(root, role) is not { } target) return false;

        if (role == PageKeyRole.Search)
        {
            target.Focus();
            Keyboard.Focus(target);
            if (target is TextBox box) box.SelectAll();
        }
        else if (target is ButtonBase button)
        {
            if (button.Command is { } command)
            {
                if (command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter);
            }
            else
            {
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            }
        }

        return true;
    }

    private static UIElement? Find(DependencyObject node, PageKeyRole role)
    {
        if (node is UIElement { IsVisible: false }) return null;

        if (node is UIElement { IsEnabled: true } element && GetRole(node) == role) return element;

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            if (Find(VisualTreeHelper.GetChild(node, i), role) is { } found) return found;
        }

        return null;
    }
}
