using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class ServerPage : Page
{
    public PlayViewModel ViewModel { get; } = AppServices.Play;

    public ServerPage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        // NavigationCacheMode keeps this page alive for the app's lifetime, so the poll has to be
        // stopped when it goes off screen rather than left running behind every other page.
        Loaded += (_, _) => ViewModel.StartPolling();
        Unloaded += (_, _) => ViewModel.StopPolling();
    }
}
