using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App;

//
// Where the main window opens, how big, and how it gets in and out of full screen.
//
// Everything window-geometry-related lives here rather than being spread between MainWindow, App
// and the Options page - the same arrangement as AppTheme, and for the same reason: the three of
// them only say *when*, and the rules for what a startup mode actually means stay in one place.
//
internal static class WindowLayout
{
    private static Window? _window;
    private static UIElement? _titleBar;

    // What the window looked like before full screen took it over, so F11 can put it back exactly.
    private static WindowStyle _restoreStyle;
    private static ResizeMode _restoreResize;
    private static WindowState _restoreState;
    private static Rect _restoreBounds;

    public static bool IsFullScreen { get; private set; }

    //
    // Called once from MainWindow's constructor. Sizing happens here rather than on Loaded because
    // WindowStartupLocation only reads Width/Height while deciding where to put the window, and by
    // Loaded it has already decided.
    //
    public static void Attach(Window window, UIElement titleBar)
    {
        _window = window;
        _titleBar = titleBar;

        var settings = new SettingsService().Load().Window;

        ApplyStartupGeometry(window, settings);

        // Full screen needs the window's HWND to work out which monitor it is on, so it waits for
        // one. SourceInitialized is the first moment there is one, and still before anything paints.
        if (settings.StartupMode == WindowStartupMode.FullScreen)
            window.SourceInitialized += (_, _) => EnterFullScreen();

        //
        // Tunnelling, so a focused text box can't swallow it.
        //
        // F11 only, deliberately - no Escape. Escape is the obvious second key for "get me out of
        // full screen", but this handler tunnels from the window down, so it would reach Escape
        // before any open ContentDialog did: closing a mod's details dialog while full screen would
        // drop the window out of full screen and leave the dialog sitting there.
        //
        window.PreviewKeyDown += OnPreviewKeyDown;

        window.Closing += (_, _) => Remember();
    }

    private static void ApplyStartupGeometry(Window window, WindowSettings settings)
    {
        switch (settings.StartupMode)
        {
            case WindowStartupMode.Maximized:
                window.WindowState = WindowState.Maximized;
                break;

            case WindowStartupMode.Custom:
                window.Width = Clamp(settings.CustomWidth, WindowSettings.MinimumWidth);
                window.Height = Clamp(settings.CustomHeight, WindowSettings.MinimumHeight);
                break;

            case WindowStartupMode.FullScreen:
                // Sized as if it were a normal window, so leaving full screen with F11 lands
                // somewhere sensible rather than at whatever the last full-screen rect was.
                if (settings.HasStoredBounds) RestoreStoredBounds(window, settings);
                break;

            default:
                if (settings.HasStoredBounds) RestoreStoredBounds(window, settings);
                if (settings.WasMaximized) window.WindowState = WindowState.Maximized;
                break;
        }
    }

    //
    // Puts the window back where it was, unless where it was no longer exists - a monitor that has
    // been unplugged, or a resolution that has shrunk under it. A window restored off-screen looks
    // exactly like an app that failed to start, so anything that doesn't land on a real desktop
    // falls back to the centred default rather than being nudged into range.
    //
    private static void RestoreStoredBounds(Window window, WindowSettings settings)
    {
        var left = settings.Left!.Value;
        var top = settings.Top!.Value;
        var width = Clamp(settings.Width!.Value, WindowSettings.MinimumWidth);
        var height = Clamp(settings.Height!.Value, WindowSettings.MinimumHeight);

        var vLeft = SystemParameters.VirtualScreenLeft;
        var vTop = SystemParameters.VirtualScreenTop;
        var vRight = vLeft + SystemParameters.VirtualScreenWidth;
        var vBottom = vTop + SystemParameters.VirtualScreenHeight;

        // A generous overlap rather than full containment: a window deliberately hanging off the
        // right edge of a monitor is a normal thing to have done, and should be restored as it was.
        const double MinimumVisible = 120;

        var visible =
            left + width > vLeft + MinimumVisible &&
            left < vRight - MinimumVisible &&
            top + height > vTop + MinimumVisible &&
            top < vBottom - MinimumVisible;

        if (!visible)
        {
            AppLog.Info("Window", $"stored position {left},{top} is off-screen - opening centred");
            window.Width = width;
            window.Height = height;
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = left;
        window.Top = top;
        window.Width = width;
        window.Height = height;
    }

    //
    // Applies a mode the user just picked in Options to the window that is already open, so the
    // setting does something now rather than at the next launch.
    //
    // Remember is the exception and deliberately does nothing: it means "keep whatever I leave it
    // at", and a window that jumps to last session's position the moment you select it is the
    // opposite of that.
    //
    public static void ApplyModeNow(WindowStartupMode mode, WindowSettings settings)
    {
        if (_window is not { } window) return;

        switch (mode)
        {
            case WindowStartupMode.Maximized:
                if (IsFullScreen) ExitFullScreen();
                window.WindowState = WindowState.Maximized;
                break;

            case WindowStartupMode.Custom:
                ApplyCustomSizeNow(settings);
                break;

            case WindowStartupMode.FullScreen:
                EnterFullScreen();
                break;
        }
    }

    /// <summary>Resizes the open window to the custom size, for the "Apply now" button beside the
    /// two size boxes in Options.</summary>
    public static void ApplyCustomSizeNow(WindowSettings settings)
    {
        if (_window is not { } window) return;

        if (IsFullScreen) ExitFullScreen();
        window.WindowState = WindowState.Normal;
        window.Width = Clamp(settings.CustomWidth, WindowSettings.MinimumWidth);
        window.Height = Clamp(settings.CustomHeight, WindowSettings.MinimumHeight);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F11) return;

        Toggle();
        e.Handled = true;
    }

