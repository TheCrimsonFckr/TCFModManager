using System.Net;

namespace TCFModManager.Core.Services;

//
// A download the server answered with a status that isn't success. Host is the server that sent it,
// after any redirects (sp-mod or the file host its link points at).
//
public sealed class DownloadHttpException(string message, HttpStatusCode status, string? host, TimeSpan? retryAfter)
    : HttpRequestException(message, null, status)
{
    public HttpStatusCode Status { get; } = status;

    public string? Host { get; } = host;

    public bool IsSpMod { get; } = IsSpModHost(host);

    // From the Retry-After header, as a delta or a date; never negative.
    public TimeSpan? RetryAfter { get; } = retryAfter;

    public bool IsServerSide => DownloadRetryPolicy.IsServerSide(Status);

    public static DownloadHttpException From(HttpResponseMessage response)
    {
        string message;
        try
        {
            response.EnsureSuccessStatusCode();
            message = $"Response status code does not indicate success: {(int)response.StatusCode}.";
        }
        catch (HttpRequestException ex)
        {
            message = ex.Message;
        }

        var retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;

        return new DownloadHttpException(message, response.StatusCode, response.RequestMessage?.RequestUri?.Host, retryAfter);
    }

    private static bool IsSpModHost(string? host) =>
        host is not null
        && (host.Equals("sp-mod.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".sp-mod.com", StringComparison.OrdinalIgnoreCase));
}
