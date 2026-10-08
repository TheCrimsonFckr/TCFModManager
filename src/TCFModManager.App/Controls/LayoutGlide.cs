using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

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

    public LayoutGlide(Panel panel)
    {
        _panel = panel;
        panel.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) _fresh = true;
        };
    }

    public void AfterArrange()
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

    private static void Start(TranslateTransform transform, double fromX, double fromY)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, Glide(fromX));
        transform.BeginAnimation(TranslateTransform.YProperty, Glide(fromY));
    }

    private static DoubleAnimation Glide(double from) =>
        new(from, 0, Motion.Of(Motion.Glide)) { EasingFunction = Motion.GlideEase };

    //
    // Held hidden until its turn, then faded up and risen into its slot. FillBehavior.Stop hands
    // Opacity and Y back to their resting values (1 and 0) when it ends, so nothing is left pinned.
    //
    private static void FadeIn(UIElement child, TranslateTransform transform, TimeSpan delay)
    {
        var end = delay + Motion.FadeIn.TimeSpan;
        child.BeginAnimation(UIElement.OpacityProperty, Arrive(0, 1, delay, end));
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, Arrive(Rise, 0, delay, end));
    }

    private static DoubleAnimationUsingKeyFrames Arrive(double from, double to, TimeSpan delay, TimeSpan end)
    {
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(end), Motion.GlideEase));
        return animation;
    }

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
