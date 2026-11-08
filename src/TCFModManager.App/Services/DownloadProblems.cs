using System.Globalization;
using System.Net;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What the user is told when a download's server fails, and while a download waits to try again.
//
public static class DownloadProblems
{
    public static string Failed(DownloadHttpException problem) =>
        Failed(problem.Status, problem.Host is null || problem.IsSpMod, problem.Host);

    public static string Failed(HttpStatusCode status, bool spMod, string? host)
    {
        var (spModFormat, hostFormat) = status switch
        {
            HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout =>
                (Strings.Downloads_ServerTimeoutSpModFormat, Strings.Downloads_ServerTimeoutHostFormat),
            HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable =>
                (Strings.Downloads_ServerBusySpModFormat, Strings.Downloads_ServerBusyHostFormat),
            _ =>
                (Strings.Downloads_ServerErrorSpModFormat, Strings.Downloads_ServerErrorHostFormat),
        };

        return Text(spMod ? spModFormat : hostFormat, (int)status, host);
    }

    // Null once the wait is over: the line goes back to what the download was doing.
    public static string? Waiting(DownloadRetry retry)
    {
        if (retry.State == DownloadRetryState.Resumed) return null;

        if (retry.Cause == DownloadRetryCause.Connection)
            return Text(Strings.Downloads_RetryingFormat, retry.Wait.TotalSeconds);

        var spMod = retry.Host is null || DownloadHttpException.IsSpModHost(retry.Host);
        var format = retry.State == DownloadRetryState.Held
            ? (spMod ? Strings.Downloads_HeldSpModFormat : Strings.Downloads_HeldHostFormat)
            : (spMod ? Strings.Downloads_RetryingServerSpModFormat : Strings.Downloads_RetryingServerHostFormat);

        return Text(format, retry.Wait.TotalSeconds, retry.Host);
    }

    private static string Text(string format, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, format, values);
}
