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

    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(167));

    private readonly FrameworkElement _page;
    private readonly ToggleButton _toggle;
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

    // The panel's grid column (0) and the results' (1) - the page's rows above the panel span both.
    public FilterSidePanel(FrameworkElement page, ToggleButton toggle, FrameworkElement panel, ColumnDefinition column)
    {
        _page = page;
        _toggle = toggle;
        _panel = panel;
        _column = column;

        _panel.RenderTransform = _slide;

        _toggle.Checked += (_, _) => Apply(slideIn: true);
        _toggle.Unchecked += (_, _) => Apply(slideIn: false);
        _page.SizeChanged += (_, _) => Apply(slideIn: false);

        // handledEventsToo: the cards mark their own clicks handled, and a click on one should still
        // close a panel lying over the results.
        _page.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(Page_PreviewMouseDown), true);

        Apply(slideIn: false);
    }

    private bool IsDocked => _page.ActualWidth >= DockWidth;

    // Whether an event came from inside the panel - the page's scroll-anywhere wheel steps aside for it.
    public bool Contains(object? source) => Within(source, _panel);

    private void Apply(bool slideIn)
    {
        var open = _toggle.IsChecked == true;
        var docked = IsDocked;

        _panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        _column.Width = open && docked ? GridLength.Auto : new GridLength(0);

        Grid.SetColumnSpan(_panel, docked ? 1 : 2);
        Panel.SetZIndex(_panel, docked ? 0 : 10);
        _panel.HorizontalAlignment = HorizontalAlignment.Left;
        _panel.VerticalAlignment = docked ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        _panel.Margin = docked ? new Thickness(0, 0, 12, 0) : new Thickness(0);
        _panel.Effect = docked ? null : _shadow;

        if (!open || !slideIn) return;

        // The same 167ms and ease as a ComboBox's dropdown, sideways from the left edge.
        var ease = new CircleEase { EasingMode = EasingMode.EaseOut };
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-24, 0, SlideDuration) { EasingFunction = ease });
        _panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, SlideDuration) { EasingFunction = ease });
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
