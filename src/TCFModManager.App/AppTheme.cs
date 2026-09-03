using System.Windows;
using System.Windows.Media;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TCFModManager.App;

//
// Applies the stored ThemePreference, and follows Windows while the preference is Follow system.
//
// Everything theme-related lives here rather than being spread between App, MainWindow and the
// Options page: the three of them only say *when*, and the rules for what that means - including
// remembering to stop following the OS when the user pins a theme - stay in one place.
//
// The default preference is Follow system, so a fresh install matches the desktop. Every colour the
// app draws itself now comes from a WPF-UI theme brush rather than a literal, which is what makes
// Light a real option rather than an unreadable one - see Converters/ThemeBrush for the one case
// that can lag a live theme change.
//
public static class AppTheme
{
    // Stateless (it only holds a path), so constructing one costs nothing and every write does its
    // own Load first - the same load-mutate-save the rest of the app uses on this file.
    private static readonly SettingsService Settings = new();

    // The window currently handed to SystemThemeWatcher, so it can be un-watched again. Null when
    // the preference isn't Follow system.
    private static Window? _watching;

    // Guards against hooking ApplicationThemeManager.Changed more than once.
    private static bool _subscribed;

    public static ThemePreference Stored => Settings.Load().Theme;

    //
    // Applies the stored preference. Called from App.OnStartup, before the main window exists, so
    // the window is painted in the right theme rather than being repainted a moment after it opens.
    //
    public static void ApplyStored() => ApplyOnly(Stored);

    //
    // Hooks the main window up: repaints its chrome on every theme change, and starts following
    // Windows if that's what the preference says. Separate from ApplyStored because both of those
    // need a real window, which doesn't exist yet at startup.
    //
    public static void Attach(Window window)
    {
        if (!_subscribed)
        {
            // Subscribed rather than only called from ApplyOnly, so a theme change that SystemThemeWatcher
            // makes on its own - the user changing Windows' theme while Follow system is on - repaints
            // the chrome too.
            ApplicationThemeManager.Changed += (_, _) =>
            {
                RefreshWindowChrome();
                PinPressedButtonForeground();
            };
            _subscribed = true;
        }

        if (Stored == ThemePreference.FollowSystem) StartFollowing(window);
    }

    // Applies a newly chosen preference, saves it, and starts or stops following Windows to match.
    public static void Set(ThemePreference preference)
    {
        ApplyOnly(preference);

        var settings = Settings.Load();
        settings.Theme = preference;
        Settings.Save(settings);

        if (preference == ThemePreference.FollowSystem)
        {
            if (Application.Current?.MainWindow is { } window) StartFollowing(window);
        }
        else
        {
            // Load-bearing: left watching, the next time Windows changed theme it would drag the app
            // back off the theme the user just pinned.
            StopFollowing();
        }

        AppLog.Info("Theme", $"set to {preference}");
    }

    private static void ApplyOnly(ThemePreference preference)
    {
        switch (preference)
        {
            case ThemePreference.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;

            case ThemePreference.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;

            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }

        RefreshWindowChrome();
        PinPressedButtonForeground();
    }

    //
    // Makes a pressed button read as the button changing colour, not the label changing.
    //
    // WPF-UI's Button template sets Foreground from the button's PressedForeground for as long as
    // it is held down, and PressedForeground defaults to the ButtonForegroundPressed brush - which
    // in dark mode is far darker than the resting one, so the label flips light-to-dark mid-click.
    // The background and border already carry the feedback perfectly well on their own.
    //
    // Pointing that one brush at the resting ButtonForeground neutralises it. Deliberately a brush
    // override rather than a Style: an implicit Style TargetType="ui:Button" in App.xaml was tried
    // first and stripped every button in the app of its chrome. WPF-UI declares the real style in a
    // theme dictionary that ApplicationThemeManager merges at RUNTIME, so a
    // BasedOn="{StaticResource {x:Type ui:Button}}" resolved when App.xaml is parsed never reaches
    // it - and the implicit style then shadows the real one with no template behind it. Overriding
    // a brush can at worst give a wrong colour; it cannot take a template away.
    //
    // Has to run after every Apply, not once at startup: each theme swaps in a fresh
    // ButtonForeground, and this needs to track it. An entry set directly on Application.Resources
    // is found ahead of any merged dictionary, which is what makes the override win.
    //
    // Buttons that set their own Foreground in XAML - Remove, Sort out, the folder links - were
    // never affected either way: a local value already outranks a template trigger.
    //
    private static void PinPressedButtonForeground()
    {
        if (Application.Current is not { } app) return;

        var resting = app.TryFindResource("ButtonForeground") as Brush;
        var before = app.TryFindResource("ButtonForegroundPressed") as Brush;

        // Guarded rather than assumed. If WPF-UI ever renames the key, doing nothing leaves the
        // momentary colour flip; writing null would make every button label disappear.
        if (resting is not null) app.Resources["ButtonForegroundPressed"] = resting;

        // Logged because this is invisible when it silently does nothing - a missing key, or an
        // override that doesn't win the lookup, both look exactly like "no effect" from outside.
        AppLog.Info("Theme",
            $"pressed button foreground: resting={Describe(resting)}, was={Describe(before)}, "
            + $"now={Describe(app.TryFindResource("ButtonForegroundPressed") as Brush)}");
    }

    private static string Describe(Brush? brush) => brush switch
    {
        null => "missing",
        SolidColorBrush solid => solid.Color.ToString(),
        _ => brush.GetType().Name,
    };

    //
    // Repaints the window's own chrome - the title bar, the backdrop behind the navigation rail and
    // page area.
    //
    // Applying a theme swaps the brushes in the application's resource dictionaries, which is enough
    // for everything drawn *inside* the window (cards, text, controls all re-resolve immediately).
    // The window frame is not drawn from those brushes: FluentWindow sets its backdrop and the Win32
    // dark-mode title bar attribute once when it is created, and nothing revisits them afterwards.
    // Without this, switching theme with the app open left a light page sitting inside a dark frame -
    // while launching with the same theme already stored looked perfectly correct, because the window
    // was built after the theme was applied.
    //
    private static void RefreshWindowChrome()
    {
        // Absent at startup, when ApplyStored runs before any window exists. Nothing to do then:
        // FluentWindow reads the current theme itself as it is created.
        if (Application.Current?.MainWindow is not FluentWindow window) return;

        try
        {
            WindowBackgroundManager.UpdateBackground(
                window,
                ApplicationThemeManager.GetAppTheme(),
                window.WindowBackdropType);
        }
        catch (Exception ex)
        {
            // Chrome that didn't repaint is ugly, not broken, and is not worth taking the app down for.
            AppLog.Warn("Theme", $"couldn't repaint the window chrome: {ex.Message}");
        }
    }

    private static void StartFollowing(Window window)
    {
        if (ReferenceEquals(_watching, window)) return;

        StopFollowing();
        SystemThemeWatcher.Watch(window);
        _watching = window;
    }

    private static void StopFollowing()
    {
        if (_watching is null) return;

        SystemThemeWatcher.UnWatch(_watching);
        _watching = null;
    }
}
