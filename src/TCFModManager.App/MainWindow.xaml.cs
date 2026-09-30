using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using TCFModManager.App.Help;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using Wpf.Ui.Controls;

namespace TCFModManager.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();

        // Before the window is shown, not on Loaded: WindowStartupLocation reads Width/Height while
        // it decides where to put the window, and by Loaded it has already decided. This also wires
        // F11/Escape and records the window's position on close - see WindowLayout.
        WindowLayout.Attach(this, RootTitleBar);

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

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) AppTray.OnWindowShown();
        };

        // Constructs and shows the mod details dialog when requested.
        AppServices.ModDetailsOverlay.Requested += async (_, request) =>
            await new ModDetailsContentDialog(RootContentDialogPresenter, request).ShowAsync();

        // Constructs and shows the mod update dialog, awaitable so callers know when it closes.
        AppServices.ModUpdateOverlay.ShowAsync = async mod =>
        {
            var dialog = new ModUpdateContentDialog(RootContentDialogPresenter, mod);
            await dialog.ShowAsync();
            return dialog.ViewModel.MadeChanges;
        };
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
