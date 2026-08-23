using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class InstalledPage : Page
{
    public InstalledViewModel ViewModel { get; } = new();

    public InstalledPage()
    {
        DataContext = ViewModel;
        InitializeComponent();
    }

    private async void InstalledPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.UpdateLayoutForWidth(ResultsListBox.ActualWidth);
        await ViewModel.ScanCommand.ExecuteAsync(null);
    }

    // Opens the details/update dialog for the clicked card, then clears selection.
    private async void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is not InstalledModCardViewModel mod) return;

        await ViewModel.ShowDetailsCommand.ExecuteAsync(mod);
        listBox.SelectedItem = null;
    }

    private void ResultsListBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.UpdateLayoutForWidth(e.NewSize.Width);
    }

    private void ModGroups_Click(object sender, RoutedEventArgs e) =>
        new ModGroupsWindow { Owner = Window.GetWindow(this) }.Show();
}
