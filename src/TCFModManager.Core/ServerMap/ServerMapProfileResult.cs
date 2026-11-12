using TCFModManager.Core.Services;

namespace TCFModManager.Core.ServerMap;

// What /profile said about one profile.
public enum ServerMapProfileState
{
    // Read. Carries Stats and Written.
    Found,

    // The same as the copy whose Written was sent.
    Unchanged,

    // The server has no profile by that id.
    Missing,

    // The server caught the file mid-save. Worth asking again shortly.
    Busy,
}

public sealed record ServerMapProfileResult
{
    public required ServerMapEndpoint Endpoint { get; init; }

    public ServerMapProfileState State { get; init; }

    public SptProfileStats? Stats { get; init; }

    // The server's write time for the file, sent back as "since" next time.
    public long? Written { get; init; }

    public ServerMapProblem Problem { get; init; }

    public int? StatusCode { get; init; }

    public Exception? Error { get; init; }

    public bool Succeeded => Problem == ServerMapProblem.None;
}
