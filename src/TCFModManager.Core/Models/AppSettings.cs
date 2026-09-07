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
    // it, even on an install that never turns the page on.
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
    //
    // Whether the Server map page appears in the sidebar at all.
    //
    // OFF by default and switched on deliberately, the same as the Mod footprint page: the map only
    // does anything if someone you play with runs an SPT server with the Server Map mod installed,
    // which most installs will not. A page that is empty for everyone except the people who set one
    // up is worth opting into rather than shipping to everyone.
    //
    // Kept in here rather than beside ShowModFootprintPage on AppSettings so the whole feature is
    // one object in settings.json - it is switched off and forgotten far more often than it is used.
    //
    public bool ShowPage { get; set; }

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

    //
    // The server's shared key, as the operator sent it. Everything except the handshake needs it.
    //
    // Stored in the clear, which is honest about what it is: not a password and not tied to an
    // identity, just the string that says you were told about this server. It reaches settings.json,
    // which the Options page already invites people to open - anyone who can read that file can
    // already read the address it goes with.
    //
    public string? SharedKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    public bool HasKey => !string.IsNullOrWhiteSpace(SharedKey);

    public ServerMapEndpoint ToEndpoint() => new(Host ?? string.Empty, Port, PinnedThumbprint, SharedKey);
}
