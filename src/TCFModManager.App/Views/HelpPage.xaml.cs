using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class HelpPage : Page
{
    public HelpViewModel ViewModel { get; }

    public HelpPage()
    {
        ViewModel = new HelpViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        // Wheel scrolls the how-tos from anywhere on the page, the search box included - the same
        // tunnelling handler as InstalledPage and DependenciesPage, for the same reason.
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(Page_PreviewMouseWheel), true);

        // Navigating to this page again while it is on screen doesn't reload it, so a request made
        // from here - the no-install banner - arrives by the event instead.
        Loaded += (_, _) => TakeRequest();

        // The page is cached, so without this the next visit opens on whatever was left open.
        Unloaded += (_, _) => ViewModel.CollapseAll();
        AppNavigation.HelpRequested += (_, _) =>
        {
            if (IsLoaded) TakeRequest();
        };
    }

    private void TakeRequest()
    {
        if (AppNavigation.TakeHelpSection() is not { } sectionId) return;

        var section = ViewModel.Arrive(sectionId);

        // After the expanders have opened and the list has laid itself out again, or the offset
        // is measured against the old, collapsed heights.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (SectionsList.ItemContainerGenerator.ContainerFromItem(section) is not FrameworkElement container)
                return;

            var top = container.TranslatePoint(new Point(0, 0), SectionsList).Y;
            SectionsScrollViewer.ScrollToVerticalOffset(top);
        });
    }

    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SectionsScrollViewer.ScrollToVerticalOffset(SectionsScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
