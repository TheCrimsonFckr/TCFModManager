namespace TCFModManager.ServerMap.Stub;

// One response, fully formed by the payload. The stub writes it and understands none of it.
public sealed record PayloadResponse(int StatusCode, string ContentType, string Body);

//
// The entire contract between the stub in user\mods and the payload in TCFModManager\ServerMap\.
// Deliberately this small: every route, every piece of state and every decision lives on the far
// side of it, so the stub never needs rebuilding when the feature changes.
//
public interface IServerMapPayload
{
    // The route prefix this payload owns, e.g. "/tcfservermap". Read once, at load.
    string RoutePrefix { get; }

    Task<PayloadResponse> HandleAsync(string path, string method, CancellationToken cancellationToken);
}
