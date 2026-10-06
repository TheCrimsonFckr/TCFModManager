using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TCFModManager.App.Behaviors;

//
// Set on a ToggleButton that opens a StaysOpen=False Popup (Browse's SPT version dropdown), naming
// that popup, so a click on the button while the popup is open closes it and leaves it closed.
//
// The popup closes on the mouse-down of that click, which unchecks the button, and the click then
// carries on to the button and checks it again - the dropdown flickered shut and back open. This
// swallows a mouse-down on the button that arrives just after its popup closed.
//
public static class DropDownToggle
{
    private const long ReclickWindowMs = 300;

    public static readonly DependencyProperty PopupProperty = DependencyProperty.RegisterAttached(
        "Popup", typeof(Popup), typeof(DropDownToggle), new PropertyMetadata(null, OnPopupChanged));

    private static readonly DependencyProperty ClosedAtProperty = DependencyProperty.RegisterAttached(
        "ClosedAt", typeof(long), typeof(DropDownToggle), new PropertyMetadata(0L));

    public static void SetPopup(DependencyObject element, Popup? value) => element.SetValue(PopupProperty, value);

    public static Popup? GetPopup(DependencyObject element) => (Popup?)element.GetValue(PopupProperty);

    private static void OnPopupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ToggleButton toggle) return;

        if (e.NewValue is Popup popup) popup.Closed += Closed;

        toggle.PreviewMouseLeftButtonDown -= Toggle_PreviewMouseLeftButtonDown;
        toggle.PreviewMouseLeftButtonDown += Toggle_PreviewMouseLeftButtonDown;

        void Closed(object? sender, EventArgs args) => toggle.SetValue(ClosedAtProperty, Environment.TickCount64);
    }

    private static void Toggle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ToggleButton toggle || toggle.IsChecked == true) return;

        if (Environment.TickCount64 - (long)toggle.GetValue(ClosedAtProperty) < ReclickWindowMs) e.Handled = true;
    }
}
