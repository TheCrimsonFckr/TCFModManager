using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace TCFModManager.App;

//
// One place for how the app moves (OPEN-26): the shared timings, and whether to move at all.
//
// Follows Windows' own Animation effects setting (Settings > Accessibility > Visual effects) rather
// than an option of the app's (R3). Off there, every transition here is instant - durations come
// back as zero, so an animation still runs and lands on its end value, it just takes no time. That
// keeps every caller's Completed handler firing exactly as it does with motion on.
//
// Code that builds its animations at run time reads Enabled each time, so flipping the Windows
// setting applies from the next animation. Storyboards written in XAML take their duration from
// MotionDuration when the style is first loaded, so those follow it from the next launch.
//
internal static class Motion
{
    // Cards sliding to a new row or column, and a card growing or shrinking as it opens (R10).
    public static readonly Duration Glide = Ms(250);
    public static readonly Duration Resize = Ms(250);

    // New results, and the view being switched to, fading in (R10).
    public static readonly Duration FadeIn = Ms(150);

    // Delay between one new item's fade and the next, and the most any item waits (R10).
    public static readonly TimeSpan StaggerStep = TimeSpan.FromMilliseconds(15);
    public static readonly TimeSpan StaggerCap = TimeSpan.FromMilliseconds(150);

    public static readonly IEasingFunction GlideEase = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    private static bool _enabled = Read();

    public static bool Enabled => _enabled;

    // Raised on the UI thread when the Windows setting is changed while the app is running.
    public static event EventHandler? EnabledChanged;

    static Motion()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
    }

    // The duration to animate for: as given with motion on, zero with it off.
    public static Duration Of(Duration duration) => _enabled ? duration : new Duration(TimeSpan.Zero);

    // How long the nth new item waits before it fades in.
    public static TimeSpan StaggerFor(int index)
    {
        if (!_enabled || index <= 0) return TimeSpan.Zero;

        var delay = TimeSpan.FromTicks(StaggerStep.Ticks * index);
        return delay < StaggerCap ? delay : StaggerCap;
    }

    private static void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;

        var now = Read();
        if (now == _enabled) return;

        _enabled = now;
        EnabledChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool Read()
    {
        try
        {
            return SystemParameters.ClientAreaAnimation;
        }
        catch
        {
            return true;
        }
    }

    private static Duration Ms(double milliseconds) => new(TimeSpan.FromMilliseconds(milliseconds));

    private static IEasingFunction Freeze(EasingFunctionBase ease)
    {
        ease.Freeze();
        return ease;
    }
}
