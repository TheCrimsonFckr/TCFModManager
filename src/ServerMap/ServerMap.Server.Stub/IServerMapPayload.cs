namespace TCFModManager.ServerMap.Stub;

//
// One request, flattened. The stub is the only thing that touches ASP.NET types; the payload gets
// plain data, so widening the feature never means rebuilding the stub sitting in user\mods.
//
// Body is the raw bytes as they arrived. Nothing between the wire and here decompresses or rewrites
// them - see the /echo route, which exists to prove exactly that.
//
public sealed record PayloadRequest(
    string Method,
    string Path,
    string? Query,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body);

// One response, fully formed by the payload. The stub writes it and understands none of it.
public sealed record PayloadResponse(int StatusCode, string ContentType, string Body);

//
// The entire contract between the stub in user\mods and the payload in TCFModManager\ServerMap\.
// Deliberately this small: every route, every piece of state and every decision lives on the far
// side of it.
//
public interface IServerMapPayload
{
    // The route prefix this payload owns, e.g. "/tcfservermap". Read once, at load.
    string RoutePrefix { get; }

    Task<PayloadResponse> HandleAsync(PayloadRequest request, CancellationToken cancellationToken);
}
