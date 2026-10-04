using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class FootprintPage : Page
{
    public FootprintViewModel ViewModel { get; } = new();

    public FootprintPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private bool _visited;

    //
    // Refreshes on every visit rather than once, so a mod installed since the last visit appears -
    // off the cache, so the usual cost is a directory walk rather than re-reading every assembly.
    // A return visit waits for the page transition to finish first, so the refresh doesn't stall it.
    //
    private async void FootprintPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_visited)
        {
            await AppNavigation.AfterTransitionAsync();
            if (!IsLoaded) return;
        }

        _visited = true;
        await ViewModel.RefreshAsync();
    }
}
