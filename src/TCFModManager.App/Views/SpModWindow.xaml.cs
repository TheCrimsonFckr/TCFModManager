using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

//
// OPEN-76: sp-mod's sign-in page, or the signed-in user's account page, on the profile every web view
// shares. Only sp-mod and, for Sign in with Discord, Discord stay in the window; anything else opens
// in the browser. Signing in closes the window once sp-mod's account page answers for the user.
//
public partial class SpModWindow : FluentWindow
{
    private static readonly string[] SignInPaths =
    [
        "/login", "/register", "/forgot-password", "/reset-password", "/two-factor-challenge",
        "/email/verify", "/user/confirm-password",
    ];

    private readonly WebView2 _view = new();

    private readonly bool _signingIn;

    private SpModWindow(Window? owner, Uri start, bool signingIn)
    {
        InitializeComponent();

        _signingIn = signingIn;
        Title = signingIn ? Strings.SpModWindow_SignInTitle : Strings.SpModWindow_Title;
        WindowTitleBar.Title = Title;
        SignInHint.Visibility = signingIn ? Visibility.Visible : Visibility.Collapsed;

        Owner = owner;
        WindowStartupLocation = owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        Host.Child = _view;
        Loaded += async (_, _) => await StartAsync(start);
        Closed += (_, _) =>
        {
            Host.Child = null;
            _view.Dispose();
            _ = AppServices.SpModAccount.CheckAsync();
        };
    }

    public static void SignIn(Window? owner) => new SpModWindow(owner, SpModAccountViewModel.SignInPage, signingIn: true).ShowDialog();

    public static void Open(Window? owner, Uri page) => new SpModWindow(owner, page, signingIn: false).ShowDialog();

    private async Task StartAsync(Uri start)
    {
        if (!await WebViews.InitializeAsync(_view))
        {
            Close();
            return;
        }

        var core = _view.CoreWebView2;

        core.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme == "about" || WebViews.IsSignInPage(uri)) return;

            e.Cancel = true;
            MarkupActions.OpenInBrowser(e.Uri);
        };

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && WebViews.IsSignInPage(uri)) core.Navigate(e.Uri);
            else MarkupActions.OpenInBrowser(e.Uri);
        };

        core.SourceChanged += (_, _) => ShowAddress(core);

        core.NavigationCompleted += async (_, e) =>
        {
            ShowAddress(core);
            if (_signingIn && e.IsSuccess) await CloseWhenSignedInAsync(core);
        };

        ShowAddress(core);
        core.Navigate(start.AbsoluteUri);
    }

    private void ShowAddress(CoreWebView2 core)
    {
        Address.Text = Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.GetLeftPart(UriPartial.Path)
            : string.Empty;
    }

    private async Task CloseWhenSignedInAsync(CoreWebView2 core)
    {
        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) || !WebViews.IsSpModPage(uri)) return;
        if (SignInPaths.Any(p => uri.AbsolutePath.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return;

        await AppServices.SpModAccount.CheckAsync();
        if (!AppServices.SpModAccount.IsSignedIn || !IsLoaded) return;

        AppLog.Info("SpModAccount", "signed in, closing the sign-in window");
        Close();
    }
}
