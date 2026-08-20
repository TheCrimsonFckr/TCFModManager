using System.Windows;
using System.Windows.Controls;
using TCFModManagement.App.ViewModels;

namespace TCFModManagement.App.Views;

public partial class DependenciesPage : Page
{
    public DependenciesViewModel ViewModel { get; }

    public DependenciesPage()
    {
        ViewModel = new DependenciesViewModel();
        DataContext = ViewModel;
        InitializeComponent();
    }

    // Resolves on first open only; re-navigating reuses what's already there, and Refresh re-runs it.
    private async void DependenciesPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasLoaded || ViewModel.IsBusy) return;

        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }
}
