using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class ModUpdateContentDialog : ContentDialog
{
    // Ensures WPF-UI's ContentDialog style applies to this subclass.
    static ModUpdateContentDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ModUpdateContentDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog)));
    }

    public ModUpdateDialogViewModel ViewModel { get; }

    //
    // Selects the version whose header was clicked. Deliberately NOT marked handled, so the
    // expander still opens and closes as before - the click now does both.
    //
    // Before this, a CardExpander header swallowed the click on its way to the ListBoxItem, so the
    // only way to select a version was to click inside its expanded release notes.
    //
    private void VersionHeader_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } item) item.IsSelected = true;
    }

    //
    // Hybrid visual/logical climb. A click on the "Published:" line reports the Run as
    // OriginalSource, and Run is a ContentElement with no visual parent - VisualTreeHelper.GetParent
    // throws on it. Third time this has bitten in this codebase.
    //
    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;

            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    // Uses WPF-UI's legacy ContentPresenter-based ContentDialog constructor.
#pragma warning disable CS0618 // Type or member is obsolete
    public ModUpdateContentDialog(ContentPresenter host, InstalledModCardViewModel mod) : base(host)
    {
        ViewModel = new ModUpdateDialogViewModel(mod);
        InitializeComponent();
        DataContext = ViewModel;
        Title = mod.DisplayTitle;

        // Fire-and-forget load; the XAML's loading state covers it until it completes.
        _ = ViewModel.LoadAsync();
    }
#pragma warning restore CS0618

    // Forces a fixed dialog size after the base ContentDialog auto-sizing pass.
    protected override Size MeasureOverride(Size availableSize)
    {
        Size result = base.MeasureOverride(availableSize);

        SetCurrentValue(DialogWidthProperty, 700.0);
        SetCurrentValue(DialogHeightProperty, 660.0);

        return result;
    }
}
