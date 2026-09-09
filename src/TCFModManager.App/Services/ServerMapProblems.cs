using TCFModManager.Core.Models;
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
    // What happened when the published list was fetched. Separate from Describe because a list
    // failing is not the connection failing - the server answered, which is most of what the user
    // cares about, and the list is the part that went wrong.
    //
    // servingOwnList is the operator looking at their own server: what came back is the list this
    // machine published, so it is already in their mod lists and still theirs to edit. Saying
    // "saved in your mod lists" there would read as a second copy having appeared.
    //
    public static string DescribeList(ServerMapListResult result, ModList? held, bool servingOwnList = false)
        => result.Problem switch
    {
        ServerMapProblem.None when result.List is not null && servingOwnList =>
            $"This server is serving your own list \"{result.List.Name}\" (revision "
            + $"{result.List.Revision}, {Mods(result.List.Entries.Count)}). It stays yours to edit on "
            + "the Mod lists page - publish it again after a change to update what the server hands out.",

        ServerMapProblem.None when result.List is not null =>
            $"\"{result.List.Name}\" (revision {result.List.Revision}, {Mods(result.List.Entries.Count)}) "
            + "is saved in your mod lists. Applying it is done from the Mod lists page, which shows "
            + "what would change first.",

        //
        // Not a failure. A server can run the mod and deliberately publish nothing.
        //
        // Deliberately NOT worded as "has stopped publishing". The held list says only that some
        // server published one once, and the app cannot tell one server from another behind a
        // changed address - so the likeliest reading of this state is that the list came from a
        // DIFFERENT server, which is exactly what it looks like when someone runs two installs and
        // points the app at the second. Announcing a change nobody made sends them looking for an
        // unpublish that never happened.
        //
        ServerMapProblem.NoList when held is not null =>
            $"This server isn't publishing a mod list. \"{held.Name}\" is still in your mod lists as "
            + "you last fetched it - though if you were expecting to see it here, check the address "
            + "is the server you published it to, and that the file reached that server's config "
            + "folder.",

        ServerMapProblem.NoList =>
            "This server doesn't publish a mod list. Its operator publishes one with \"Publish to "
            + "this server\" on the Mod lists page, from the app on the machine running the server.",

        //
        // Two sentences for one status code, because the next action is different. One is "go ask
        // someone", the other is "what you were given is wrong or has been changed".
        //
        ServerMapProblem.KeyRequired =>
            "This server needs a shared key before it will send its list. Ask whoever runs it for "
            + "the key and put it in the box above - they'll find it in the mod's config folder, in "
            + "servermap-key.txt.",

        ServerMapProblem.KeyRejected =>
            "The server didn't accept the key. Check it against the one whoever runs the server has "
            + "- it may have been re-generated since they sent it to you. Dashes and capitals don't "
            + "matter.",

        ServerMapProblem.ListUnreadable =>
            $"The server sent a list this app couldn't read - {result.ParseError}.",

        ServerMapProblem.Unreachable =>
            "The server answered the first time and then stopped, so the list wasn't fetched.",

        ServerMapProblem.CertificateRejected =>
            "The list wasn't fetched - the server's certificate is not the one this app recorded.",

        _ => $"The list couldn't be fetched{(result.Error is null ? "" : $": {result.Error.Message}")}.",
    };

    //
    // How the key is described in Options. Says what it is and is not, because the word "key"
    // invites people to treat it as a password and reuse one.
    //
    public static string DescribeKey(bool hasKey, bool serverRequiresKey) => (hasKey, serverRequiresKey) switch
    {
        (false, true) =>
            "This server needs a key. Whoever runs it will find it in the Server Map mod's config "
            + "folder, in servermap-key.txt.",

        (false, false) =>
            "Only needed if the server asks for one. It's generated by the server, not chosen - it "
            + "says you were told about that server, and nothing more.",

        (true, _) =>
            "Sent with every request except the first handshake. It identifies nobody and protects "
            + "nothing on its own, so it isn't a password - don't reuse one anywhere else.",
    };

    // What a server says it publishes, before anything is fetched.
    public static string DescribePublished(ServerHello hello)
    {
        if (!hello.HasList) return "This server doesn't publish a mod list.";

        var name = string.IsNullOrWhiteSpace(hello.ListName) ? "A mod list" : $"\"{hello.ListName}\"";
        var size = hello.ListEntryCount is { } count ? $", {Mods(count)}" : "";

        return $"{name} (revision {hello.ListRevision?.ToString() ?? "unknown"}{size})";
    }

    private static string Mods(int count) => count == 1 ? "1 mod" : $"{count} mods";

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
