using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TCFModManager.App.ViewModels;

using TCFModManager.Core.Services;

namespace TCFModManager.App.Views;

public partial class BrowsePage : Page
{
    // Shared app-lifetime ViewModel so results persist across navigations.
    public BrowseViewModel ViewModel { get; } = AppServices.Browse;

    public BrowsePage()
    {
        DataContext = ViewModel;
        InitializeComponent();
    }

    private async void BrowsePage_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Debug("Browse", "Loaded: start");

        // Run the initial search once per app session; loads default results.
        if (!ViewModel.HasLoadedResults)
        {
            AppLog.Debug("Browse", "Loaded: about to await SearchCommand.ExecuteAsync");
            await ViewModel.SearchCommand.ExecuteAsync(null);
            AppLog.Debug("Browse", "Loaded: SearchCommand.ExecuteAsync await resumed");
        }

        // Re-sync the column count against the current width.
        ViewModel.UpdateLayoutForWidth(ResultsListBox.ActualWidth);

        AppLog.Debug("Browse", "Loaded: handler returning (WPF layout/render still pending)");

        // Logs once WPF has finished pending layout/render work.
        _ = Dispatcher.BeginInvoke(
            new Action(() => AppLog.Debug("Browse", "Loaded: WPF caught up on layout/render")),
            DispatcherPriority.ContextIdle);
    }

    private async void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        if (listBox.SelectedItem is not ModCardViewModel card)
        {
            // Ignores the re-entrant SelectionChanged caused by clearing SelectedItem below.
            return;
        }

        // Loads and shows the selected mod's details overlay.
        await ViewModel.LoadDetailsAsync(card.Mod);

        // Clears the selection so clicking the same card again reopens the overlay.
        listBox.SelectedItem = null;
    }

    private void ResultsListBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.UpdateLayoutForWidth(e.NewSize.Width);
    }
}
