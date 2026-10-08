using System.ComponentModel;
using System.Runtime.InteropServices;
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

    //
    // WPF runs every animation at 60 frames a second unless told otherwise - Timeline.DesiredFrameRate
    // defaults to 60. On a high refresh display that is a quarter of the frames everything else on
    // the desktop gets (Chris's is 240Hz, 2026-10-08), and the glides read as stepping next to a
    // window being dragged at full rate. This raises the default to the primary display's refresh
    // rate, once, at startup; a timeline that sets its own DesiredFrameRate still wins. Left at 60
    // when the rate can't be read or is 60 or below.
    //
    public static void ApplyFrameRate()
    {
        var rate = PrimaryDisplayRefreshRate();
        if (rate <= 60) return;

        // Guarded: a refused override costs the higher frame rate, never the app starting.
        try
        {
            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = Math.Min(rate, 360) });
        }
        catch (ArgumentException)
        {
        }
    }

    private static int PrimaryDisplayRefreshRate()
    {
        try
        {
            var mode = new DisplayMode { Size = (short)Marshal.SizeOf<DisplayMode>() };
            return EnumDisplaySettings(null, CurrentSettings, ref mode) ? mode.DisplayFrequency : 0;
        }
        catch
        {
            return 0;
        }
    }

    private const int CurrentSettings = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DisplayMode mode);

    // DEVMODEW, display half of the union.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int IcmMethod;
        public int IcmIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;
    }

    private static Duration Ms(double milliseconds) => new(TimeSpan.FromMilliseconds(milliseconds));

    private static IEasingFunction Freeze(EasingFunctionBase ease)
    {
        ease.Freeze();
        return ease;
    }
}
