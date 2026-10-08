using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

using TCFModManager.App.Behaviors;

namespace TCFModManager.App.Controls;

//
// Slides a panel's children from where they were to where the last arrange put them (OPEN-26 §2.2),
// so a card that changes row or column travels there instead of teleporting. Used by the Glide*
// panels, which call AfterArrange at the end of their ArrangeOverride.
//
// The usual FLIP trick: the panel has already put the child in its new slot, so the child is given
// a TranslateTransform of (old - new) - back where it was on screen - and that is animated to 0.
// Only the render transform moves, so a glide costs no layout passes and hit-testing follows it.
// Position only, never size: a card that changes width just takes the new width.
//
// Only a change of row or column glides (R4). Everything else - the window being dragged a pixel at
// a time, the docked Filters panel narrowing the results, a card above growing taller - moves the
// child instantly, as it always has. Gliding those would leave every card trailing behind the
// window edge. A glide already running when the next small move lands keeps going; its end point
// simply moves with the slot.
//
// Nothing glides on a panel's first arrange, or the first after it becomes visible again: a view
// that was hidden while the window changed size would otherwise open with every card sliding in.
//
// Children without a previous slot - new results - fade in instead (§2.4): opacity 0 -> 1 with a
// short rise, one after another in reading order (R7, R10). That covers a filter or sort change
// inserting rows, a Browse search or page turn (every card new), and each batch of a GradualFill
// arriving below the last. Not on a fresh arrange, so a page or view's first paint is immediate.
// Removed children just go (R6).
//
internal sealed class LayoutGlide
{
    // Two slots whose tops are within this of each other are on the same line. Layout rounding can
    // leave cards in one row a fraction of a pixel apart.
    private const double SameLine = 0.5;

    // How far below its slot a new child starts as it fades in.
    private const double Rise = 8;

    private readonly Panel _panel;
    private Dictionary<UIElement, Placement> _last = new();
    private readonly Dictionary<UIElement, TranslateTransform> _transforms = new();
    private bool _fresh = true;

    // Waiting for the current layout pass to finish before comparing - see AfterArrange.
    private bool _pending;

