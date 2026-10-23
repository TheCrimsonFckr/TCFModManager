using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
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

        Loaded += (_, _) =>
        {
            HelpButtonToolTip();

            // The theme itself was applied at startup. This hooks up the two things that need a
            // window: repainting the chrome when the theme changes, and following Windows.
            AppTheme.Attach(this);

            // Installed when this launch came from clicking an update notification (§6), Browse
            // otherwise. Attached first, so a click landing from here on navigates by itself.
            AppNavigation.Attach(RootNavigationView);
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

            // A server already up as the app opens - left running last time, perhaps with no window.
            _ = SayServerAlreadyRunningAsync();
        };

        //
        // Running in the tray (§8a): with the setting on, closing hides the window instead, and the
        // tray icon goes as soon as the window is back, however it came back.
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
            if (!ServerExitCheck.AllowExit()) e.Cancel = true;
        };

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) AppTray.OnWindowShown();
        };

        // Constructs and shows the mod details dialog when requested.
        AppServices.ModDetailsOverlay.Requested += async (_, request) =>
            await new ModDetailsContentDialog(RootContentDialogPresenter, request).ShowAsync();

        // Constructs and shows the mod update dialog, awaitable so callers know when it closes.
        AppServices.ModUpdateOverlay.ShowAsync = async (mod, addonCards) =>
        {
            var dialog = new ModUpdateContentDialog(RootContentDialogPresenter, mod, addonCards);
            await dialog.ShowAsync();
            return dialog.ViewModel.MadeChanges;
        };
    }

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
