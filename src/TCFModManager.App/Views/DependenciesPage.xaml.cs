using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

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

    // Lets the wheel scroll the dependency tree from anywhere on the page, not just while
    // hovering it directly - same fix as InstalledPage's group view. MouseWheel bubbles from
    // wherever the pointer actually is up through this root Grid; when the pointer is already
    // over TreesScrollViewer it handles the wheel event itself first and marks it handled, so
    // this handler is simply skipped for those (no double-scrolling).
    private void RootGrid_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TreesScrollViewer.Visibility != Visibility.Visible) return;

        TreesScrollViewer.ScrollToVerticalOffset(TreesScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
