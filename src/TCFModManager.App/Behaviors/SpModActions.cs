using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using TCFModManager.App.Services;

namespace TCFModManager.App.Behaviors;

//
// OPEN-76 R11: the sp-mod actions button on a card. Set ModId on a button: a click opens
// SpModActionsFlyout on that mod's sp-mod page, and resting the pointer on it starts the page loading.
//
public static class SpModActions
{
    private static readonly TimeSpan RestDelay = TimeSpan.FromMilliseconds(400);

    private static readonly DispatcherTimer Rest = new() { Interval = RestDelay };

    private static int? _resting;

    public static readonly DependencyProperty ModIdProperty = DependencyProperty.RegisterAttached(
        "ModId", typeof(int?), typeof(SpModActions), new PropertyMetadata(null, OnModIdChanged));

    static SpModActions()
    {
        Rest.Tick += (_, _) =>
        {
            Rest.Stop();
            if (_resting is { } id) SpModActionsFlyout.Preload(id);
        };
    }

    public static int? GetModId(DependencyObject element) => (int?)element.GetValue(ModIdProperty);

    public static void SetModId(DependencyObject element, int? value) => element.SetValue(ModIdProperty, value);

    private static void OnModIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;

        button.Click -= Click;
        button.MouseEnter -= Enter;
        button.MouseLeave -= Leave;

        if (e.NewValue is null) return;

        button.Click += Click;
        button.MouseEnter += Enter;
        button.MouseLeave += Leave;
    }

    private static void Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button || GetModId(button) is not { } id) return;

        e.Handled = true;
        Rest.Stop();
        SpModActionsFlyout.Open(button, id);
    }

    private static void Enter(object sender, MouseEventArgs e)
    {
        if (sender is not ButtonBase button || GetModId(button) is not { } id) return;

        _resting = id;
        Rest.Stop();
        Rest.Start();
    }

    private static void Leave(object sender, MouseEventArgs e)
    {
        Rest.Stop();
        _resting = null;
    }
}
