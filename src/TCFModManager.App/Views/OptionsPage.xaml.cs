using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class OptionsPage : Page
{
    public OptionsViewModel ViewModel { get; } = new();

    public OptionsPage()
    {
        DataContext = ViewModel;
        InitializeComponent();
    }

    // Same reason as ServerMapPage: the key file can appear or change while this app is open, and
    // the section that shows it is right here.
    // The held size too, since a removal or Undo elsewhere changes it.
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AppServices.ServerMap.RefreshLocalKey();
        ViewModel.RefreshRemovedModsSize();
    }

    private void DataFiles_Click(object sender, RoutedEventArgs e) =>
        new DataFilesWindow { Owner = Window.GetWindow(this) }.Show();

    private void Credits_Click(object sender, RoutedEventArgs e) => CreditsWindow.Open(Window.GetWindow(this));
}
