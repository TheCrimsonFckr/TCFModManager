using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

// OPEN-12 A4: an author's page - see AuthorViewModel.
public partial class AuthorPage : Page
{
    public AuthorViewModel ViewModel { get; } = new();

    public AuthorPage()
    {
        InitializeComponent();
        DataContext = ViewModel;

        // A request while this page is already on screen (another author's name clicked from here).
        AppNavigation.AuthorRequested += async (_, _) =>
        {
            if (IsLoaded) await LoadPendingAsync();
        };
    }

    private async void AuthorPage_Loaded(object sender, RoutedEventArgs e) => await LoadPendingAsync();

    private async Task LoadPendingAsync()
    {
        if (AppNavigation.TakeAuthor() is not { } request) return;

        PageScroll.ScrollToTop();
        await ViewModel.LoadAsync(request.Id, request.Name);
    }

    private void ModRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AuthorModRow row) ViewModel.OpenModCommand.Execute(row);
    }

    private void AddonRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AuthorAddonRow row) ViewModel.OpenAddonCommand.Execute(row);
    }
}
