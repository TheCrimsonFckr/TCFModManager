using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        //
        // Lets the wheel scroll the version list from anywhere in the dialog rather than only over
        // the scrollbar. Several controls in here mark a bubbling MouseWheel handled whether or not
        // they have anything to scroll - the ListBox's own template ScrollViewer, the manual-version
        // ui:TextBox, and each release-note RichTextBox - so a plain MouseWheel handler never sees
        // most of the dialog. A tunnelling PreviewMouseWheel registered with handledEventsToo:true
        // runs before all of them and regardless of what they do. Same fix as InstalledPage's group
        // view and DependenciesPage's tree.
        //
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(Dialog_PreviewMouseWheel), true);

        // Fire-and-forget load; the XAML's loading state covers it until it completes.
        _ = ViewModel.LoadAsync();
    }
#pragma warning restore CS0618

    // The dialog template's own scroller - see OnApplyTemplate.
    private ScrollViewer? _contentScroll;

    //
    // Resolves the scroller that actually moves. ContentDialog's template wraps the whole of this
    // dialog's content in a PassiveScrollViewer named PART_ContentScroll and measures the content
    // with unbounded height, so any ScrollViewer *inside* the content - including the one around
    // the version list - is given all the room it asks for and has nothing left to scroll. The
    // visible scrollbar down the dialog's right edge is this template part, and it is the only
    // thing worth driving.
    //
    // Looked up by name rather than by walking the tree: it is a documented template part, and a
    // null here means the template changed, which should stop the wheel doing anything rather than
    // quietly scroll some other element.
    //
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _contentScroll = GetTemplateChild("PART_ContentScroll") as ScrollViewer;
    }

    //
    // Only takes the wheel when there is actually somewhere to scroll. Marking the event handled
    // when there isn't would swallow it for whatever else might have wanted it, which is how the
    // first attempt at this made the dialog worse rather than better.
    //
    private void Dialog_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_contentScroll is not { } scroll || scroll.ScrollableHeight <= 0) return;

        scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    // Forces a fixed dialog size after the base ContentDialog auto-sizing pass.
    protected override Size MeasureOverride(Size availableSize)
    {
        Size result = base.MeasureOverride(availableSize);

        SetCurrentValue(DialogWidthProperty, 700.0);
        SetCurrentValue(DialogHeightProperty, 660.0);

        return result;
    }
}
