using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using TCFModManager.App.Behaviors;
using TCFModManager.App.Help;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App;

public partial class MainWindow : FluentWindow
{
    private void ApplyIcons()
    {
        Icon = AppIcons.ForWindow;
        TitleBarIcon.Source = AppIcons.ForTitleBar;
    }

    public MainWindow()
    {
        InitializeComponent();

        // Before the window is shown, not on Loaded: WindowStartupLocation reads Width/Height while
        // it decides where to put the window, and by Loaded it has already decided. This also wires
        // F11/Escape and records the window's position on close - see WindowLayout.
        WindowLayout.Attach(this, RootTitleBar);

        // Cards snap rather than glide while the window is being resized by hand (OPEN-26).
        Motion.TrackWindowDrag(this);

        ApplyIcons();
        AppIcons.Watch();
        AppIcons.Changed += (_, _) => ApplyIcons();

        // F1 is the title bar's "?" (Help R8). Preview, so a focused text box doesn't get it first.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F1 || Keyboard.Modifiers != ModifierKeys.None) return;

            ShowHelp();
            e.Handled = true;
        };

        // OPEN-52 N20/N21: Ctrl+F, F5, and a sp-mod link pasted outside a text box.
        PreviewKeyDown += (_, e) =>
        {
            if (RootContentDialogPresenter.Content is not null) return;

            if (PageKeys.TryHandle(RootNavigationView, e))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control
                && Keyboard.FocusedElement is not (TextBoxBase or System.Windows.Controls.PasswordBox or System.Windows.Controls.ComboBox)
                && ClipboardText() is { } text
                && SpModPageLink.Parse(text) is { } link)
            {
                e.Handled = true;
                _ = OpenLinkAsync(link, text);
            }
        };

        AllowDrop = true;
        DragOver += (_, e) =>
        {
            if (e.Handled || DroppedLink(e.Data) is null) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Handled || DroppedLink(e.Data) is not { } dropped) return;
            e.Handled = true;
            _ = OpenLinkAsync(dropped.Link, dropped.Text);
        };

        // TEMPORARY, ADDED IN v1.28.0 - DELETE WHEN THE APP LEAVES BETA. Test crashes for the problem
        // report (OPEN-15): Ctrl+Shift+F12 throws on the UI thread, Ctrl+Shift+F11 off it, which ends
        // the app and offers the report on the next start.
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return;

            if (e.Key == Key.F12) throw new InvalidOperationException("TestCrashUiThread");
            if (e.Key == Key.F11) new Thread(() => throw new InvalidOperationException("TestCrashOffUiThread")).Start();
        };

        Loaded += (_, _) =>
        {
            HelpButtonToolTip();

            // The theme itself was applied at startup. This hooks up the two things that need a
            // window: repainting the chrome when the theme changes, and following Windows.
            AppTheme.Attach(this);

            // Installed when this launch came from clicking an update notification (§6), Browse
            // otherwise. Attached first, so a click landing from here on navigates by itself.
            AppNavigation.Attach(RootNavigationView);
            AppServices.Play.StartQuickPolling();
            AppServices.DownloadQueue.RestoreRemembered();
            AppTray.ShowIcon();
            RootNavigationView.Navigate(AppNavigation.StartOnInstalled ? typeof(InstalledPage) : typeof(BrowsePage));

            // Fire-and-forget: whether a newer build of this app exists on sp-mod.com has no
            // bearing on the window opening, and a failed check just leaves the banner down.
            _ = AppServices.AppUpdate.CheckOnStartupAsync();

            // Same arrangement, same reason: a server that is off, unreachable or simply not
            // configured just leaves the Server Map item out of the sidebar, so nothing here is
            // worth holding the window open for.
            _ = AppServices.ServerMap.ConnectOnStartupAsync();

            // Closes the game when the server stops, if that is ticked on the Play page.
            Services.GameCloser.Start();

            // Stops the server once the game has closed, if that is switched on (OPEN-45).
            Services.ServerStopper.Start();

            // Raises the crash bar when the server, the game or the headless crashes (OPEN-54 N30).
            Services.CrashWatcher.Start();

            // A server already up as the app opens - left running last time, perhaps with no window.
            _ = SayServerAlreadyRunningAsync();
        };

        //
        // Running in the tray (§8a): with the setting on, closing hides the window instead. The tray
        // icon is there throughout (OPEN-43).
        //
        Closing += (_, e) =>
        {
            if (!AppTray.HidesOnClose()) return;

            e.Cancel = true;
            AppTray.HideToTray(this);
        };

        //
        // Really closing (not to the tray) with this install's server still up: it keeps running
        // without the app, and with no window of its own nothing else would say so. The tray's Quit
        // asks before it shuts the app down (see AppTray.Quit), so this is the window's own close.
        //
        Closing += (_, e) =>
        {
            if (e.Cancel || AppTray.IsQuitting) return;
            if (!UnsavedConfigEdits.AllowExit() || !ServerExitCheck.AllowExit()) e.Cancel = true;
        };

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) AppTray.OnWindowShown();
            TellPlayWhetherAway();
        };

        StateChanged += (_, _) => TellPlayWhetherAway();

        // Constructs and shows the mod details dialog when requested.
        AppServices.ModDetailsOverlay.Requested += async (_, request) =>
            await new ModDetailsContentDialog(RootContentDialogPresenter, request).ShowAsync();

        AppServices.ModDetailsOverlay.AddonRequested += async (_, request) =>
            await new AddonDetailsContentDialog(RootContentDialogPresenter, request).ShowAsync();

        // Constructs and shows the mod update dialog, awaitable so callers know when it closes.
        AppServices.ModUpdateOverlay.ShowAsync = async (mod, addonCards) =>
        {
            var dialog = new ModUpdateContentDialog(RootContentDialogPresenter, mod, addonCards);
            await dialog.ShowAsync();
            return dialog.ViewModel.MadeChanges;
        };
    }

    private static string? ClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static (SpModLink Link, string Text)? DroppedLink(IDataObject data)
    {
        foreach (var format in new[] { "UniformResourceLocatorW", DataFormats.UnicodeText, DataFormats.Text })
        {
            if (!data.GetDataPresent(format)) continue;

            var text = data.GetData(format) switch
            {
                string s => s,
                System.IO.MemoryStream stream => System.Text.Encoding.Unicode.GetString(stream.ToArray()).TrimEnd('\0'),
                _ => null,
            };

            if (text is not null && SpModPageLink.Parse(text) is { } link) return (link, text.Trim());
        }

        return null;
    }

    private async Task OpenLinkAsync(SpModLink link, string text)
    {
        AppLog.Info("Links", $"opening a sp-mod {link.Kind} link: {link.Id}");

        if (link.Kind == SpModLinkKind.List)
        {
            AppNavigation.ImportListFromLink(text.Trim());
            return;
        }

        bool found;
        try
        {
            found = await AppServices.Browse.OpenFromLinkAsync(link.Kind == SpModLinkKind.Addon, link.Id);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or Core.SpModApi.SpModApiException)
        {
            AppLog.Warn("Links", $"couldn't open the linked {link.Kind} {link.Id}: {ex.Message}");
            found = false;
        }

        if (found) return;

        AskWindow.Show(new AskRequest
        {
            Title = Strings.Links_NotFoundTitle,
            Lead = Strings.Links_NotFoundLead,
            First = Strings.Common_Close,
            Cancel = null,
        });
    }

    private void TellPlayWhetherAway() =>
        AppServices.Play.SetWindowAway(!IsVisible || WindowState == WindowState.Minimized);

    private static async Task SayServerAlreadyRunningAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var server = await Task.Run(() => SptLaunchService.Describe(installPath, SptLaunchTarget.Server));
        if (!server.IsRunning) return;

        AppLog.Info("Launch", "the server was already running as the app opened");
        var hidden = new SettingsService().Load().HideServerWindow;

        switch (ServerStillRunningWindow.Ask(closing: false, server.ExePath ?? "", hidden))
        {
            case ServerRunningChoice.ShowPlay:
                AppNavigation.Navigate(typeof(ServerPage));
                break;

            case ServerRunningChoice.Stop:
                var result = await Task.Run(() => SptLaunchService.Stop(installPath, SptLaunchTarget.Server));
                AppLog.Info("Launch", $"opening: server stop {(result.Problem == SptLaunchProblem.None ? "done" : result.Problem.ToString())}");
                break;
        }
    }

    private void RootTitleBar_HelpClicked(TitleBar sender, RoutedEventArgs e) => ShowHelp();

    //
    // The "?" and F1. A ContentDialog (mod details, mod update) sits over the page inside this
    // window, so both still reach the window while it is up - and Help would open underneath it,
    // hidden. The dialog is closed first, the same as its Close button, and Help opens at the
    // section for the page it was over. The other dialogs are separate modal windows, which keep
    // this window from getting either.
    //
    private void ShowHelp()
    {
        if (RootContentDialogPresenter.Content is ContentDialog dialog) dialog.Hide(ContentDialogResult.None);

        AppNavigation.ShowHelpForCurrentPage();
    }

    private void HowToSetUp_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.ShowHelp(HelpCatalog.StartSectionId);

    //
    // The caption "?" is a template part with no tooltip of its own. Bound rather than set, so it
    // follows a language change like every {loc:Str}. Whether Windows shows it depends on the
    // caption hit-testing handing the mouse to WPF there; if it doesn't, nothing is lost.
    //
    private void HelpButtonToolTip()
    {
        if (RootTitleBar.Template?.FindName("PART_HelpButton", RootTitleBar) is not FrameworkElement button) return;

        button.SetBinding(ToolTipProperty, new Binding($"[{nameof(Strings.Help_TitleBarToolTip)}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        });
    }

    // The banner's action takes the user to the update page to read what changed and decide there,
    // rather than starting a download straight off a banner.
    private void AppUpdateBanner_Click(object sender, RoutedEventArgs e) =>
        RootNavigationView.Navigate(typeof(AppUpdatePage));
}
