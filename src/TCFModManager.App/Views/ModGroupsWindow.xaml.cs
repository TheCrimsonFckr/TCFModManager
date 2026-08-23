using System.Windows;
using System.Windows.Input;
using TCFModManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class ModGroupsWindow : FluentWindow
{
    public ModGroupsViewModel ViewModel { get; } = new();

    // Drag state for the manual drag-source gesture on a mod row (see ModRow_PreviewMouseMove) -
    // WPF has no built-in "drag this ItemsControl row" support, so this hand-rolls the standard
    // press-then-move-past-a-threshold pattern.
    private Point _dragStart;
    private InstalledModCardViewModel? _dragCandidate;

    public ModGroupsWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private void ModRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as InstalledModCardViewModel;
    }

    private void ModRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null) return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var mod = _dragCandidate;
        _dragCandidate = null; // one drag per mouse-down
        if (sender is DependencyObject source) DragDrop.DoDragDrop(source, mod, DragDropEffects.Move);
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
