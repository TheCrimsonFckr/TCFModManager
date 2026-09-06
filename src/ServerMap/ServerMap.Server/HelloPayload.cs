using System.Reflection;
using System.Text.Json;
using TCFModManager.ServerMap.Stub;

namespace TCFModManager.ServerMap;

//
// The spike payload. It answers exactly one route, /tcfservermap/hello, which is the handshake the
// design has TCFMM probe to decide whether to show its Server Map page at all.
//
// /hello is unauthenticated on purpose: it is what a client asks before the user has entered a key,
// and it discloses nothing a port scan would not. Everything else the feature grows will sit behind
// the shared key.
//
public sealed class HelloPayload : IServerMapPayload
{
    private const int Protocol = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string RoutePrefix => "/tcfservermap";

    public Task<PayloadResponse> HandleAsync(string path, string method, CancellationToken cancellationToken)
    {
        var route = path.Length > RoutePrefix.Length ? path[RoutePrefix.Length..] : "";

        if (!route.Equals("/hello", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(NotFound(route));
        }

        var hello = new
        {
            protocol = Protocol,
            modVersion = ModVersion(),
            serverName = Environment.MachineName,
            requiresKey = false,
            hasList = false,
            capabilities = new[] { "hello" },
            // Proof the payload is what answered, not the stub - the point of the whole spike.
            payloadPath = Assembly.GetExecutingAssembly().Location,
        };

        return Task.FromResult(new PayloadResponse(200, "application/json", JsonSerializer.Serialize(hello, Json)));
    }

    private static PayloadResponse NotFound(string route)
    {
        var body = new { protocol = Protocol, error = $"No route '{route}'.", routes = new[] { "/hello" } };

        return new PayloadResponse(404, "application/json", JsonSerializer.Serialize(body, Json));
    }

    private static string ModVersion() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0";
}
