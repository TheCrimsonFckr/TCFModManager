using Microsoft.AspNetCore.Http;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;

namespace TCFModManager.ServerMap.Stub;

//
// SPT 4.1's IHttpListener: CanHandle takes only the HttpContext (the session is resolved after
// listener selection, and is no longer part of it), and Handle became HandleAsync with a
// CancellationToken. A listener written against 4.0.13 does not compile here.
//
[Injectable(InjectionType.Singleton)]
public class ServerMapListener(ISptLogger<ServerMapListener> logger) : IHttpListener
{
    // Set by ModEntry once the payload is found. Null means the payload folder is gone.
    public static IServerMapPayload? Payload { get; set; }

    // Kept separately from Payload so an unloaded stub still recognises its own routes and can say
    // why it isn't serving them, rather than falling through to a 404 from somewhere else.
    public static string RoutePrefix { get; set; } = "/tcfservermap";

    public bool CanHandle(HttpContext context) =>
        context.Request.Path.StartsWithSegments(RoutePrefix, StringComparison.OrdinalIgnoreCase);

    public async Task HandleAsync(MongoId sessionId, HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (Payload is null)
        {
            logger.Warning(
                $"[TCFMM ServerMap] {path} was requested but the payload is not loaded - see the startup log.");
            context.Response.StatusCode = 503;
            return;
        }

        try
        {
            var response = await Payload.HandleAsync(path, context.Request.Method, cancellationToken)
                .ConfigureAwait(false);

            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = response.ContentType;
            await context.Response.WriteAsync(response.Body, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error($"[TCFMM ServerMap] Error handling {path}: {ex}");
            context.Response.StatusCode = 500;
        }
    }
}
