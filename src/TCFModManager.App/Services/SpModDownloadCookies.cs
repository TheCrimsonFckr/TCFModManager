using System.Net;
using System.Net.Http;
using Microsoft.Web.WebView2.Core;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-76 N2: the cookie jar of the client every download goes through (AppServices.Downloads).
// While the user is signed in it holds the web view profile's sp-mod cookies, so sp-mod records a
// download made here against the account, as it would one made on the site. Kept in memory only.
// The jar's own domain matching keeps them off the file hosts sp-mod redirects to, and a Secure
// cookie only goes over https (R-S2).
//
internal static class SpModDownloadCookies
{
    private static readonly CookieContainer Jar = new();

    public static HttpClient CreateClient() => new(new HttpClientHandler { UseCookies = true, CookieContainer = Jar });

    public static void Use(IEnumerable<CoreWebView2Cookie> cookies)
    {
        Clear();

        var added = 0;
        foreach (var cookie in cookies)
        {
            if (!WebViews.IsSpModPage(new Uri($"https://{cookie.Domain.TrimStart('.')}/"))) continue;

            try
            {
                Jar.Add(cookie.ToSystemNetCookie());
                added++;
            }
            catch (CookieException)
            {
                AppLog.Debug("SpModAccount", $"download client skipped cookie {cookie.Name}");
            }
        }

        AppLog.Info("SpModAccount", $"downloads carry the sign-in: {added} cookie(s)");
    }

    public static void Clear()
    {
        var cleared = 0;
        foreach (Cookie cookie in Jar.GetAllCookies())
        {
            if (cookie.Expired || !WebViews.IsSpModPage(new Uri($"https://{cookie.Domain.TrimStart('.')}/"))) continue;

            cookie.Expired = true;
            cleared++;
        }

        if (cleared > 0) AppLog.Info("SpModAccount", $"downloads no longer carry a sign-in: {cleared} cookie(s) cleared");
    }
}
