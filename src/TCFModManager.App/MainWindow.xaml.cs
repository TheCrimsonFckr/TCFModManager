using System.Windows;

using TCFModManager.App.Views;
using Wpf.Ui.Controls;

namespace TCFModManager.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // The theme itself was applied at startup. This hooks up the two things that need a
            // window: repainting the chrome when the theme changes, and following Windows.
            AppTheme.Attach(this);

            RootNavigationView.Navigate(typeof(BrowsePage));

            // Fire-and-forget: whether a newer build of this app exists on sp-mod.com has no
            // bearing on the window opening, and a failed check just leaves the banner down.
            _ = AppServices.AppUpdate.CheckOnStartupAsync();
        };

        // Constructs and shows the mod details dialog when requested.
        AppServices.ModDetailsOverlay.Requested += async (_, mod) =>
            await new ModDetailsContentDialog(RootContentDialogPresenter, mod).ShowAsync();

        // Constructs and shows the mod update dialog, awaitable so callers know when it closes.
        AppServices.ModUpdateOverlay.ShowAsync = async mod =>
            await new ModUpdateContentDialog(RootContentDialogPresenter, mod).ShowAsync();
    }

    // The banner's action takes the user to the update page to read what changed and decide there,
    // rather than starting a download straight off a banner.
    private void AppUpdateBanner_Click(object sender, RoutedEventArgs e) =>
        RootNavigationView.Navigate(typeof(AppUpdatePage));
}
