using System.ComponentModel;
using System.Windows;
using TCFModManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class ThemeEditorWindow : FluentWindow
{
    private readonly ThemeEditorViewModel _viewModel;

    private ThemeEditorWindow()
    {
        _viewModel = new ThemeEditorViewModel();
        DataContext = _viewModel;

        InitializeComponent();

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        Closing += OnClosing;
        Closed += (_, _) => _viewModel.OnClosed();
    }

    public static void Open() => new ThemeEditorWindow().ShowDialog();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.ConfirmClose()) e.Cancel = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
