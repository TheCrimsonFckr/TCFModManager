namespace TCFModManager.Core.ServerMap;

//
// The Server Map handshake, returned by GET /tcfservermap/hello.
//
// Unauthenticated by design: it is asked before the user has entered a shared key, and it discloses
// nothing a port scan would not. It is also what decides whether the Server Map page appears at
// all, so it has to be cheap and safe to poll.
//
// Lives beside the client rather than in Core/Models with the sp-mod DTOs (D4). The Server Map is
// meant to be a feature that owns its own files with a short, listed set of touch points on the
// rest of the app; a wire type shared with nothing else has no business being in the common bag.
//
public sealed class ServerHello
{
    //
    // Wire format version. A server speaking a protocol this build does not know is refused rather
    // than guessed at - a handshake that parses is not the same as one that means what we think.
    //
    public int Protocol { get; set; }

    public string? ModVersion { get; set; }

    public string? ServerName { get; set; }

    public string? SptVersion { get; set; }

    public bool RequiresKey { get; set; }

    // Whether the server publishes a mod list at all. False means the map works but there is
    // nothing to compare an install against.
    public bool HasList { get; set; }

    //
    // The published list's revision, carried on the handshake on purpose: the cheap probe the app
    // already makes to decide whether to show the nav item also answers "is there anything newer
    // than what I hold", so the list itself is only fetched when this moves.
    //
    public int? ListRevision { get; set; }

    public List<string> Capabilities { get; set; } = [];

    public bool Supports(string capability) =>
        Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);
}
