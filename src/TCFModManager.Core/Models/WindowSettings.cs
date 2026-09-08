using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

//
// How the main window opens.
//
// Written as a name rather than a number for the same reason ThemePreference is: settings.json is
// offered for hand-editing on the Options page, and "StartupMode": 2 would mean nothing to whoever
// opened it.
//
public enum WindowStartupMode
{
    //
    // Reopen at the size and position the window was last closed at, maximized if it was maximized.
    //
    // The default. Every other mode overrides a deliberate act - dragging the window to a size and
    // leaving it there - which is a strange thing for an app to do by default.
    //
    Remember,

    // Maximized every launch, whatever size it was left at. Respects the taskbar.
    Maximized,

    // The exact size in CustomWidth/CustomHeight, centred on the screen.
    Custom,

    //
    // Borderless, filling the whole monitor including the taskbar. Distinct from Maximized: there
    // is no title bar and nothing of the desktop shows.
    //
    // F11 toggles this at any time regardless of the startup mode, and leaving it that way is not
    // remembered - a window that opens with no title bar and no visible way back is a trap, so
    // full screen is only ever the startup state when it was chosen here.
    //
    FullScreen,
}

//
// Where the main window opens and how big.
//
// Kept as its own object rather than five loose keys on AppSettings, so the whole of it reads as
// one thing in settings.json and a hand-edit can replace the lot.
//
public sealed class WindowSettings
{
    // The size the window shipped with before any of this existed, and still the starting point.
    public const double DefaultWidth = 1720;
    public const double DefaultHeight = 980;

    // Matches MainWindow's own MinWidth/MinHeight. A custom size below this is clamped rather than
    // refused - the window would ignore it anyway.
    public const double MinimumWidth = 720;
    public const double MinimumHeight = 480;

    [JsonConverter(typeof(JsonStringEnumConverter<WindowStartupMode>))]
    public WindowStartupMode StartupMode { get; set; } = WindowStartupMode.Remember;

    public double CustomWidth { get; set; } = DefaultWidth;

    public double CustomHeight { get; set; } = DefaultHeight;

    //
    // The last restore bounds - where the window sat when it was not maximized. Recorded on close
    // in every mode, not just Remember, so switching to Remember later has something to restore
    // rather than starting from the default size once.
    //
    // Null on an install that has never closed the window, which is what makes "no stored position"
    // distinguishable from "stored at 0,0".
    //
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    public bool WasMaximized { get; set; }

    // Whether there is a position worth restoring. Size alone is not enough: a window restored to a
    // size but not a position lands wherever Windows puts it, which is not what Remember means.
    [JsonIgnore]
    public bool HasStoredBounds =>
        Left is not null && Top is not null && Width is > 0 && Height is > 0;
}
