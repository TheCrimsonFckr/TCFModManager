using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class ModListsPage : Page
{
    public ModListsViewModel ViewModel { get; } = new();

    public ModListsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;

        //
        // Registered on the Page with handledEventsToo rather than as a bubbling MouseWheel
        // attribute in XAML: ui:TextBox marks the bubbling wheel event handled even with nothing to
        // scroll, so hovering the name box would otherwise swallow it. Same fix as ConfigsPage.
        //
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(Page_PreviewMouseWheel), handledEventsToo: true);
    }

    private void ModListsPage_Loaded(object sender, RoutedEventArgs e) => ViewModel.Refresh();

    //
    // Sends the wheel to whichever list the pointer is nearest, so scrolling works anywhere on the
    // page rather than only directly over a list.
    //
    // The right-hand column holds two lists in one cell - the selected list's contents and, after a
    // preview, the plan - so which one is on screen decides where the wheel goes.
    //
    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        ListBox? right = PlanBox.IsVisible ? PlanBox : EntriesBox.IsVisible ? EntriesBox : null;

        var target = right is not null && (right.IsMouseOver || !ListsBox.IsMouseOver)
            ? Scroller(right)
            : Scroller(ListsBox);

        if (target is null) return;

        target.ScrollToVerticalOffset(target.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? Scroller(DependencyObject root)
    {
        if (root is ScrollViewer found) return found;

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (Scroller(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        }

        return null;
    }
}
