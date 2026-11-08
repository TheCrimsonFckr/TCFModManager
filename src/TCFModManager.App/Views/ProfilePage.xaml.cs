using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class ProfilePage : Page
{
    public PlayViewModel ViewModel { get; } = AppServices.Play;

    public ProfilePage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            ViewModel.StartPolling();

            //
            // Fire-and-forget, and deliberately not part of the poll: the check reads the whole
            // install to compare it against the server's list, which is far too much to do every two
            // seconds beside a launch button. Opening the page is the moment it needs to be current,
            // and "Check again" covers the rest.
            //
            _ = AppServices.PreLaunchCheck.CheckAsync();
            _ = ViewModel.RefreshConflictsAsync();
        };

        Unloaded += (_, _) => ViewModel.StopPolling();

        AppServices.ServerPageGate.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServerPageGateViewModel.IsServerOnProfile)) Arrange();
        };
        Arrange();
    }

    // OPEN-42 Q3: with the server cards here, the order they are used in.
    private void Arrange()
    {
        UIElement[] cards = AppServices.ServerPageGate.IsServerOnProfile
            ? [Warnings, BeforeYouJoin, Server, DirectPlay, Launcher, Headless, Stats, Tools, Message]
            : [Warnings, BeforeYouJoin, DirectPlay, Stats, Launcher, Message];

        Root.Children.Clear();
        foreach (var card in cards) Root.Children.Add(card);
    }
}
