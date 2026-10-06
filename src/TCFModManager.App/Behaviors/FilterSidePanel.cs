using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace TCFModManager.App.Behaviors;

//
// The Filters panel on Installed and Browse (Chris, 2026-10-06): a panel down the left of the
// results, opened and closed by the Filters button.
//
// With room for it - the page at least DockWidth wide - it takes a column of its own and the results
// move over to make room, so it can stay open while you work. Narrower than that it lies over the
// left of the results with a shadow, and a click anywhere else on the page closes it as a dropdown
// would.
//
// Not a Popup: a StaysOpen=False popup closes on the mouse-down of a click on its own button, and
// the rest of that click then opened it straight back up.
//
public sealed class FilterSidePanel
{
    public const double DockWidth = 1100;

    // The space between the docked panel and the results.
    private const double Gap = 12;

    // Opening is a little slower than closing, and both run longer than a dropdown's 167ms: on a wide
    // page the results resize along with the panel, and that reads as fluid only given time to travel.
    private static readonly Duration OpenDuration = new(TimeSpan.FromMilliseconds(280));
    private static readonly Duration CloseDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly IEasingFunction OpenEase = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction CloseEase = new CubicEase { EasingMode = EasingMode.EaseInOut };

    private readonly FrameworkElement _page;
    private readonly ToggleButton _toggle;
    private readonly FrameworkElement _host;
    private readonly FrameworkElement _panel;
    private readonly ColumnDefinition _column;
    private readonly TranslateTransform _slide = new();
    private readonly DropShadowEffect _shadow = new()
    {
        BlurRadius = 20,
        Direction = 0,
        Opacity = 0.2,
        ShadowDepth = 6,
        Color = Color.FromRgb(0x20, 0x20, 0x20),
    };

    // Bumped on every change, so the end of an animation that was overtaken does nothing.
    private int _generation;
    private bool? _wasDocked;

    // The host sits in grid column 0 - the column given - and the results in column 1; the rows above
    // span both. The panel inside the host keeps a fixed width and is right-aligned, so as the host
    // widens from nothing the panel comes in from the left edge.
    public FilterSidePanel(FrameworkElement page, ToggleButton toggle, FrameworkElement host, FrameworkElement panel, ColumnDefinition column)
    {
        _page = page;
        _toggle = toggle;
        _host = host;
        _panel = panel;
        _column = column;

        _panel.RenderTransform = _slide;

        _toggle.Checked += (_, _) => Apply(animate: true);
        _toggle.Unchecked += (_, _) => Apply(animate: true);
        _page.SizeChanged += (_, _) =>
        {
            // Only crossing DockWidth changes anything; a resize otherwise leaves an animation be.
            if (_wasDocked != IsDocked) Apply(animate: false);
        };

        // handledEventsToo: the cards mark their own clicks handled, and a click on one should still
        // close a panel lying over the results.
        _page.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(Page_PreviewMouseDown), true);

        Apply(animate: false);
    }

    private bool IsDocked => _page.ActualWidth >= DockWidth;

    // Whether an event came from inside the panel - the page's scroll-anywhere wheel steps aside for it.
    public bool Contains(object? source) => Within(source, _panel);

    private void Apply(bool animate)
    {
        var generation = ++_generation;
        var open = _toggle.IsChecked == true;
        var docked = IsDocked;
        _wasDocked = docked;

        // Docked, the column is Auto and follows the host's width; lying over the results, the host
        // spans both columns and takes no room of its own.
        _column.Width = docked ? GridLength.Auto : new GridLength(0);
        Grid.SetColumnSpan(_host, docked ? 1 : 2);
        Panel.SetZIndex(_host, docked ? 0 : 10);
        _host.VerticalAlignment = docked ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        _host.ClipToBounds = docked;
        _panel.Margin = docked ? new Thickness(0, 0, Gap, 0) : new Thickness(0);
        _panel.Effect = docked ? null : _shadow;

        // Where the host is now, before any running animation is let go of.
        var currentWidth = _host.ActualWidth;
        var currentX = _slide.X;
        var currentOpacity = _panel.Opacity;
        Stop();

        if (!animate)
        {
            _host.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var fullWidth = _panel.Width + Gap;

        if (open)
        {
            var reopening = _host.Visibility == Visibility.Visible;
            _host.Visibility = Visibility.Visible;

            if (docked)
            {
                Animate(_host, FrameworkElement.WidthProperty, reopening ? currentWidth : 0, fullWidth, OpenDuration, OpenEase,
                    () => _host.BeginAnimation(FrameworkElement.WidthProperty, null));
            }
            else
            {
                Animate(_slide, TranslateTransform.XProperty, reopening ? currentX : -24, 0, OpenDuration, OpenEase);
            }

            Animate(_panel, UIElement.OpacityProperty, reopening ? currentOpacity : 0, 1, OpenDuration, OpenEase);
            return;
        }

        void Done()
        {
            if (generation != _generation) return;
            _host.Visibility = Visibility.Collapsed;
            Stop();
        }

        if (docked)
        {
            Animate(_host, FrameworkElement.WidthProperty, currentWidth, 0, CloseDuration, CloseEase, Done);
        }
        else
        {
            Animate(_slide, TranslateTransform.XProperty, currentX, -24, CloseDuration, CloseEase, Done);
        }

        Animate(_panel, UIElement.OpacityProperty, currentOpacity, 0, CloseDuration, CloseEase);
    }

    private void Stop()
    {
        _host.BeginAnimation(FrameworkElement.WidthProperty, null);
        _slide.BeginAnimation(TranslateTransform.XProperty, null);
        _panel.BeginAnimation(UIElement.OpacityProperty, null);
    }

    private static void Animate(IAnimatable target, DependencyProperty property, double from, double to,
        Duration duration, IEasingFunction ease, Action? completed = null)
    {
        var animation = new DoubleAnimation(from, to, duration) { EasingFunction = ease };
        if (completed is not null) animation.Completed += (_, _) => completed();
        target.BeginAnimation(property, animation);
    }

    private void Page_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_toggle.IsChecked != true || IsDocked) return;
        if (Within(e.OriginalSource, _panel) || Within(e.OriginalSource, _toggle)) return;

        _toggle.IsChecked = false;
    }

    private static bool Within(object? source, DependencyObject ancestor)
    {
        for (var node = source as DependencyObject; node is not null;)
        {
            if (ReferenceEquals(node, ancestor)) return true;

            // A click can report a ContentElement such as a Run, which VisualTreeHelper throws on.
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }
}
