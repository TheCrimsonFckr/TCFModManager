using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

// Persisted application settings.
public sealed class AppSettings
{
    public string? SptInstallPath { get; set; }

    // The app version whose update banner the user dismissed, so a release they've decided to skip
    // (a bug-fix one, most likely) stops raising the banner on every launch. It's compared as an
    // exact string, so anything published after it raises a fresh one.
    public string? DismissedAppUpdateVersion { get; set; }

    //
    // Which theme to use. Written as a name rather than a number, because settings.json is offered
    // for hand-editing on the Options page and "Theme": 2 would mean nothing to whoever opened it.
    //
    // Defaults to following Windows, so the app matches the rest of the desktop without anyone
    // having to find this setting - which is the point of supporting themes at all.
    //
    // This does mean an install upgrading from a build with no Theme key changes appearance on
    // first launch if Windows is set to light. That is deliberate rather than overlooked: the
    // alternative is a feature almost nobody discovers, and putting it back is one dropdown.
    //
    [JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
    public ThemePreference Theme { get; set; } = ThemePreference.FollowSystem;
}
