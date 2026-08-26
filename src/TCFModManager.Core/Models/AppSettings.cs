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
    // Defaults to Dark, which is what every build before this setting existed used - so an install
    // that has never seen this option keeps exactly the appearance it already had.
    //
    [JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
    public ThemePreference Theme { get; set; } = ThemePreference.Dark;
}
