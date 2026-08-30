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

    //
    // Skips the "read the mod's page first" gate before anything is downloaded.
    //
    // Off by default, and turning it on is confirmed on the Options page, because the gate is not
    // busywork: a mod's page is where its author puts install steps, requirements, known conflicts
    // and warnings, and this app has no way to tell you which mods need reading before they will
    // work. Someone who knows their setup can reasonably turn it off; someone who doesn't should be
    // told what they are giving up first.
    //
    // Does not apply to this app's own update, which always asks - that page carries its release
    // notes.
    //
    public bool SkipModPageConfirmation { get; set; }

    //
    // Whether the Installed page tags each mod with the mod lists it belongs to. On by default -
    // the badges are the point of having lists visible at all - but an install with several lists
    // puts a row of chips on every card, so it can be turned off to quieten the page down.
    //
    public bool ShowModListBadges { get; set; } = true;
}
