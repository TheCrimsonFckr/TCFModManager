namespace TCFModManager.Core.Models;

// Which theme the app should use. Stored in settings.json; applied by the App layer, since the
// theming itself belongs to WPF-UI and Core carries no UI dependencies.
public enum ThemePreference
{
    // Match Windows, and keep matching it when the user changes it.
    FollowSystem,

    Light,

    Dark,
}
