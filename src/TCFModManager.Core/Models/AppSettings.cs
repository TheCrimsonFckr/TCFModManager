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
    // Whether the Mod footprint page appears in the sidebar at all.
    //
    // OFF by default, deliberately. The page reads what each mod ships and describes how much of
    // the game it is positioned to touch - it times nothing and measures nothing, and what a mod
    // actually costs depends on hardware, settings and mod interactions it cannot see. Someone who
    // has read what it is can turn it on and take it for what it is; someone who meets a "Heavy"
    // label with no context is being handed a conclusion the app never made. Opt-in until the
    // measurement side of this exists to back it up.
    //
    public bool ShowModFootprintPage { get; set; }

    //
    // How the Installed and Browse pages were left: how many cards to a page, and what they were
    // ordered by. Both pages went back to 12 per page and their first sort option on every visit,
    // which is a poor default for anyone who had settled on something else.
    //
    // Null means "whatever the page's own default is", so an upgrade with no keys yet behaves
    // exactly as before, and deleting a key by hand puts a page back to its default rather than
    // breaking it. A size outside the page's dropdown is ignored for the same reason.
    //
    // The sorts are stored as names rather than numbers, for the reason the Theme comment gives -
    // settings.json is offered for hand-editing, and "InstalledSort": 4 would mean nothing. They
    // are plain strings rather than enums because the orderings are declared in the App project,
    // next to the dropdowns they fill; this project cannot see them, and moving UI ordering into
    // it to satisfy a settings file would be the tail wagging the dog. An unrecognised name falls
    // back to the page's default.
    //
    public int? InstalledPageSize { get; set; }

    public string? InstalledSort { get; set; }

    public int? BrowsePageSize { get; set; }

    public string? BrowseSort { get; set; }

    //
    // Whether the Installed page tags each mod with the mod lists it belongs to. On by default -
    // the badges are the point of having lists visible at all - but an install with several lists
    // puts a row of chips on every card, so it can be turned off to quieten the page down.
    //
    public bool ShowModListBadges { get; set; } = true;
}
