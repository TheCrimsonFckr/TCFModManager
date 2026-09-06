namespace TCFModManager.Core.ServerMap;

//
// Where a server is and what certificate it is expected to present.
//
// The address is a user setting, never read from the install's own config: on a Fika install
// SPT_Data/Server/configs/http.json is stale, because Fika overrides the binding at runtime from
// its own fika.jsonc - and the value it writes there is 0.0.0.0, a bind address rather than one a
// client can dial. The same address the user already types into the SPT launcher is the honest
// source, so that is what the Options page asks for.
//
public sealed record ServerMapEndpoint(string Host, int Port, string? PinnedThumbprint = null)
{
    public const int DefaultPort = 6969;

    public bool HasPin => !string.IsNullOrWhiteSpace(PinnedThumbprint);

    //
    // https://host:port, with an IPv6 literal bracketed. UriBuilder does not do that for you:
    // given "::1" it produces "https://::1:6969", which does not parse.
    //
    public bool TryGetBaseUri(out Uri uri)
    {
        uri = null!;

        if (string.IsNullOrWhiteSpace(Host)) return false;
        if (Port is <= 0 or > 65535) return false;

        var host = Host.Trim();

        if (host.Contains(':') && !host.StartsWith('[')) host = $"[{host}]";

        return Uri.TryCreate($"https://{host}:{Port}", UriKind.Absolute, out uri!);
    }
}
