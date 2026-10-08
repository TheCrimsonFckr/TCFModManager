using System.Windows;
using System.Windows.Markup;

namespace TCFModManager.App.Behaviors;

//
// A storyboard duration in XAML that follows Windows' Animation effects setting (OPEN-26 R9):
// Duration="{behaviors:MotionDuration 167}" is 167ms with animations on and zero with them off.
//
// Read when the style or template holding it is loaded, which for the styles in App.xaml is once
// per run - see Motion for why the run-time animations pick up a change sooner.
//
[MarkupExtensionReturnType(typeof(Duration))]
public sealed class MotionDuration : MarkupExtension
{
    public MotionDuration()
    {
    }

    public MotionDuration(double milliseconds)
    {
        Milliseconds = milliseconds;
    }

    [ConstructorArgument("milliseconds")]
    public double Milliseconds { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        Motion.Of(new Duration(TimeSpan.FromMilliseconds(Milliseconds)));
}
