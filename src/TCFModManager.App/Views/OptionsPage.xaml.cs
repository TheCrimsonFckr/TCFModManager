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

    private void DataFiles_Click(object sender, RoutedEventArgs e) =>
        new DataFilesWindow { Owner = Window.GetWindow(this) }.Show();
}
