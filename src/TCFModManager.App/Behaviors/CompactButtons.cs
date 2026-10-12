using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace TCFModManager.App.Behaviors;

//
// A row of labelled buttons that drops to icons when it no longer fits (Chris, 2026-10-12: Remove
// was cut off on narrow cards). Set CompactButtons.Enable on the panel holding the ui:Buttons.
//
// The labels are taken off rather than shrunk: each button's Content is set aside - the binding
// itself when it is bound, so Pin's changing label keeps working - and put back once the row has
// room for all of them again. Every button here already carries a tooltip naming what it does.
//
// Checked after every layout pass rather than on size changes: the card clips the row without the
// row's own size changing, so the first version, which measured the row on SizeChanged, never saw a
// thing. Now it looks at where the buttons were actually put - one running past the edge, or the
// buttons together wider than the room the card shows - which is what being cut off is.
//
public static class CompactButtons
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable", typeof(bool), typeof(CompactButtons), new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);

    public static void SetEnable(DependencyObject d, bool value) => d.SetValue(EnableProperty, value);

    // Per button: what its Content was while the label is off.
    private static readonly DependencyProperty SavedContentProperty = DependencyProperty.RegisterAttached(
        "SavedContent", typeof(object), typeof(CompactButtons));

    private sealed class State
    {
        public bool IsCompact;

        // The width the labelled row needed when it was last too wide for its room.
        public double FullWidth;
    }

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel || e.NewValue is not true) return;

        var state = new State();
        EventHandler onLayout = (_, _) => Update(panel, state);

        panel.Loaded += (_, _) =>
        {
            panel.LayoutUpdated -= onLayout;
            panel.LayoutUpdated += onLayout;
        };
        panel.Unloaded += (_, _) => panel.LayoutUpdated -= onLayout;
    }

    private static void Update(Panel panel, State state)
    {
        if (!panel.IsLoaded || panel.ActualWidth <= 0) return;

        var buttons = panel.Children.OfType<Wpf.Ui.Controls.Button>().Where(b => b.Visibility == Visibility.Visible).ToList();
        if (buttons.Count == 0) return;

        var room = Room(panel);

        if (!state.IsCompact)
        {
            var needed = buttons.Sum(b => b.ActualWidth + b.Margin.Left + b.Margin.Right);
            var overflowing = needed > room + 0.5 || buttons.Any(b => RightEdge(b, panel) > room + 0.5);
            if (!overflowing) return;

            state.FullWidth = needed;
            state.IsCompact = true;
            foreach (var button in buttons) Strip(button);
        }
        else if (room >= state.FullWidth + 8)
        {
            state.IsCompact = false;
            foreach (var button in panel.Children.OfType<Wpf.Ui.Controls.Button>()) Restore(button);
        }
    }

    private static double RightEdge(FrameworkElement button, FrameworkElement panel)
    {
        try
        {
            return button.TranslatePoint(new Point(button.ActualWidth, 0), panel).X;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    // The width the row can actually be seen in: its own, or less where an ancestor clips it.
    private static double Room(FrameworkElement panel)
    {
        var room = panel.ActualWidth;

        DependencyObject? current = panel;
        for (var i = 0; i < 12; i++)
        {
            current = current is Visual ? VisualTreeHelper.GetParent(current) : null;
            if (current is null or ItemsControl or Page or Window) break;
            if (current is not FrameworkElement { ActualWidth: > 0 } a) continue;

            try
            {
                var x = panel.TransformToAncestor(a).Transform(new Point(0, 0)).X;
                room = Math.Min(room, a.ActualWidth - x);
            }
            catch (InvalidOperationException)
            {
                // Not connected yet - looked at again on the next layout pass.
            }
        }

        return room;
    }

    private static void Strip(Wpf.Ui.Controls.Button button)
    {
        if (button.ReadLocalValue(SavedContentProperty) != DependencyProperty.UnsetValue) return;

        var local = button.ReadLocalValue(ContentControl.ContentProperty);
        if (local == DependencyProperty.UnsetValue) return;

        button.SetValue(SavedContentProperty, local is BindingExpressionBase bound ? bound.ParentBindingBase : local);
        BindingOperations.ClearBinding(button, ContentControl.ContentProperty);
        button.Content = null;
    }

    private static void Restore(Wpf.Ui.Controls.Button button)
    {
        if (button.ReadLocalValue(SavedContentProperty) is not { } saved || saved == DependencyProperty.UnsetValue) return;

        if (saved is BindingBase binding) button.SetBinding(ContentControl.ContentProperty, binding);
        else button.Content = saved;

        button.ClearValue(SavedContentProperty);
    }
}
