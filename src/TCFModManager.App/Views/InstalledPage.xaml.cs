using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    }

    private async void InstalledPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.UpdateLayoutForWidth(ResultsListBox.ActualWidth);
        await ViewModel.ScanCommand.ExecuteAsync(null);
    }

    // Opens the details/update dialog for the clicked card, then clears selection. Only wired to
    // the flat grid's ListBox - group view's mod rows are plain ItemsControl items (organize-only,
    // no click-to-open) so a drag gesture never has to fight a click-driven selection change.
    private async void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is not InstalledModCardViewModel mod) return;

        await ViewModel.ShowDetailsCommand.ExecuteAsync(mod);
        listBox.SelectedItem = null;
    }

    private void ResultsListBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.UpdateLayoutForWidth(e.NewSize.Width);
    }

    private void CardsView_Click(object sender, RoutedEventArgs e) => ViewModel.GroupViewEnabled = false;

    private void GroupsView_Click(object sender, RoutedEventArgs e) => ViewModel.GroupViewEnabled = true;

    private void ModRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as InstalledModCardViewModel;
        _dragStarted = false;
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
