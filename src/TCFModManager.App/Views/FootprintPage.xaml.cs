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

    //
    // Refreshes on every visit rather than once, so a mod installed since the last visit appears -
    // off the cache, so the usual cost is a directory walk rather than re-reading every assembly.
    //
    private async void FootprintPage_Loaded(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshAsync();
}