    public LayoutGlide(Panel panel)
    {
        _panel = panel;
        panel.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) _fresh = true;
        };
    }

    //
    // Called from the panel's ArrangeOverride, but nothing is compared there. One layout pass can
    // arrange the panel more than once on its way to the answer, and the in-between arrangements
    // are wrong. Dragging the window narrower on Installed: the panel is first arranged at the new
    // width with the previous frame's CardWidth, so the last card of every row wraps onto the next;
    // only then does SizeChanged set the new CardWidth and the cards go back. Gliding from those
    // in-between slots is what sent the cards bouncing and the icons flickering on every step of a
    // drag (Chris, 2026-10-08). So this just asks to be told once the whole pass has finished -
    // LayoutUpdated, which fires after layout and before anything is drawn - and compares the
    // settled slots with the last settled slots.
    //
    public void AfterArrange()
    {
        if (_pending) return;
        _pending = true;
        _panel.LayoutUpdated += OnLayoutUpdated;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        _panel.LayoutUpdated -= OnLayoutUpdated;
        _pending = false;
        Settle();
    }

    private void Settle()
    {
        var now = Place();
        var glide = Motion.Enabled && !_fresh;
        var arrivals = new List<(UIElement Child, Placement Placement)>();

        foreach (var (child, placement) in now)
        {
            _transforms.TryGetValue(child, out var transform);

            if (!glide)
            {
                if (transform is not null) Stop(child, transform);
                continue;
            }

            if (!_last.TryGetValue(child, out var before))
            {
                arrivals.Add((child, placement));
                continue;
            }

            if (placement.Row == before.Row && placement.Column == before.Column) continue;

            transform ??= Attach(child);

            // Where the child is on screen right now - its old slot plus however far a glide still
            // running has it from there - measured from its new slot.
            var dx = before.Origin.X + transform.X - placement.Origin.X;
            var dy = before.Origin.Y + transform.Y - placement.Origin.Y;
            Start(transform, dx, dy);
        }

        arrivals.Sort((a, b) => a.Placement.Row != b.Placement.Row
            ? a.Placement.Row.CompareTo(b.Placement.Row)
            : a.Placement.Column.CompareTo(b.Placement.Column));
        for (var i = 0; i < arrivals.Count; i++)
        {
            var child = arrivals[i].Child;
            if (!_transforms.TryGetValue(child, out var transform)) transform = Attach(child);
            FadeIn(child, transform, Motion.StaggerFor(i));
        }

        // Children that have gone take their transforms with them.
        foreach (var gone in _transforms.Keys.Where(child => !now.ContainsKey(child)).ToList())
            _transforms.Remove(gone);

        _last = now;
        _fresh = false;
    }

    // Each visible child's slot, and the row and column it sits in.
    private Dictionary<UIElement, Placement> Place()
    {
        var slots = new List<(UIElement Child, Point Origin)>(_panel.Children.Count);
        foreach (UIElement child in _panel.Children)
        {
            if (child is not FrameworkElement element || element.Visibility == Visibility.Collapsed) continue;
            slots.Add((child, LayoutInformation.GetLayoutSlot(element).TopLeft));
        }

        slots.Sort((a, b) => a.Origin.Y != b.Origin.Y ? a.Origin.Y.CompareTo(b.Origin.Y) : a.Origin.X.CompareTo(b.Origin.X));

        var placed = new Dictionary<UIElement, Placement>(slots.Count);
        var row = -1;
        var column = 0;
        var lineTop = double.NegativeInfinity;

        foreach (var (child, origin) in slots)
        {
            if (origin.Y > lineTop + SameLine)
            {
                row++;
                column = 0;
                lineTop = origin.Y;
            }

            placed[child] = new Placement(origin, row, column++);
        }

        return placed;
    }

    private TranslateTransform Attach(UIElement child)
    {
        var transform = new TranslateTransform();
        child.RenderTransform = transform;
        _transforms[child] = transform;
        return transform;
    }

    //
    // Every offset lands on a whole device pixel (SnappedDoubleAnimation), so text and icons stay on
    // the pixel grid while they move (Chris, 2026-10-08: jagged while the window was dragged). The
    // windows use TextFormattingMode="Display", and a fractional RenderTransform shifts that crisp
    // text between pixels every frame. Timeline animations rather than a per-frame handler so they
    // run at the display's refresh rate - see Motion.ApplyFrameRate.
    //
    private void Start(TranslateTransform transform, double fromX, double fromY)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, Snapped(fromX, 0, Motion.Glide, TimeSpan.Zero, FillBehavior.HoldEnd));
        transform.BeginAnimation(TranslateTransform.YProperty, Snapped(fromY, 0, Motion.Glide, TimeSpan.Zero, FillBehavior.HoldEnd));
    }

    //
    // Held hidden until its turn, then faded up and risen into its slot. FillBehavior.Stop hands
    // Opacity and Y back to their resting values (1 and 0) when it ends, so nothing is left pinned.
    // The rise only begins at its turn; until then the child sits in its slot, but at opacity 0.
    //
    private void FadeIn(UIElement child, TranslateTransform transform, TimeSpan delay)
    {
        var duration = Motion.FadeIn.TimeSpan;
        var opacity = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(delay + duration), Motion.GlideEase));
        child.BeginAnimation(UIElement.OpacityProperty, opacity);

        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, Snapped(Rise, 0, Motion.FadeIn, delay, FillBehavior.Stop));
    }

    private SnappedDoubleAnimation Snapped(double from, double to, Duration duration, TimeSpan delay, FillBehavior fill) =>
        new()
        {
            From = from,
            To = to,
            Duration = Motion.Of(duration),
            BeginTime = delay,
            FillBehavior = fill,
            EasingFunction = Motion.GlideEase,
            Scale = VisualTreeHelper.GetDpi(_panel).DpiScaleY,
        };

    private static void Stop(UIElement child, TranslateTransform transform)
    {
        child.BeginAnimation(UIElement.OpacityProperty, null);
        if (transform.X == 0 && transform.Y == 0 && !transform.HasAnimatedProperties) return;

        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.X = 0;
        transform.Y = 0;
    }

    private readonly record struct Placement(Point Origin, int Row, int Column);
}
