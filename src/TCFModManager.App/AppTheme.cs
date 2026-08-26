using System.Windows;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Wpf.Ui.Appearance;

namespace TCFModManager.App;

//
// Applies the stored ThemePreference, and follows Windows while the preference is Follow system.
//
// Everything theme-related lives here rather than being spread between App, MainWindow and the
// Options page: the three of them only say *when*, and the rules for what that means - including
// remembering to stop following the OS when the user pins a theme - stay in one place.
//
// Note the whole app is still pinned to Dark by default. WPF-UI's own controls theme themselves, but
// a good deal of this app's XAML still hardcodes white text, so Light is not yet worth choosing -
// that sweep is the next piece of work.
//
public static class AppTheme
{
    // Stateless (it only holds a path), so constructing one costs nothing and every write does its
    // own Load first - the same load-mutate-save the rest of the app uses on this file.
    private static readonly SettingsService Settings = new();

    // The window currently handed to SystemThemeWatcher, so it can be un-watched again. Null when
    // the preference isn't Follow system.
    private static Window? _watching;

    public static ThemePreference Stored => Settings.Load().Theme;

    //
    // Applies the stored preference. Called from App.OnStartup, before the main window exists, so
    // the window is painted in the right theme rather than being repainted a moment after it opens.
    //
    public static void ApplyStored() => ApplyOnly(Stored);

    //
    // Starts following Windows if that's what the preference says. Separate from ApplyStored because
    // SystemThemeWatcher needs a real window, which doesn't exist yet at startup.
    //
    public static void FollowSystemIfPreferred(Window window)
    {
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
