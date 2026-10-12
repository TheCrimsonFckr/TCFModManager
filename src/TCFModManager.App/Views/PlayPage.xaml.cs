using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class PlayPage : Page
{
    private const double MinServerLogHeight = 300;

    private ScrollViewer? _pageScroll;

    public PlayViewModel ViewModel { get; } = new();

    public PlayPage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        // NavigationCacheMode keeps this page alive for the app's lifetime, so the poll has to be
        // stopped when it goes off screen rather than left running behind every other page.
        Loaded += (_, _) =>
        {
            ViewModel.StartPolling();

            _pageScroll ??= FindPageScroll();
            LayoutUpdated += FitServerLog;

            //
            // Fire-and-forget, and deliberately not part of the poll: the check reads the whole
            // install to compare it against the server's list, which is far too much to do every two
            // seconds beside a launch button. Opening the page is the moment it needs to be current,
            // and "Check again" covers the rest.
            //
            _ = AppServices.PreLaunchCheck.CheckAsync();
            _ = ViewModel.RefreshConflictsAsync();
        };

        Unloaded += (_, _) =>
        {
            ViewModel.StopPolling();
            LayoutUpdated -= FitServerLog;
        };
    }

    //
    // The server log runs from wherever it sits down to the bottom of the window, with the folder
    // button and the card's edge still on screen. Measured rather than a fixed allowance, because
    // the cards above it come and go (warnings, the pre-launch check) and the window is resizable.
    // Measured in content coordinates, so scrolling the page does not change it. Never under
    // MinServerLogHeight; when that happens the page scrolls as before.
    //
    private void FitServerLog(object? sender, EventArgs e)
    {
        if (_pageScroll is null || !ServerLog.IsVisible || _pageScroll.ViewportHeight <= 0) return;

        var top = ServerLog.TranslatePoint(new Point(0, 0), _pageScroll).Y + _pageScroll.VerticalOffset;
        var below = ServerCard.TranslatePoint(new Point(0, ServerCard.ActualHeight), ServerLog).Y - ServerLog.ActualHeight;
        var height = Math.Max(MinServerLogHeight, _pageScroll.ViewportHeight - top - below - Root.Margin.Bottom);

        if (Math.Abs(height - ServerLog.Height) > 1)
            ServerLog.Height = height;
    }

    private ScrollViewer? FindPageScroll()
    {
        for (DependencyObject? at = VisualTreeHelper.GetParent(this); at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is ScrollViewer scroll) return scroll;
        }
        return null;
    }

    // The Play card's "..." - the things done now and then, kept off the card.
    private void DirectMore_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = PlacementMode.Bottom,
            DataContext = vm,
        };

        menu.Items.Add(new MenuItem
        {
            Header = vm.WipeNextStart ? Strings.Play_DirectWipeCancel : Strings.Play_DirectWipe,
            ToolTip = Strings.Play_DirectWipeToolTip,
            Command = vm.ToggleWipeCommand,
            IsEnabled = vm.SelectedProfile is not null && !vm.IsPlaying,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectNewProfile,
            ToolTip = Strings.Play_DirectNewProfileToolTip,
            Command = vm.NewProfileCommand,
            IsEnabled = !vm.IsPlaying,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectDeleteProfile,
            ToolTip = Strings.Play_DirectDeleteProfileToolTip,
            Command = vm.AskDeleteProfileCommand,
            IsEnabled = vm.SelectedProfile is not null && !vm.IsPlaying && !vm.IsGameRunning,
        });

        menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectClearCache,
            ToolTip = Strings.Play_DirectClearCacheToolTip,
            Command = vm.ClearGameCacheNowCommand,
            IsEnabled = !vm.IsPlaying && !vm.IsGameRunning,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectOpenLauncher,
            ToolTip = Strings.Play_DirectOpenLauncherToolTip,
            Command = vm.StartClientCommand,
            IsEnabled = vm.CanStartClient,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectSettings,
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => AppNavigation.Navigate(typeof(OptionsPage))),
        });

        menu.IsOpen = true;
    }

    private void RoleShowMeHow_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.ShowHelp("options", "options.role");
}
