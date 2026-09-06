using System.Reflection;
using System.Text;
using System.Text.Json;
using TCFModManager.ServerMap.Stub;

namespace TCFModManager.ServerMap;

//
// The spike payload. Two routes, one per open question:
//
//   GET  /tcfservermap/hello  - the handshake TCFMM probes to decide whether to show its page.
//                               Unauthenticated on purpose: asked before the user has a key, and
//                               it discloses nothing a port scan would not.
//   POST /tcfservermap/echo   - spike S3. Reports the request body exactly as it arrived, so we can
//                               see whether anything between a plain HttpClient and this listener
//                               compresses or rewrites it.
//
public sealed class SpikePayload : IServerMapPayload
{
    private const int Protocol = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string RoutePrefix => "/tcfservermap";

    public Task<PayloadResponse> HandleAsync(PayloadRequest request, CancellationToken cancellationToken)
    {
        var route = request.Path.Length > RoutePrefix.Length ? request.Path[RoutePrefix.Length..] : "";

        return Task.FromResult(route.ToLowerInvariant() switch
        {
            "/hello" => Hello(),
            "/echo" => Echo(request),
            _ => NotFound(route),
        });
    }

    private static PayloadResponse Hello()
    {
        var body = new
        {
            protocol = Protocol,
            modVersion = ModVersion(),
            serverName = Environment.MachineName,
            requiresKey = false,
            hasList = false,
            capabilities = new[] { "hello", "echo" },
            // Proof the payload is what answered, not the stub.
            payloadPath = Assembly.GetExecutingAssembly().Location,
        };

        return new PayloadResponse(200, "application/json", JsonSerializer.Serialize(body, Json));
    }

    //
    // Everything needed to settle S3 in one reply: what arrived, how long it was, whether the first
    // bytes carry a compression header, and whether it reads back as the text that was sent.
    //
    private static PayloadResponse Echo(PayloadRequest request)
    {
        var body = request.Body;

        var looksZlib = body.Length >= 2 && body[0] == 0x78 && body[1] is 0x01 or 0x5E or 0x9C or 0xDA;
        var looksGzip = body.Length >= 2 && body[0] == 0x1F && body[1] == 0x8B;

        string? asText = null;
        try
        {
            asText = new UTF8Encoding(false, true).GetString(body);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8, which would itself mean something rewrote it.
        }

        var report = new
        {
            protocol = Protocol,
            method = request.Method,
            query = request.Query,
            receivedBytes = body.Length,
            firstBytesHex = Convert.ToHexString(body.AsSpan(0, Math.Min(16, body.Length))),
            looksZlibCompressed = looksZlib,
            looksGzipCompressed = looksGzip,
            isValidUtf8 = asText is not null,
            bodyAsText = asText,
            // The headers that would say whether anything claimed to compress it.
            contentType = Header(request, "Content-Type"),
            contentLength = Header(request, "Content-Length"),
            contentEncoding = Header(request, "Content-Encoding"),
            requestCompressed = Header(request, "requestcompressed"),
            allHeaders = request.Headers,
        };

        return new PayloadResponse(200, "application/json", JsonSerializer.Serialize(report, Json));
    }

    private static string? Header(PayloadRequest request, string name) =>
        request.Headers.TryGetValue(name, out var value) ? value : null;

    private static PayloadResponse NotFound(string route)
    {
        var body = new { protocol = Protocol, error = $"No route '{route}'.", routes = new[] { "/hello", "/echo" } };

        return new PayloadResponse(404, "application/json", JsonSerializer.Serialize(body, Json));
    }

    private static string ModVersion() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0";
}
