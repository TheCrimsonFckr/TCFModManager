using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.Localization;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class ModListNameWindow : FluentWindow
{
    private ModListNameWindow(string title, string description, string confirm, SymbolRegular icon)
    {
        InitializeComponent();

        WindowTitleBar.Title = title;
        DescriptionText.Text = description;
        CaptureButton.Content = confirm;
        CaptureButton.Icon = new SymbolIcon { Symbol = icon };

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        Loaded += (_, _) => NameBox.Focus();
    }

    public static string? AskForCapture() => Ask(
        Strings.ModLists_CaptureHeader, Strings.ModLists_CaptureDescription, Strings.ModLists_Capture, SymbolRegular.AppsList24);

    public static string? AskForEmpty() => Ask(
        Strings.ModLists_EmptyHeader, Strings.ModLists_EmptyDescription, Strings.ModLists_Create, SymbolRegular.DocumentAdd24);

    private static string? Ask(string title, string description, string confirm, SymbolRegular icon)
    {
        var window = new ModListNameWindow(title, description, confirm, icon);
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
