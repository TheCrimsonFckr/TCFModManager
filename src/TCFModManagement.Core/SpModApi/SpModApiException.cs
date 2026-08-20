using System.Net;

namespace TCFModManagement.Core.SpModApi;

// Thrown for any non-2xx response from the sp-mod.com API.
public class SpModApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    // The API's machine-readable error code (e.g. "VALIDATION_FAILED", "NOT_FOUND"), if any.
    public string? Code { get; }

    public SpModApiException(HttpStatusCode statusCode, string? code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

// Thrown for HTTP 429 responses. Callers should back off for RetryAfter before retrying.
public sealed class SpModApiRateLimitedException : SpModApiException
{
    public TimeSpan? RetryAfter { get; }

    public SpModApiRateLimitedException(string? code, string message, TimeSpan? retryAfter)
        : base(HttpStatusCode.TooManyRequests, code, message)
    {
        RetryAfter = retryAfter;
    }
}
