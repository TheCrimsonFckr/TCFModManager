using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace TCFModManager.App.Behaviors;

//
// A row of labelled buttons that drops to icons when it no longer fits (Chris, 2026-10-12: Remove
// was cut off on narrow cards). Set CompactButtons.Enable on the panel holding the ui:Buttons.
//
// The labels are taken off rather than shrunk: each button's Content is set aside - the binding
// itself when it is bound, so Pin's changing label keeps working - and put back once the row has
// room for all of them again. Every button here already carries a tooltip naming what it does.
// The width the labelled row needs is measured unconstrained and remembered, so it doesn't flick
// between the two states at the edge.
//
public static class CompactButtons
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable", typeof(bool), typeof(CompactButtons), new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);

    public static void SetEnable(DependencyObject d, bool value) => d.SetValue(EnableProperty, value);

    // Per panel: whether the labels are off, and the width the labelled row needed.
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(CompactButtons));

    // Per button: what its Content was while the label is off.
    private static readonly DependencyProperty SavedContentProperty = DependencyProperty.RegisterAttached(
        "SavedContent", typeof(object), typeof(CompactButtons));

    private sealed class State
    {
        public bool IsCompact;
        public double FullWidth;
    }

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel || e.NewValue is not true) return;

        panel.SetValue(StateProperty, new State());
        panel.SizeChanged += (_, _) => Update(panel);
        panel.Loaded += (_, _) => Update(panel);
    }

    private static void Update(Panel panel)
    {
        if (panel.GetValue(StateProperty) is not State state || panel.ActualWidth <= 0) return;

        var buttons = panel.Children.OfType<Wpf.Ui.Controls.Button>().Where(b => b.Visibility == Visibility.Visible).ToList();

        if (!state.IsCompact)
        {
            state.FullWidth = buttons.Sum(b =>
            {
                b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return b.DesiredSize.Width;
            });

            if (state.FullWidth <= panel.ActualWidth) return;

            state.IsCompact = true;
            foreach (var button in buttons) Strip(button);
        }
        else if (panel.ActualWidth >= state.FullWidth + 8)
        {
            state.IsCompact = false;
            foreach (var button in panel.Children.OfType<Wpf.Ui.Controls.Button>()) Restore(button);
        }
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
