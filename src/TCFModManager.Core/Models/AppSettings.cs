using System.Text.Json.Serialization;
using TCFModManager.Core.ServerMap;

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
    // Whether the Installed page tags each mod with the mod lists it belongs to. On by default -
    // the badges are the point of having lists visible at all - but an install with several lists
    // puts a row of chips on every card, so it can be turned off to quieten the page down.
    //
    public bool ShowModListBadges { get; set; } = true;

    //
    // Server Map. Always present in settings.json so the shape is obvious to anyone hand-editing
    // it, but the feature stays invisible until an address is entered and a handshake succeeds -
    // there is no separate "enable" toggle to get out of step with whether it works.
    //
    // Never null, including when a hand-edited file says "ServerMap": null - this file is offered
    // for editing, so a null written into it is a thing that happens rather than a thing to assume
    // away.
    //
    public ServerMapSettings ServerMap
    {
        get => _serverMap;
        set => _serverMap = value ?? new ServerMapSettings();
    }

    private ServerMapSettings _serverMap = new();
}

//
// Where the Server Map server is and what it is trusted to be.
//
// Deliberately not derived from SptInstallPath: the address is the one the user already types into
// the SPT launcher, and the install's own http.json is stale on any Fika setup. See
// ServerMapEndpoint for why.
//
public sealed class ServerMapSettings
{
    // Host or IP as the user typed it. Empty means the feature is unconfigured, not off.
    public string? Host { get; set; }

    public int Port { get; set; } = ServerMapEndpoint.DefaultPort;

    //
    // SHA-256 thumbprint of the certificate this host presented the first time it was reached.
    //
    // Recorded on first connect and compared on every later one. SPT's certificate is self-signed
    // for localhost, so it fails the chain and hostname checks at any remote address - this is the
    // only check that means anything, and clearing it re-arms trust-on-first-use.
    //
    public string? PinnedThumbprint { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    public ServerMapEndpoint ToEndpoint() => new(Host ?? string.Empty, Port, PinnedThumbprint);
}
