using TCFModManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class DataFilesWindow : FluentWindow
{
    public DataFilesViewModel ViewModel { get; } = new();

    public DataFilesWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
    }
}
