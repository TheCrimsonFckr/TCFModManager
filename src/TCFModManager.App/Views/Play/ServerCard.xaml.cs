using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TCFModManager.App.Views.Play;

// The Server page's server card: Start/Restart/Stop and the server log. DataContext is the page's
// PlayViewModel.
public partial class ServerCard : UserControl
{
    private const double MinServerLogHeight = 300;

    private ScrollViewer? _pageScroll;

    public static readonly DependencyProperty FillsWindowProperty = DependencyProperty.Register(
        nameof(FillsWindow), typeof(bool), typeof(ServerCard), new PropertyMetadata(true));

    // True: the log reaches down to the bottom of the window. False: it stays MinServerLogHeight tall.
    public bool FillsWindow
    {
        get => (bool)GetValue(FillsWindowProperty);
        set => SetValue(FillsWindowProperty, value);
    }

    public ServerCard()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            _pageScroll = FindPageScroll();
            LayoutUpdated -= FitServerLog;
            LayoutUpdated += FitServerLog;
        };

        Unloaded += (_, _) => LayoutUpdated -= FitServerLog;
    }

    //
    // The server log runs from wherever it sits down to the bottom of the window, with the folder
    // button and the card's edge still on screen. Measured rather than a fixed allowance, because
    // the window is resizable.
    // Measured in content coordinates, so scrolling the page does not change it. Never under
    // MinServerLogHeight unless the window is shorter than that; then the page scrolls as before.
    //
    private void FitServerLog(object? sender, EventArgs e)
    {
        if (!FillsWindow)
        {
            if (Math.Abs(ServerLog.Height - MinServerLogHeight) > 1) ServerLog.Height = MinServerLogHeight;
            return;
        }

        if (_pageScroll is null || !_pageScroll.IsAncestorOf(ServerLog)) _pageScroll = FindPageScroll();
        if (_pageScroll is null || !ServerLog.IsVisible || _pageScroll.ViewportHeight <= 0) return;

        var bottomMargin = Card.Margin.Bottom + ((Parent as FrameworkElement)?.Margin.Bottom ?? 0);
        var top = ServerLog.TranslatePoint(new Point(0, 0), _pageScroll).Y + _pageScroll.VerticalOffset;
        var below = Card.TranslatePoint(new Point(0, Card.ActualHeight), ServerLog).Y - ServerLog.ActualHeight;
        var fit = _pageScroll.ViewportHeight - top - below - bottomMargin;
        var most = _pageScroll.ViewportHeight - below - bottomMargin;
        var height = Math.Min(most, Math.Max(MinServerLogHeight, fit));

        if (height > 0 && Math.Abs(height - ServerLog.Height) > 1)
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
}
