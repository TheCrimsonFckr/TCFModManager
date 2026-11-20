using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Behaviors;

//
// OPEN-61: copy any text in the app. Right-click text with no menu of its own for a Copy menu,
// or Ctrl+C with the pointer over it; a menu that already exists gains Copy "(text)" at its top.
//
public static class CopyText
{
    private const int LabelLength = 40;

    private static readonly TimeSpan TipTime = TimeSpan.FromSeconds(1.2);

    private static bool _registered;

    private static WeakReference<FrameworkElement>? _rightClicked;

    private static ToolTip? _tip;

    private static DispatcherTimer? _tipTimer;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler((_, e) => _rightClicked = Under(e.OriginalSource) is { } block ? new(block) : null), true);
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((_, _) => _rightClicked = null), true);
        EventManager.RegisterClassHandler(typeof(Window), UIElement.MouseRightButtonUpEvent,
            new MouseButtonEventHandler(OnRightButtonUp));
        EventManager.RegisterClassHandler(typeof(Window), UIElement.KeyDownEvent,
            new KeyEventHandler(OnKeyDown));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler(OnMenuOpened), true);
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent,
            new RoutedEventHandler(OnMenuClosed), true);
    }

    public static void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            Clipboard.SetText(text);
            ShowTip(Strings.Copy_Copied);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            ShowTip(Strings.Copy_Failed);
        }
    }

    public static string MenuLabel(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length > LabelLength) flat = flat[..LabelLength].TrimEnd() + "…";
        return LocalizationService.Text(Strings.Copy_TextFormat, flat);
    }

    private static void OnRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || HasOwnMenu(e.OriginalSource) || Under(e.OriginalSource) is not { } block) return;

        var menu = new ContextMenu { PlacementTarget = block, Placement = PlacementMode.MousePoint };
        menu.Items.Add(CopyItem(TextOf(block)));
        _rightClicked = null;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (Keyboard.FocusedElement is PasswordBox or TextBoxBase and not TextBox { IsReadOnly: true, SelectionLength: 0 }) return;
        if (Mouse.DirectlyOver is not DependencyObject over || !ReferenceEquals(Window.GetWindow(over), sender)) return;
        if (Under(over) is not { } block) return;

        Copy(TextOf(block));
        e.Handled = true;
    }

    private static void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var block = Take();

        if (block is null || menu.ItemsSource is not null || menu.Items.Count == 0) return;
        if (menu.PlacementTarget is not DependencyObject target || !Within(block, target)) return;

        menu.Items.Insert(0, new Separator { Tag = typeof(CopyText) });
        menu.Items.Insert(0, CopyItem(TextOf(block)));
    }

    private static void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        if (menu.ItemsSource is not null) return;

        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i] is FrameworkElement { Tag: Type tag } && tag == typeof(CopyText)) menu.Items.RemoveAt(i);
        }
    }

    private static FrameworkElement? Take()
    {
        FrameworkElement? block = null;
        _rightClicked?.TryGetTarget(out block);
        _rightClicked = null;
        return block;
    }

    private static MenuItem CopyItem(string text)
    {
        var item = new MenuItem
        {
            Header = MenuLabel(text),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Copy24 },
            Tag = typeof(CopyText),
        };
        item.Click += (_, _) => Copy(text);
        return item;
    }

    private static FrameworkElement? Under(object? source)
    {
        for (var node = source as DependencyObject; node is not null and not Window; node = Parent(node))
        {
            if (node is TextBox { IsReadOnly: true } box) return string.IsNullOrWhiteSpace(box.Text) ? null : box;
            if (node is TextBoxBase or PasswordBox or MenuItem) return null;
            if (node is TextBlock block) return string.IsNullOrWhiteSpace(block.Text) ? null : block;
        }

        return null;
    }

    private static string TextOf(FrameworkElement element) => element switch
    {
        TextBox { SelectionLength: > 0 } box => box.SelectedText,
        TextBox box => box.Text,
        TextBlock block => block.Text,
        _ => string.Empty,
    };

    private static bool HasOwnMenu(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = Parent(node))
        {
            if (ContextMenuService.GetContextMenu(node) is not null) return true;
            if (node is TextBox { IsReadOnly: true }) return false;
            if (node is TextBoxBase) return true;
        }

        return false;
    }

    private static bool Within(DependencyObject node, DependencyObject ancestor)
    {
        for (DependencyObject? at = node; at is not null; at = Parent(at))
        {
            if (ReferenceEquals(at, ancestor)) return true;
        }

        return false;
    }

    private static DependencyObject? Parent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    private static void ShowTip(string text)
    {
        if (Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) is not { } window) return;

        _tip ??= new ToolTip { Placement = PlacementMode.Mouse };
        _tip.IsOpen = false;
        _tip.Content = text;
        _tip.PlacementTarget = window;
        _tip.IsOpen = true;

        _tipTimer ??= new DispatcherTimer { Interval = TipTime };
        _tipTimer.Tick -= CloseTip;
        _tipTimer.Tick += CloseTip;
        _tipTimer.Stop();
        _tipTimer.Start();
    }

    private static void CloseTip(object? sender, EventArgs e)
    {
        _tipTimer?.Stop();
        if (_tip is not null) _tip.IsOpen = false;
    }
}
