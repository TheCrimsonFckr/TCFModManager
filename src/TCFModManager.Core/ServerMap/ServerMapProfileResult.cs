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

    // The host hides profiles other machines made, and this machine didn't make it.
    Hidden,
}

// The host's profile rules as they apply to this machine, and the profiles it made (/profiles).
public sealed record ServerMapProfileRules
{
    public bool HideOtherProfiles { get; init; }

    public bool ProtectOtherProfiles { get; init; }

    public IReadOnlySet<string> Owned { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool Owns(string profileId) => Owned.Contains(profileId);
}

public sealed record ServerMapProfileRulesResult
{
    public required ServerMapEndpoint Endpoint { get; init; }

    public ServerMapProfileRules? Rules { get; init; }

    public ServerMapProblem Problem { get; init; }

    public int? StatusCode { get; init; }

    public Exception? Error { get; init; }

    public bool Succeeded => Problem == ServerMapProblem.None;
}

// Why /claim turned a claim down.
public enum ServerMapClaimRefusal
{
    None,
    OwnedByAnother,
    NoSuchProfile,
    TooOld,
    NotSaved,
}

public sealed record ServerMapClaimResult
{
    public required ServerMapEndpoint Endpoint { get; init; }

    public bool Claimed { get; init; }

    public ServerMapClaimRefusal Refusal { get; init; }

    public ServerMapProblem Problem { get; init; }

    public int? StatusCode { get; init; }

    public Exception? Error { get; init; }

    public bool Succeeded => Problem == ServerMapProblem.None;
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
