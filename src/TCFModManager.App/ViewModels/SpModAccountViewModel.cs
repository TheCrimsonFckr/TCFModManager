using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

public enum SpModSignIn
{
    Unknown,
    SignedOut,
    SignedIn,
    Remembered,
}

//
// OPEN-76: signing in to sp-mod. The sign-in is sp-mod's own page in SpModWindow, on the profile every
// web view shares (WebViews). Whether it holds is read from that profile's sp-mod cookies and one
// request for sp-mod's account page, which sp-mod answers only for a signed-in user. Cookie names may
// be logged, never their values (R-S3).
//
public sealed partial class SpModAccountViewModel : LocalizedViewModel
{
    public static readonly Uri SignInPage = new("https://sp-mod.com/login");

    public static readonly Uri AccountPage = new("https://sp-mod.com/user/profile");

    private static readonly Uri Site = new("https://sp-mod.com/");

    private static readonly Uri Discord = new("https://discord.com/");

    private const string RememberCookiePrefix = "remember_web_";

    private const string SessionCookie = "the_forge_session";

    private const string WebView2Download = "https://developer.microsoft.com/microsoft-edge/webview2/";

    private Task? _check;

    private bool _started;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(CanSignIn), nameof(Status), nameof(TitleBarToolTip))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(SignOutCommand), nameof(OpenAccountCommand))]
    private SpModSignIn _state = SpModSignIn.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    private bool _couldNotCheck;

    public bool IsAvailable => WebViews.IsAvailable;

    public bool IsUnavailable => !IsAvailable;

    public bool IsSignedIn => State is SpModSignIn.SignedIn or SpModSignIn.Remembered;

    public bool CanSignIn => IsAvailable && !IsSignedIn;

    public string Status
    {
        get
        {
            if (!IsAvailable) return Strings.SpModAccount_StatusNoWebView2;

            return State switch
            {
                SpModSignIn.Remembered => Strings.SpModAccount_StatusRemembered,
                SpModSignIn.SignedIn => Strings.SpModAccount_StatusSession,
                SpModSignIn.SignedOut => Strings.SpModAccount_StatusSignedOut,
                _ when IsChecking => Strings.SpModAccount_StatusChecking,
                _ when CouldNotCheck => Strings.SpModAccount_StatusCouldNotCheck,
                _ => Strings.SpModAccount_StatusSignedOut,
            };
        }
    }

    public string TitleBarToolTip => IsSignedIn ? Strings.SpModAccount_SignedIn : Strings.SpModAccount_SignInToolTip;

    public async Task StartAsync()
    {
        if (_started || !IsAvailable) return;
        _started = true;

        await EndUnrememberedSignInAsync();
        await CheckAsync();
        LogProfileEncryption();
    }

    public async Task CheckAsync()
    {
        while (_check is { IsCompleted: false } running) await running;

        _check = RunCheckAsync();
        await _check;
    }

    private async Task RunCheckAsync()
    {
        if (!IsAvailable) return;

        IsChecking = true;

        try
        {
            var cookies = await SpModCookiesAsync();
            if (cookies is null)
            {
                CouldNotCheck = true;
                return;
            }

            AppLog.Info("SpModAccount", $"{cookies.Count} sp-mod cookie(s): {string.Join(", ", cookies.Select(c => c.Name))}");

            if (cookies.All(c => c.Name.Equals("XSRF-TOKEN", StringComparison.OrdinalIgnoreCase)))
            {
                CouldNotCheck = false;
                State = SpModSignIn.SignedOut;
                SpModDownloadCookies.Clear();
                return;
            }

            var signedIn = await AskSpModAsync(cookies);
            if (signedIn is null)
            {
                CouldNotCheck = true;
                return;
            }

            CouldNotCheck = false;
            State = signedIn.Value
                ? cookies.Any(c => c.Name.StartsWith(RememberCookiePrefix, StringComparison.OrdinalIgnoreCase)) ? SpModSignIn.Remembered : SpModSignIn.SignedIn
                : SpModSignIn.SignedOut;

            AppLog.Info("SpModAccount", $"state: {State}");

            if (IsSignedIn) SpModDownloadCookies.Use(cookies);
            else SpModDownloadCookies.Clear();
        }
        finally
        {
            IsChecking = false;
        }
    }

    private static async Task<IReadOnlyList<CoreWebView2Cookie>?> SpModCookiesAsync()
    {
        if (await WebViews.CookiesAsync() is not { } manager) return null;

        try
        {
            return await manager.GetCookiesAsync(Site.AbsoluteUri);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn("SpModAccount", $"could not read the cookies: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool?> AskSpModAsync(IEnumerable<CoreWebView2Cookie> cookies)
    {
        var jar = new CookieContainer();
        foreach (var cookie in cookies)
        {
            try
            {
                jar.Add(cookie.ToSystemNetCookie());
            }
            catch (CookieException)
            {
                AppLog.Debug("SpModAccount", $"skipped cookie {cookie.Name}");
            }
        }

        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = jar };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(new SpModApiOptions().UserAgent);

        try
        {
            using var response = await http.GetAsync(AccountPage, HttpCompletionOption.ResponseHeadersRead);
            var code = (int)response.StatusCode;

            if (code is >= 200 and < 300) return true;

            if (code is >= 300 and < 400)
            {
                var to = response.Headers.Location?.ToString() ?? string.Empty;
                AppLog.Debug("SpModAccount", $"account page sent {code} to {to}");
                return !to.Contains("/login", StringComparison.OrdinalIgnoreCase);
            }

            AppLog.Info("SpModAccount", $"account page answered {code}");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            AppLog.Info("SpModAccount", $"could not reach sp-mod: {ex.Message}");
            return null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private void SignIn() => SpModWindow.SignIn(Application.Current?.MainWindow);

    [RelayCommand(CanExecute = nameof(IsSignedIn))]
    private void OpenAccount() => SpModWindow.Open(Application.Current?.MainWindow, AccountPage);

    [RelayCommand(CanExecute = nameof(IsSignedIn))]
    private async Task SignOutAsync()
    {
        if (await WebViews.CookiesAsync() is not { } manager) return;

        var deleted = 0;

        try
        {
            foreach (var site in new[] { Site, Discord })
            {
                foreach (var cookie in await manager.GetCookiesAsync(site.AbsoluteUri))
                {
                    manager.DeleteCookie(cookie);
                    deleted++;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn("SpModAccount", $"could not delete the cookies: {ex.Message}");
        }

        AppLog.Info("SpModAccount", $"signed out, {deleted} cookie(s) deleted");
        AppServices.EndorsedMods.Clear();
        State = SpModSignIn.SignedOut;
        await CheckAsync();
    }

    private static async Task EndUnrememberedSignInAsync()
    {
        if (await WebViews.CookiesAsync() is not { } manager) return;

        try
        {
            var cookies = await manager.GetCookiesAsync(Site.AbsoluteUri);
            if (cookies.Count == 0 || cookies.Any(c => c.Name.StartsWith(RememberCookiePrefix, StringComparison.OrdinalIgnoreCase))) return;

            var sessions = cookies.Where(c => c.Name.Equals(SessionCookie, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var cookie in sessions) manager.DeleteCookie(cookie);
            AppLog.Info("SpModAccount", $"no Remember me: ended last run's sign-in, {sessions.Count} session cookie(s) deleted");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn("SpModAccount", $"could not end last run's sign-in: {ex.Message}");
        }
    }

    [RelayCommand]
    private static void GetWebView2() => MarkupActions.OpenInBrowser(WebView2Download);

    private static void LogProfileEncryption()
    {
        var localState = Path.Combine(AppPaths.DataDirectory, "webView2", "EBWebView", "Local State");

        try
        {
            using var stream = new FileStream(localState, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = JsonDocument.Parse(stream);
            var encrypted = json.RootElement.TryGetProperty("os_crypt", out var crypt) && crypt.TryGetProperty("encrypted_key", out _);
            AppLog.Info("SpModAccount", $"web view profile cookies encrypted with the Windows user's key: {encrypted}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Info("SpModAccount", $"could not read the web view profile's Local State: {ex.Message}");
        }
    }
}
