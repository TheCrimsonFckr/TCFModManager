using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.Behaviors;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public sealed record CardLayoutPick(Guid? Id);

public partial class CardLayoutEditorWindow : FluentWindow
{
    private const string DragFormat = "TCFModManager.CardFieldRow";

    private readonly CardLayoutEditorViewModel _viewModel;
    private CardLayoutPick? _picked;
    private Point _dragStart;
    private CardFieldRow? _dragCandidate;

    private CardLayoutEditorWindow(CardLayoutPage page, Guid? current, object? sample)
    {
        _viewModel = new CardLayoutEditorViewModel(page, current, sample);
        DataContext = _viewModel;

        InitializeComponent();
        DialogFit.Attach(this);

        PreviewCard.ContentTemplate = (DataTemplate)FindResource(page == CardLayoutPage.Browse ? "BrowseCardBody" : "InstalledCardHeader");
        CardDensity.Apply(PreviewHost.Resources, _viewModel.Preview.Size);
        _viewModel.PropertyChanged += OnViewModelChanged;

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
    }

    public static CardLayoutPick? Edit(CardLayoutPage page, Guid? current, object? sample)
    {
        var window = new CardLayoutEditorWindow(page, current, sample);
        window.ShowDialog();
        return window._picked;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CardLayoutEditorViewModel.Preview)) CardDensity.Apply(PreviewHost.Resources, _viewModel.Preview.Size);
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CommitName()) return;
        _picked = new CardLayoutPick(_viewModel.SelectedLayout?.Id);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CommitName();
        Close();
    }

    private void NameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _viewModel.CommitName();

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _viewModel.CommitName();
        e.Handled = true;
    }

    private void Grip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragCandidate = (sender as FrameworkElement)?.DataContext as CardFieldRow;
    }

    private void Grip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is not { IsEditable: true } row || e.LeftButton != MouseButtonState.Pressed) return;

        var now = e.GetPosition(this);
        if (Math.Abs(now.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragCandidate = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, row), DragDropEffects.Move);
    }

    private ObservableCollection<CardFieldRow>? RowsOf(CardFieldRow row) =>
        _viewModel.FieldRows.Contains(row) ? _viewModel.FieldRows
        : _viewModel.ListRows.Contains(row) ? _viewModel.ListRows
        : null;

    private void FieldRow_DragOver(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(DragFormat) as CardFieldRow;
        var target = (sender as FrameworkElement)?.DataContext as CardFieldRow;
        e.Effects = dragged is not null && target is not null && RowsOf(dragged) is { } rows && ReferenceEquals(rows, RowsOf(target))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void FieldRow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not CardFieldRow dragged
            || (sender as FrameworkElement)?.DataContext is not CardFieldRow target
            || RowsOf(dragged) is not { } rows
            || !ReferenceEquals(rows, RowsOf(target)))
        {
            return;
        }

        _viewModel.MoveRow(rows, dragged, rows.IndexOf(target));
        e.Handled = true;
    }
}
