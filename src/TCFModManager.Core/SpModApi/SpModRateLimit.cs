using TCFModManager.Core.Services;

namespace TCFModManager.Core.SpModApi;

//
// Waits out sp-mod's 429 and asks again. sp-mod allows 300 requests a minute per address, shared
// with everything else the app does.
//
public static class SpModRateLimit
{
    private const int MaxRetries = 3;

    // sp-mod's Retry-After is honoured up to this; the window's Cancel is the way out of a long one.
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(20);

    public static async Task<T> RetryingAsync<T>(Func<Task<T>> call, string source, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (SpModApiRateLimitedException ex) when (attempt < MaxRetries)
            {
                var wait = ex.RetryAfter is { } after && after > TimeSpan.Zero
                    ? (after < MaxWait ? after : MaxWait)
                    : DefaultWait;
                AppLog.Warn(source, $"rate limited by sp-mod, waiting {wait.TotalSeconds:0}s");
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }
        }
    }
}
