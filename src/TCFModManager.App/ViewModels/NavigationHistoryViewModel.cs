using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TCFModManager.App.ViewModels;

// OPEN-56 N18: the Back and Forward items at the top of the sidebar.
public sealed partial class NavigationHistoryViewModel : ObservableObject
{
    public NavigationHistoryViewModel()
    {
        AppNavigation.HistoryChanged += (_, _) =>
        {
            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
        };
    }

    public bool CanGoBack => AppNavigation.CanGoBack;

    public bool CanGoForward => AppNavigation.CanGoForward;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private static void GoBack() => AppNavigation.GoBack();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private static void GoForward() => AppNavigation.GoForward();
}
