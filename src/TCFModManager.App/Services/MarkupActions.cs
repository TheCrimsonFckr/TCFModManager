using System.Diagnostics;
using System.Text.RegularExpressions;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What a click inside a rendered description or changelog does (OPEN-12 F18). Everything opens in
// the browser - a link, a picture full size, a video on YouTube. SSPTMM opens pictures and videos in
// a viewer of its own (WebView2), which this app does not carry (R19).
//
public static class MarkupActions
{
    // Hosts whose links are the picture itself rather than a page about it.
    private static readonly Regex PictureLink = new(
        @"\.(png|jpe?g|gif|webp|bmp)(\?|#|$)|^https?://i\.imgur\.com/", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void OpenLink(string url) => OpenInBrowser(url);

    public static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("Links", $"could not open {url}: {ex.Message}");
        }
    }

    // True when a link around a picture points at a picture too, so a click shows the picture itself
    // rather than the page the link names.
    public static bool IsPictureLink(string? href, string imageSource) =>
        href is null
        || string.Equals(StripQuery(href), StripQuery(imageSource), StringComparison.OrdinalIgnoreCase)
        || PictureLink.IsMatch(href);

    private static string StripQuery(string url)
    {
        var cut = url.IndexOfAny(['?', '#']);
        return cut < 0 ? url : url[..cut];
    }
}
