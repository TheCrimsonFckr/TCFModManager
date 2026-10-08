using System.Windows;
using System.Windows.Media.Animation;

namespace TCFModManager.App.Behaviors;

//
// A DoubleAnimation whose every value lands on a whole device pixel (OPEN-26). For sizes that move
// the content around them - a card growing as it opens pushes everything below it down - so that
// content never sits between pixels mid-animation, where Display-mode text and icons step and
// shimmer. Scale is the window's DPI scale (1.25 at 125%), so the snap is to real pixels.
//
public sealed class SnappedDoubleAnimation : DoubleAnimation
{
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(double), typeof(SnappedDoubleAnimation), new PropertyMetadata(1.0));

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    protected override double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, AnimationClock animationClock)
    {
        var value = base.GetCurrentValueCore(defaultOriginValue, defaultDestinationValue, animationClock);
        var scale = Scale > 0 ? Scale : 1;
        return Math.Round(value * scale) / scale;
    }

    protected override Freezable CreateInstanceCore() => new SnappedDoubleAnimation();
}
