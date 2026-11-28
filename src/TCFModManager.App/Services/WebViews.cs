using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The one WebView2 environment the app's embedded pages share: sp-mod's comments in a mod's details
// (OPEN-12 A9) and the sp-mod sign-in window (OPEN-76).
//
// Its profile (cookies - so an sp-mod sign-in lasts) lives in Data\WebView2 with the rest of this
// app's data. Anything that uses a web view asks IsAvailable first and falls back to the browser
// without one: the WebView2 runtime ships with Windows 11 and current Windows 10, but can be missing
// on an old or stripped-down install.
//
public static class WebViews
{
    private static bool? _available;
    private static Task<CoreWebView2Environment>? _environment;
    private static Task<CoreWebView2CookieManager?>? _cookies;
    private static CoreWebView2Controller? _cookieController;

    public static bool IsAvailable
    {
        get
        {
            if (_available is { } known) return known;

            try
            {
                _available = !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or DllNotFoundException or FileNotFoundException or BadImageFormatException or System.Runtime.InteropServices.COMException)
            {
                AppLog.Info("WebView", $"no WebView2 runtime: {ex.Message}");
                _available = false;
            }

            return _available.Value;
        }
    }

    private static Task<CoreWebView2Environment> EnvironmentAsync() =>
        _environment ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: Path.Combine(AppPaths.DataDirectory, "webView2"));

    /// <summary>Starts <paramref name="view"/> on the shared environment. False when it could not
    /// start, in which case the caller falls back to the browser.</summary>
    public static async Task<bool> InitializeAsync(WebView2 view)
    {
        if (!IsAvailable) return false;

        try
        {
            if (view.CoreWebView2 is null) await view.EnsureCoreWebView2Async(await EnvironmentAsync());
            if (view.CoreWebView2 is not { } core) return false;

            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            return true;
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            AppLog.Warn("WebView", $"could not start a web view: {ex.Message}");
            return false;
        }
    }

    /// <summary>The shared profile's cookies, read through a hidden web view on the main window. Null
    /// without the runtime or when it could not start.</summary>
    public static async Task<CoreWebView2CookieManager?> CookiesAsync()
    {
        var cookies = await (_cookies ??= StartCookiesAsync());
        if (cookies is null) _cookies = null;
        return cookies;
    }

    private static async Task<CoreWebView2CookieManager?> StartCookiesAsync()
    {
        if (!IsAvailable || Application.Current?.MainWindow is not { } window) return null;

        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            _cookieController = await (await EnvironmentAsync()).CreateCoreWebView2ControllerAsync(handle);
            _cookieController.IsVisible = false;
            return _cookieController.CoreWebView2.CookieManager;
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            AppLog.Warn("WebView", $"could not start the cookie reader: {ex.Message}");
            return null;
        }
    }

    /// <summary>An sp-mod page - the only pages a web view here navigates to; anything else opens in the browser.</summary>
    public static bool IsSpModPage(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("sp-mod.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".sp-mod.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>A page the sp-mod sign-in window may show: sp-mod, or Discord for sp-mod's Sign in
    /// with Discord (OPEN-76 R-S7).</summary>
    public static bool IsSignInPage(Uri uri) =>
        IsSpModPage(uri) ||
        (uri.Scheme == Uri.UriSchemeHttps &&
         (uri.Host.Equals("discord.com", StringComparison.OrdinalIgnoreCase) ||
          uri.Host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase)));
}
