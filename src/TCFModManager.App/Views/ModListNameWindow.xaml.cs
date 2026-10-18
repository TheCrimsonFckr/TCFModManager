using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class ModListNameWindow : FluentWindow
{
    private ModListNameWindow()
    {
        InitializeComponent();

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        Loaded += (_, _) => NameBox.Focus();
    }

    public static string? Ask()
    {
        var window = new ModListNameWindow();
        return window.ShowDialog() == true ? window.NameBox.Text.Trim() : null;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        CaptureButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return;
        DialogResult = true;
    }
}