    public static void Toggle()
    {
        if (IsFullScreen) ExitFullScreen();
        else EnterFullScreen();
    }

    public static void EnterFullScreen()
    {
        if (IsFullScreen || _window is not { } window) return;

        _restoreStyle = window.WindowStyle;
        _restoreResize = window.ResizeMode;
        _restoreState = window.WindowState;

        // RestoreBounds is the only honest answer while maximized - Left/Top/Width/Height report
        // the maximized rectangle, which is not what F11 should put back.
        _restoreBounds = window.WindowState == WindowState.Maximized
            ? window.RestoreBounds
            : new Rect(window.Left, window.Top, window.Width, window.Height);

        // Normal first: a maximized window ignores Left/Top, so the monitor rectangle set below
        // would do nothing at all.
        window.WindowState = WindowState.Normal;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;

        // The title bar is a real element in MainWindow's grid, not window chrome - removing the
        // chrome leaves it sitting there, so it has to be hidden explicitly.
        if (_titleBar is not null) _titleBar.Visibility = Visibility.Collapsed;

        //
        // The monitor rectangle is set explicitly rather than by maximizing a borderless window.
        // Maximizing is the usual trick for this, but WPF-UI's FluentWindow applies its own
        // WindowChrome, which answers WM_GETMINMAXINFO with the *work* area - so a maximized
        // borderless FluentWindow stops at the taskbar, which is exactly what this mode exists not
        // to do.
        //
        var bounds = MonitorBounds(window);
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;

        IsFullScreen = true;
        AppLog.Info("Window", "entered full screen");
    }

    public static void ExitFullScreen()
    {
        if (!IsFullScreen || _window is not { } window) return;

        window.WindowStyle = _restoreStyle;
        window.ResizeMode = _restoreResize;

        if (_titleBar is not null) _titleBar.Visibility = Visibility.Visible;

        if (!_restoreBounds.IsEmpty)
        {
            window.Left = _restoreBounds.Left;
            window.Top = _restoreBounds.Top;
            window.Width = _restoreBounds.Width;
            window.Height = _restoreBounds.Height;
        }

        window.WindowState = _restoreState;

        IsFullScreen = false;
        AppLog.Info("Window", "left full screen");
    }

    //
    // Records where the window is so the next launch can reopen there. Written in every mode, not
    // just Remember: switching to Remember later then has something to restore, instead of the
    // choice doing nothing at all until the session after it.
    //
    // Full screen is never recorded as the window's size - it would be the whole monitor, and the
    // startup mode is what decides whether the app opens full screen, not what it was left as.
    //
    private static void Remember()
    {
        if (_window is not { } window) return;

        try
        {
            var service = new SettingsService();
            var settings = service.Load();

            var bounds = IsFullScreen
                ? _restoreBounds
                : window.WindowState == WindowState.Maximized
                    ? window.RestoreBounds
                    : new Rect(window.Left, window.Top, window.Width, window.Height);

            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                settings.Window.Left = bounds.Left;
                settings.Window.Top = bounds.Top;
                settings.Window.Width = bounds.Width;
                settings.Window.Height = bounds.Height;
            }

            settings.Window.WasMaximized = !IsFullScreen && window.WindowState == WindowState.Maximized;

            service.Save(settings);
        }
        catch (Exception ex)
        {
            // Closing the window is not worth failing over a settings write.
            AppLog.Error("Window", "could not record the window position", ex);
        }
    }

    //
    // The open window's current size, for the "Use the current size" button in Options - dragging
    // the window to the size you want and pressing one button is a great deal easier than guessing
    // two numbers.
    //
    // Null while full screen or maximized: both report the screen rather than a size anyone chose,
    // and saving that as a custom size would produce a window that only looks maximized.
    //
    public static (double Width, double Height)? CurrentSize()
    {
        if (_window is not { } window || IsFullScreen) return null;
        if (window.WindowState != WindowState.Normal) return null;

        return (window.ActualWidth, window.ActualHeight);
    }

    private static double Clamp(double value, double minimum) =>
        double.IsFinite(value) && value > minimum ? value : minimum;

    //
    // The full rectangle of the monitor the window is currently on, in WPF units.
    //
    // MonitorFromWindow rather than SystemParameters, which only ever describes the primary screen
    // - a window on a second monitor would go full screen on the wrong one.
    //
    private static Rect MonitorBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;

        if (handle != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };

            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                // The API answers in physical pixels; WPF positions windows in DIPs, and the two
                // are only the same at 100% scaling.
                var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice
                    ?? Matrix.Identity;

                var topLeft = transform.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
                var bottomRight = transform.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));

                return new Rect(topLeft, bottomRight);
            }
        }

        // No window handle yet, or the monitor query failed. The primary screen is a worse answer
        // than the real one but a much better answer than none.
        return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
