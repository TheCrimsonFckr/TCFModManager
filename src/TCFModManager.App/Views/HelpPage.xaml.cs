using System.Windows.Controls;
using System.Windows.Input;
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
    }

    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SectionsScrollViewer.ScrollToVerticalOffset(SectionsScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
