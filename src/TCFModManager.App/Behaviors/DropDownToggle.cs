using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TCFModManager.App.Behaviors;

//
// Set on a ToggleButton that opens a StaysOpen=False Popup (the pinned filter dropdowns under the
// search bar, OPEN-24; first written for Browse's old SPT version dropdown), naming
// that popup, so a click on the button while the popup is open closes it and leaves it closed.
//
// The popup closes on the mouse-down of that click, which unchecks the button, and the click then
// carries on to the button and checks it again - the dropdown flickered shut and back open.
//
// The first try timed the click from the popup's Closed event, but that is raised later, once the
// popup's window has gone - after the click it was meant to catch (Chris tested, 2026-10-06). This
// times it from the moment the button unchecks, which happens as the popup closes, and also takes a
// press that arrives while the popup still reads open.
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

        toggle.Unchecked -= Toggle_Unchecked;
        toggle.Unchecked += Toggle_Unchecked;
        toggle.PreviewMouseLeftButtonDown -= Toggle_PreviewMouseLeftButtonDown;
        toggle.PreviewMouseLeftButtonDown += Toggle_PreviewMouseLeftButtonDown;
    }

    private static void Toggle_Unchecked(object sender, RoutedEventArgs e) =>
        ((DependencyObject)sender).SetValue(ClosedAtProperty, Environment.TickCount64);

    private static void Toggle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;

        // The press reached the button before the popup let go of it: close it here, and stop the
        // press from toggling the button back on.
        if (GetPopup(toggle) is { IsOpen: true } popup)
        {
            popup.IsOpen = false;
            toggle.IsChecked = false;
            e.Handled = true;
            return;
        }

        // The popup has just closed on this same press.
        if (Environment.TickCount64 - (long)toggle.GetValue(ClosedAtProperty) < ReclickWindowMs) e.Handled = true;
    }
}
