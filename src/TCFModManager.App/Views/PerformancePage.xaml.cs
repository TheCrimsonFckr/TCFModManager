using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class PerformancePage : Page
{
    public PerformanceViewModel ViewModel { get; } = new();

    public PerformancePage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    //
    // Refreshes on every visit rather than once, so a mod installed since the last visit appears -
    // off the cache, so the usual cost is a directory walk rather than re-reading every assembly.
    //
    private async void PerformancePage_Loaded(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshAsync();
}
