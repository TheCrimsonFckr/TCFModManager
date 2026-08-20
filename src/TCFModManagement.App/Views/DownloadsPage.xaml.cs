using System.Windows;
using System.Windows.Controls;
using TCFModManagement.App.ViewModels;

namespace TCFModManagement.App.Views;

// Shows the shared download queue, one card per queued item.
public partial class DownloadsPage : Page
{
    public DownloadQueueViewModel ViewModel { get; } = AppServices.DownloadQueue;

    public DownloadsPage()
    {
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void ClearFinishedButton_Click(object sender, RoutedEventArgs e) => ViewModel.ClearFinished();
}
