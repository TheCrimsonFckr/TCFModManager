using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class InstalledPage : Page
{
    public InstalledViewModel ViewModel { get; } = new();

    // Drag/click state for the manual gesture on a group-view mod row (see ModRow_PreviewMouseMove
    // and ModRow_PreviewMouseLeftButtonUp) - WPF has no built-in "drag this ItemsControl row"
    // support, so this hand-rolls the standard press/move-past-a-threshold/release pattern that
    // lets the same row both open on a plain click and drag-to-another-group on a real drag. Only
    // used in group view; the flat grid's ListBox has its own click handling (SelectionChanged)
    // and no drag behavior.
    private Point _dragStart;
    private InstalledModCardViewModel? _dragCandidate;
    private bool _dragStarted;

    public InstalledPage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        // Registered directly on the Page (not via a XAML attribute on a specific element) so it's
        // the very first thing to see every wheel event over this page - PreviewMouseWheel tunnels
        // root-to-leaf, so this fires before any descendant's own bubble-phase handling, including
        // the internal ScrollViewer part several controls (ui:TextBox, ComboBox, ...) use for their
        // own content, which otherwise swallows the wheel event and marks it handled even when
        // there's nothing for that control itself to scroll. handledEventsToo:true on top of that
        // means it still runs even if something upstream in the tunnel already marked the event
        // handled. See Page_PreviewMouseWheel below.
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(Page_PreviewMouseWheel), true);
    }

    private async void InstalledPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.UpdateLayoutForWidth(ResultsListBox.ActualWidth);
        await ViewModel.ScanCommand.ExecuteAsync(null);
    }

    // Opens the details/update dialog for the clicked card, then clears selection. In multi-select
    // mode the same click ticks the card instead, so the whole card stays the hit target rather
    // than only the small checkbox.
    private async void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is not InstalledModCardViewModel mod) return;

        listBox.SelectedItem = null;

        if (ViewModel.SelectionMode)
        {
            mod.IsSelected = !mod.IsSelected;
            return;
        }

        await ViewModel.ShowDetailsCommand.ExecuteAsync(mod);
    }

    private void ResultsListBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.UpdateLayoutForWidth(e.NewSize.Width);
    }

    private void CardsView_Click(object sender, RoutedEventArgs e) => ViewModel.ViewMode = InstalledViewMode.Cards;

    private void GroupsView_Click(object sender, RoutedEventArgs e) => ViewModel.ViewMode = InstalledViewMode.Groups;

    private void ListView_Click(object sender, RoutedEventArgs e) => ViewModel.ViewMode = InstalledViewMode.List;

    private void SelectMode_Click(object sender, RoutedEventArgs e) => ViewModel.SelectionMode = !ViewModel.SelectionMode;

    // Lets the wheel scroll whichever of the two scrolling views is showing from anywhere on the
    // page - search box, filter row, group-management bar, directly over the list, all of it - by
    // driving that view's ScrollViewer ourselves unconditionally rather than only stepping in when
    // some other handler didn't already consume the event. Since this runs in the tunnel phase
    // before the ScrollViewer's own native wheel handling would otherwise fire, hovering directly
    // over the list also comes through here (and gets marked handled before the native handling
    // runs) - that's fine, we do the exact same scroll it would have, so there's no visible
    // difference and no double-scroll. Cards view paginates and is left to its ListBox.
    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scroller = ViewModel.ViewMode switch
        {
            InstalledViewMode.Groups => GroupsScrollViewer,
            InstalledViewMode.List => ListScrollViewer,
            _ => null,
        };

        if (scroller is null) return;

        scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    // True when the click landed on (or inside) a button within the row - the row's own gesture
    // steps aside for those, since PreviewMouseLeftButtonDown tunnels through the row Border before
    // reaching the button and would otherwise mark the event handled before the button ever saw it.
    private static bool IsInsideButton(object? originalSource)
    {
        for (var node = originalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase) return true;
        }

        return false;
    }

    private void ModRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource))
        {
            _dragCandidate = null;
            _dragStarted = false;
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as InstalledModCardViewModel;
        _dragStarted = false;

        // Without this, the event goes on to bubble as a plain MouseDown, and WPF's default
        // click-to-focus behavior walks up from this (non-focusable) Border to the nearest
        // focusable ancestor - normally the group ScrollViewer or one of its ItemsControls -
        // and focuses it, which was producing a small scroll jump the instant a row was
        // clicked. We're fully hand-rolling this row's click/drag gesture already (see
        // ModRow_PreviewMouseMove/Up below), so nothing downstream needs that default focus
        // assignment. Paired with Focusable="False" on the ScrollViewer/ItemsControls in XAML.
        e.Handled = true;
    }

    private void ModRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null || _dragStarted) return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // Marks this gesture as a drag rather than clearing _dragCandidate outright, so the
        // ButtonUp handler below can tell "this mouse-down turned into a drag" (skip opening
        // details) apart from "this mouse-down never moved" (a plain click).
        _dragStarted = true;
        if (sender is DependencyObject source) DragDrop.DoDragDrop(source, _dragCandidate, DragDropEffects.Move);
    }

    // Opens the update dialog for a group-view row that was clicked rather than dragged - the same
    // dialog ResultsListBox_SelectionChanged opens for a flat-grid card. DoDragDrop below runs its
    // own modal loop and swallows the mouse-up that ends a real drag, so in practice this only ever
    // fires for a genuine click; the _dragStarted check is the belt-and-braces guard against it.
    private async void ModRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var mod = _dragCandidate;
        var wasDrag = _dragStarted;
        _dragCandidate = null;
        _dragStarted = false;

        if (wasDrag || mod is null) return;
        if ((sender as FrameworkElement)?.DataContext as InstalledModCardViewModel != mod) return;

        await ViewModel.ShowDetailsCommand.ExecuteAsync(mod);
    }

    private void Section_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(InstalledModCardViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Section_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModGroupSectionViewModel section }) return;
        if (e.Data.GetData(typeof(InstalledModCardViewModel)) is not InstalledModCardViewModel mod) return;

        ViewModel.MoveModToGroup(mod, section.GroupId);
        e.Handled = true;
    }

    private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModGroupSectionViewModel section }) return;

        if (e.Key == Key.Enter)
        {
            ViewModel.CommitRenameCommand.Execute(section);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelRenameCommand.Execute(section);
            e.Handled = true;
        }
    }

    // Also commits on losing focus by any other means (clicking elsewhere, tabbing away) - but only
    // while still actually editing, so the extra commit the Enter path's own visibility change can
    // trigger (the TextBox collapses and loses focus as a side effect) is skipped rather than firing
    // a harmless-but-redundant second save.
    private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModGroupSectionViewModel { IsEditing: true } section }) return;
        ViewModel.CommitRenameCommand.Execute(section);
    }
}
