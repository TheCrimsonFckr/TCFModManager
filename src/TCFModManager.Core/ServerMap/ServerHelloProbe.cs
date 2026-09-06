namespace TCFModManager.Core.ServerMap;

//
// Why a probe did not produce a handshake. The App words it; this reports what happened and the
// values behind it, per the convention that Core owns no user-facing prose.
//
public enum ServerMapProblem
{
    None,

    // Nothing configured yet. Not an error - the feature is simply unused.
    NoAddress,

    // The host or port could not be turned into a URL. Carries the endpoint.
    InvalidAddress,

    // Nothing answered - DNS, refused, timed out. Carries Error.
    Unreachable,

    // A certificate was presented that is not the pinned one. Carries ExpectedThumbprint and
    // ActualThumbprint, so the caller can name both and offer to re-pin.
    CertificateRejected,

    // Something answered, but not this mod. A 404 from a plain SPT server and a 503 from a stub
    // whose payload folder has been emptied both land here - and neither is worth alarming anyone
    // with, they just mean "no server map at this address".
    NotServerMap,

    // A Server Map, speaking a protocol this build does not know. Carries ServerProtocol.
    ProtocolMismatch,

    // Anything else. Carries Error.
    Failed,
}

public sealed record ServerHelloProbe
{
    public required ServerMapEndpoint Endpoint { get; init; }

    public ServerHello? Hello { get; init; }

    public ServerMapProblem Problem { get; init; }

    // What the server actually presented, recorded even when the pin rejected it - that is what
    // makes "re-pin to this one?" possible instead of a dead end.
    public string? ActualThumbprint { get; init; }

    public string? ExpectedThumbprint { get; init; }

    // True when this connection established the pin rather than checking one, which is the moment
    // the caller has to persist ActualThumbprint.
    public bool PinnedOnThisConnection { get; init; }

    public int? ServerProtocol { get; init; }

    public int? StatusCode { get; init; }

    public Exception? Error { get; init; }

    public bool Found => Hello is not null;
}
