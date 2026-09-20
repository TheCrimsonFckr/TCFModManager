using System.Globalization;
using System.Net.Http;
using TCFModManager.App.Localization;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Services;

//
// What the user is told when a call to sp-mod.com fails.
//
// Five view models each carried the same three sentences, sixteen copies in all, because every page
// that talks to the catalog ends in the same catch triple. The cost was never the duplication - it
// was that rewording one meant finding the other fifteen, and missing one left the app describing
// the same failure two ways.
//
// This deliberately words the exception rather than replacing the catch blocks: which exceptions a
// page catches is its own decision and stays where it is.
//
public static class ApiProblems
{
    //
    // Rate limiting is checked first because SpModApiRateLimitedException derives from
    // SpModApiException - which is also why this improves three call sites for free. Browse,
    // Downloads and the app updater only ever caught the base type, so a rate limit reached them
    // worded as a generic "sp-mod.com error" with a status code in it. They now say how long to
    // wait, without their catch clauses changing at all.
    //
    public static string Describe(Exception ex) => ex switch
    {
        SpModApiRateLimitedException limited => string.Format(
            CultureInfo.CurrentCulture,
            Strings.Api_RateLimitedFormat,
            limited.RetryAfter?.TotalSeconds ?? 30),

        SpModApiException => string.Format(CultureInfo.CurrentCulture, Strings.Api_ErrorFormat, ex.Message),

        HttpRequestException => string.Format(CultureInfo.CurrentCulture, Strings.Api_NetworkErrorFormat, ex.Message),

        _ => string.Format(CultureInfo.CurrentCulture, Strings.Api_UnreachableFormat, ex.Message),
    };
}
