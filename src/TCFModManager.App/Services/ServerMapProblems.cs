using TCFModManager.Core.ServerMap;

namespace TCFModManager.App.Services;

//
// What the user is told when a Server Map handshake doesn't come back. ServerMapClient reports a
// ServerMapProblem plus the values behind it and stops there; the wording lives here, beside the
// rest of this app's prose, for the same reason ModInstallProblems and SptLaunchProblems do.
//
// The tone is deliberate. Most of these are not faults - a wrong port, a server that isn't up, an
// install without the mod. Only the certificate case is worth a warning, and it says what actually
// changed rather than the word "insecure".
//
public static class ServerMapProblems
{
    public static string Describe(ServerHelloProbe probe) => probe.Problem switch
    {
        ServerMapProblem.None => probe.Hello is null
            ? ""
            : $"Connected to {Name(probe)}{SptVersion(probe)}.{FirstConnection(probe)}",

        ServerMapProblem.NoAddress =>
            "No server address set - enter the same address you put in the SPT launcher.",

        ServerMapProblem.InvalidAddress =>
            $"\"{probe.Endpoint.Host}\" and port {probe.Endpoint.Port} don't make an address this "
            + "app can dial. The port has to be a number between 1 and 65535.",

        ServerMapProblem.Unreachable =>
            $"Nothing answered at {probe.Endpoint.Host}:{probe.Endpoint.Port}. The server may not be "
            + "running, or the address or port may be wrong - it's the same one you use in the SPT "
            + "launcher.",

        //
        // Both fingerprints are named because this is the one case where the user has to make a
        // judgement, and they cannot make it without seeing what changed. A mismatch is not
        // necessarily an attack: a server rebuilt from scratch, or reached through a tunnel that
        // terminates TLS itself, presents a different certificate for entirely ordinary reasons.
        //
        ServerMapProblem.CertificateRejected =>
            $"{probe.Endpoint.Host} presented a different certificate than the one this app "
            + "recorded the first time it connected.\n\n"
            + $"Expected:  {Short(probe.ExpectedThumbprint)}\n"
            + $"Presented: {Short(probe.ActualThumbprint)}\n\n"
            + "That happens legitimately if the server was rebuilt or you're reaching it through a "
            + "tunnel that handles the encryption itself. It also happens if something is sitting "
            + "between you and the server. Trust the new one only if you know which it is.",

        //
        // A plain SPT server 404s the route and a stub whose payload is missing 503s it - the same
        // answer either way, so the wording covers both without guessing which.
        //
        ServerMapProblem.NotServerMap =>
            $"Something is running at {probe.Endpoint.Host}:{probe.Endpoint.Port}, but it isn't a "
            + "server with the Server Map mod installed. Ask whoever runs it to add it - the map "
            + "needs the mod on the server, not just this app.",

        ServerMapProblem.ProtocolMismatch when probe.ServerProtocol > ServerMapClient.SupportedProtocol =>
            $"That server's Server Map is newer than this app understands (it speaks version "
            + $"{probe.ServerProtocol}, this app speaks {ServerMapClient.SupportedProtocol}). Update "
            + "TCF Mod Manager.",

        ServerMapProblem.ProtocolMismatch =>
            $"That server's Server Map is older than this app understands (it speaks version "
            + $"{probe.ServerProtocol}, this app speaks {ServerMapClient.SupportedProtocol}). Ask "
            + "whoever runs it to update the mod.",

        // The inner exception is the only thing that says why, so it is quoted rather than summarised.
        _ => $"Couldn't reach {probe.Endpoint.Host}:{probe.Endpoint.Port}: {probe.Error?.Message}",
    };

    //
    // The resting line beside the address box - what this app currently knows about that server,
    // before anything is clicked. Kept here beside the failure wording so the page never says one
    // thing in its status line and another in its message.
    //
    public static string DescribeState(ServerHelloProbe? probe, bool configured) => probe switch
    {
        null when !configured => "Not set up. Enter a server address to connect.",
        null => "Not connected yet.",
        _ => Describe(probe),
    };

    //
    // How the pin is described in Options. Deliberately says what it is FOR: on its own, a hex
    // string in a settings page means nothing to anyone.
    //
    public static string DescribePin(string? thumbprint) => string.IsNullOrWhiteSpace(thumbprint)
        ? "No certificate recorded yet. The first time this app connects it records the one the "
          + "server presents, and warns you if it ever changes."
        : $"Trusting certificate {Short(thumbprint)}. SPT's certificate is self-signed for "
          + "localhost, so this fingerprint - not the name on it - is what identifies the server.";

    //
    // A 64-character hex string is unreadable and unusable for comparison. The ends are what people
    // actually check against each other, so those are what is shown.
    //
    public static string Short(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint)) return "(none)";

        var value = thumbprint.Trim();

        return value.Length <= 20 ? value : $"{value[..8]}...{value[^8..]}";
    }

    private static string Name(ServerHelloProbe probe) =>
        string.IsNullOrWhiteSpace(probe.Hello!.ServerName) ? probe.Endpoint.Host : probe.Hello.ServerName!;

    private static string SptVersion(ServerHelloProbe probe) =>
        string.IsNullOrWhiteSpace(probe.Hello!.SptVersion) ? "" : $", running SPT {probe.Hello.SptVersion}";

    // Said once, on the connection that establishes the pin, so recording it is something the user
    // saw happen rather than something that happened to them.
    private static string FirstConnection(ServerHelloProbe probe) => probe.PinnedOnThisConnection
        ? $" Recorded its certificate ({Short(probe.ActualThumbprint)}) - you'll be warned if it changes."
        : "";
}
