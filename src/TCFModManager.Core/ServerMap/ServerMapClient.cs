using System.Text.Json;

namespace TCFModManager.Core.ServerMap;

//
// Talks to a TCFMM Server Map server mod over the SPT server's own port.
//
// This works at all because of one thing, found by decompiling SPT 4.1 and then proven against a
// live server: HttpServer picks a listener with CanHandle(context) BEFORE it reads the PHPSESSID
// cookie, and dispatches regardless of whether there is a session behind it. So an ordinary desktop
// app carrying no SPT assemblies is a first-class client of a mod's routes.
//
// One instance per endpoint, disposed with it. Not safe for concurrent calls on one instance: the
// certificate callback records what it saw into state the request in flight then reads back.
//
public sealed class ServerMapClient : IDisposable
{
    // The protocol this build speaks. A server on anything else is refused, not guessed at.
    public const int SupportedProtocol = 1;

    public const string HelloPath = "/tcfservermap/hello";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(6);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ServerMapEndpoint _endpoint;
    private readonly HttpClient _http;
    private readonly PinRecorder _pin;

    private ServerMapClient(ServerMapEndpoint endpoint, HttpMessageHandler handler, Uri baseUri, TimeSpan timeout,
        PinRecorder pin)
    {
        _endpoint = endpoint;
        _pin = pin;
        _http = new HttpClient(handler) { BaseAddress = baseUri, Timeout = timeout };
    }

    //
    // Null when the endpoint cannot be turned into a URL. The caller reports InvalidAddress rather
    // than this throwing, because a half-typed address in an Options box is not exceptional.
    //
    public static ServerMapClient? TryCreate(ServerMapEndpoint endpoint, TimeSpan? timeout = null)
    {
        if (!endpoint.TryGetBaseUri(out var baseUri)) return null;

        var pin = new PinRecorder();

        var handler = new HttpClientHandler
        {
            //
            // HttpClient honours HTTPS_PROXY / HTTP_PROXY from the environment. Left on, a user
            // behind a corporate or VPN proxy has their own LAN server address tunnelled through
            // it - and the failure arrives as a socket reset BEFORE the certificate callback ever
            // runs, which looks like anything except a proxy problem. Found the hard way during the
            // transport spike; this line is the fix and must not be dropped.
            //
            UseProxy = false,

            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                pin.Presented = certificate is null ? null : ServerCertificatePin.Thumbprint(certificate);
                pin.Verdict = ServerCertificatePin.Decide(endpoint.PinnedThumbprint, pin.Presented);

                return ServerCertificatePin.Accepts(pin.Verdict);
            },
        };

        return new ServerMapClient(endpoint, handler, baseUri, timeout ?? DefaultTimeout, pin);
    }

    //
    // The same client over a caller-supplied handler, so the response-handling half - status codes,
    // bodies that are not us, protocol mismatches - can be tested without a TLS stack or a
    // listening socket. No pinning happens on this path, which is why it is not public.
    //
    internal static ServerMapClient? TryCreate(ServerMapEndpoint endpoint, HttpMessageHandler handler,
        TimeSpan? timeout = null) =>
        endpoint.TryGetBaseUri(out var baseUri)
            ? new ServerMapClient(endpoint, handler, baseUri, timeout ?? DefaultTimeout, new PinRecorder())
            : null;

    //
    // The handshake. Cheap and safe to call on a timer: it is what gates the nav item, and the list
    // revision it carries is what avoids fetching a list that has not changed.
    //
    public async Task<ServerHelloProbe> HelloAsync(CancellationToken cancellationToken = default)
    {
        _pin.Reset();

        try
        {
            using var response = await _http
                .GetAsync(HelloPath, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // A 404 from a plain SPT server and a 503 from a stub with no payload mean the same
                // thing to us: there is no server map here.
                return Fail(ServerMapProblem.NotServerMap, statusCode: (int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            ServerHello? hello;
            try
            {
                hello = JsonSerializer.Deserialize<ServerHello>(body, Json);
            }
            catch (JsonException)
            {
                // Something answered 200 on that path without being us.
                return Fail(ServerMapProblem.NotServerMap, statusCode: (int)response.StatusCode);
            }

            // Protocol is required and one-based, so a zero means a body that merely happened to be
            // JSON rather than a handshake.
            if (hello is null || hello.Protocol == 0)
                return Fail(ServerMapProblem.NotServerMap, statusCode: (int)response.StatusCode);

            if (hello.Protocol != SupportedProtocol)
                return Fail(ServerMapProblem.ProtocolMismatch, serverProtocol: hello.Protocol,
                    statusCode: (int)response.StatusCode);

            return new ServerHelloProbe
            {
                Endpoint = _endpoint,
                Hello = hello,
                ActualThumbprint = _pin.Presented,
                ExpectedThumbprint = _endpoint.PinnedThumbprint,
                PinnedOnThisConnection = _pin.Verdict == PinVerdict.FirstUse,
                StatusCode = (int)response.StatusCode,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            //
            // A pin rejection surfaces here as a transport failure, because refusing the certificate
            // is what tore the connection down. The verdict recorded by the callback is the only
            // thing that can tell that apart from the server simply not being there.
            //
            if (_pin.Verdict == PinVerdict.Mismatch) return Fail(ServerMapProblem.CertificateRejected, error: ex);

            return Fail(ServerMapProblem.Unreachable, error: ex);
        }
        catch (Exception ex)
        {
            return Fail(ServerMapProblem.Failed, error: ex);
        }
    }

    private ServerHelloProbe Fail(ServerMapProblem problem, Exception? error = null, int? statusCode = null,
        int? serverProtocol = null) =>
        new()
        {
            Endpoint = _endpoint,
            Problem = problem,
            ActualThumbprint = _pin.Presented,
            ExpectedThumbprint = _endpoint.PinnedThumbprint,
            ServerProtocol = serverProtocol,
            StatusCode = statusCode,
            Error = error,
        };

    public void Dispose() => _http.Dispose();
}

//
// The one piece of shared state between the certificate callback and the request that provoked it.
// It exists because the callback runs inside the handler, which has to be built before the client
// that would otherwise own the fields.
//
internal sealed class PinRecorder
{
    public string? Presented { get; set; }

    public PinVerdict Verdict { get; set; } = PinVerdict.NoCertificate;

    public void Reset()
    {
        Presented = null;
        Verdict = PinVerdict.NoCertificate;
    }
}
