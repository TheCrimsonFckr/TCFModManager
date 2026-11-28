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

    private static readonly HashSet<ButtonBase> Shown = [];

    public static readonly DependencyProperty ModIdProperty = DependencyProperty.RegisterAttached(
        "ModId", typeof(int?), typeof(SpModActions), new PropertyMetadata(null, OnModIdChanged));

    static SpModActions()
    {
        Rest.Tick += (_, _) =>
        {
            Rest.Stop();
            if (_resting is { } id) SpModActionsFlyout.Preload(id);
        };

        AppServices.EndorsedMods.Changed += id =>
        {
            foreach (var button in Shown.ToList())
                if (id is null || GetModId(button) == id) ShowEndorsed(button);
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
        button.Loaded -= Loaded;
        button.Unloaded -= Unloaded;

        if (e.NewValue is null)
        {
            Shown.Remove(button);
            return;
        }

        button.Click += Click;
        button.MouseEnter += Enter;
        button.MouseLeave += Leave;
        button.Loaded += Loaded;
        button.Unloaded += Unloaded;

        if (button.IsLoaded) Shown.Add(button);
        ShowEndorsed(button);
    }

    private static void Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button) return;

        Shown.Add(button);
        ShowEndorsed(button);
    }

    private static void Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ButtonBase button) Shown.Remove(button);
    }

    private static void ShowEndorsed(ButtonBase button)
    {
        var icon = button.Content as Wpf.Ui.Controls.SymbolIcon
            ?? (button as Wpf.Ui.Controls.Button)?.Icon as Wpf.Ui.Controls.SymbolIcon;
        if (icon is null) return;

        icon.Filled = GetModId(button) is { } id && AppServices.EndorsedMods.Contains(id);
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
