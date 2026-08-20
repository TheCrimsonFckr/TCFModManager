using TCFModManager.App.Views;
using Wpf.Ui.Controls;

namespace TCFModManager.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RootNavigationView.Navigate(typeof(BrowsePage));

        // Constructs and shows the mod details dialog when requested.
        AppServices.ModDetailsOverlay.Requested += async (_, mod) =>
            await new ModDetailsContentDialog(RootContentDialogPresenter, mod).ShowAsync();

        // Constructs and shows the mod update dialog, awaitable so callers know when it closes.
        AppServices.ModUpdateOverlay.ShowAsync = async mod =>
            await new ModUpdateContentDialog(RootContentDialogPresenter, mod).ShowAsync();
    }
}
