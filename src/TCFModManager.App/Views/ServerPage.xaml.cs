using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class ServerPage : Page
{
    private const double MinServerLogHeight = 300;

    private ScrollViewer? _pageScroll;

    public PlayViewModel ViewModel { get; } = AppServices.Play;

    public ServerPage()
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
    // the window is resizable.
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

    private void RoleShowMeHow_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.ShowHelp("options", "options.role");
}
