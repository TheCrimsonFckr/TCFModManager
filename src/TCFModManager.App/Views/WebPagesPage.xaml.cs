using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class WebPagesPage : Page
{
    public WebPagesViewModel ViewModel => AppServices.WebPages;

    public WebPagesPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private async void WebPagesPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.IsOnPage = true;
        await AppNavigation.AfterTransitionAsync();
        if (!IsLoaded) return;

        await ViewModel.RescanAsync();
    }

    private void WebPagesPage_Unloaded(object sender, RoutedEventArgs e) => ViewModel.IsOnPage = false;
}
