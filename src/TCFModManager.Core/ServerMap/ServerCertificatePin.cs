using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TCFModManager.Core.ServerMap;

// What the pin decided about a presented certificate.
public enum PinVerdict
{
    // No pin held yet, so this connection establishes one. Trust on first use.
    FirstUse,

    // Matches what was pinned.
    Match,

    // A certificate was presented and it is not the pinned one.
    Mismatch,

    // No certificate at all.
    NoCertificate,
}

//
// Certificate pinning for the Server Map client, and nothing else.
//
// SPT serves a self-signed certificate whose subject and SAN cover localhost only, so a client on
// another machine fails BOTH the chain check and the hostname check, always, at any address.
// Verified against a live 4.1 server: pinning is not a workaround for a misconfigured server, it is
// the only way a remote client connects at all.
//
// The certificate is persisted under <serverRoot>/user/certs/ and does not change across restarts,
// so its thumbprint is a stable identity to pin to.
//
// The decision is a pure function so it can be tested without a TLS stack; the handler in
// ServerMapClient is only the wiring. Never scope this to any other HttpClient - SpModApiClient
// talks to a real CA-signed host and must keep full validation.
//
public static class ServerCertificatePin
{
    public static string Thumbprint(X509Certificate2 certificate) =>
        certificate.GetCertHashString(HashAlgorithmName.SHA256);

    //
    // Whether to accept the certificate, given what was pinned. Chain and hostname errors are
    // deliberately ignored: they are guaranteed for this certificate and say nothing useful. The
    // thumbprint is the whole check.
    //
    // A mismatch is NOT necessarily an attack. Moving between a LAN address and a WireGuard-style
    // overlay keeps the same certificate, but a TLS-terminating tunnel (ngrok, Cloudflare) presents
    // its own - indistinguishable from a man-in-the-middle from here. That is why the presented
    // thumbprint is reported back rather than swallowed: the caller can name both and offer to
    // re-pin.
    //
    public static PinVerdict Decide(string? pinnedThumbprint, string? presentedThumbprint)
    {
        if (string.IsNullOrWhiteSpace(presentedThumbprint)) return PinVerdict.NoCertificate;

        if (string.IsNullOrWhiteSpace(pinnedThumbprint)) return PinVerdict.FirstUse;

        return string.Equals(pinnedThumbprint, presentedThumbprint, StringComparison.OrdinalIgnoreCase)
            ? PinVerdict.Match
            : PinVerdict.Mismatch;
    }

    public static bool Accepts(PinVerdict verdict) =>
        verdict is PinVerdict.FirstUse or PinVerdict.Match;
}
