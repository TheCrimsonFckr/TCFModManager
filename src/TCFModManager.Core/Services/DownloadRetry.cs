using System.Net;

namespace TCFModManager.Core.Services;

public enum DownloadRetryCause
{
    // The connection dropped, reset or timed out, or the file arrived short.
    Connection,

    // The server answered with 5xx or 408.
    Server,

    // The server answered 429: too many requests for now. Waited out however many times it comes.
    RateLimited,
}

public enum DownloadRetryState
{
    // This download failed and waits Wait before trying again.
    Retrying,

    // Another download got a server error from Host; this one waits Wait before asking it.
    Held,

    // The wait is over and the next attempt has started.
    Resumed,

    // Stopped by DownloadPause; Resumed follows when it carries on. Cause and Wait mean nothing here.
    Paused,
}

public sealed record DownloadRetry(DownloadRetryState State, DownloadRetryCause Cause, string? Host, TimeSpan Wait, int Attempt);

//
// How long to wait before each retry, by cause. A download gives up once the list for the cause it
// just hit is used up. A server's Retry-After replaces that step's wait, up to MaxRetryAfter.
// RateLimited has no list: each 429 waits Retry-After, up to MaxRateLimitWait, or RateLimitDelay
// when none is sent.
//
public sealed record DownloadRetryPolicy(
    IReadOnlyList<TimeSpan> ConnectionDelays,
    IReadOnlyList<TimeSpan> ServerDelays,
    TimeSpan MaxRetryAfter)
{
    public static DownloadRetryPolicy Default { get; } = new(
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)],
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)],
        TimeSpan.FromSeconds(60));

    public static DownloadRetryPolicy None { get; } = new([], [], TimeSpan.Zero);

    public TimeSpan RateLimitDelay { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan MaxRateLimitWait { get; init; } = TimeSpan.FromHours(1);

    public static bool IsServerSide(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

    // Null: a failure that will happen again the same way (a refusal, a name that doesn't resolve, a
    // certificate, a full disk) or a cancellation.
    public static DownloadRetryCause? Classify(Exception ex, CancellationToken ct) => ex switch
    {
        _ when ct.IsCancellationRequested => null,
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => DownloadRetryCause.RateLimited,
        DownloadHttpException http => http.IsServerSide ? DownloadRetryCause.Server : null,
        HttpRequestException { StatusCode: { } code } => IsServerSide(code) ? DownloadRetryCause.Server : null,
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded or HttpRequestError.Unknown } => DownloadRetryCause.Connection,
        HttpIOException => DownloadRetryCause.Connection,
        IOException { InnerException: System.Net.Sockets.SocketException } => DownloadRetryCause.Connection,
        ModInstallException { Reason: ModInstallFailure.DownloadIncomplete } => DownloadRetryCause.Connection,
        TaskCanceledException => DownloadRetryCause.Connection,
        _ => null,
    };
}
