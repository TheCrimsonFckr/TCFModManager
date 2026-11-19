using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.Localization;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class ModListNameWindow : FluentWindow
{
    private readonly bool _allowEmpty;

    private ModListNameWindow(string title, string description, string confirm, SymbolRegular icon, string? text = null, string? placeholder = null, bool allowEmpty = false)
    {
        InitializeComponent();
        DialogFit.Attach(this);

        WindowTitleBar.Title = title;
        DescriptionText.Text = description;
        CaptureButton.Content = confirm;
        CaptureButton.Icon = new SymbolIcon { Symbol = icon };

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        _allowEmpty = allowEmpty;
        if (placeholder is not null) NameBox.PlaceholderText = placeholder;
        NameBox.Text = text ?? string.Empty;
        CaptureButton.IsEnabled = allowEmpty || !string.IsNullOrWhiteSpace(NameBox.Text);

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public static string? AskForGroupName() => Ask(
        Strings.Installed_NewGroupHeader, Strings.Installed_NewGroupDescription, Strings.Installed_NewGroupButton, SymbolRegular.FolderAdd24);

    public static string? AskForNote(string modName, string? current)
    {
        var window = new ModListNameWindow(
            LocalizationService.Text(Strings.Installed_NoteHeaderFormat, modName), Strings.Installed_NoteDescription,
            Strings.Common_Save, SymbolRegular.Note24, current, Strings.Installed_NotePlaceholder, allowEmpty: true);
        return window.ShowDialog() == true ? window.NameBox.Text.Trim() : null;
    }

    public static string? AskForPageLink(string modName)
    {
        var window = new ModListNameWindow(
            LocalizationService.Text(Strings.ModLink_HeaderFormat, modName), Strings.ModLink_Description,
            Strings.ModLink_Button, SymbolRegular.Link24, placeholder: Strings.ModLink_Placeholder);
        return window.ShowDialog() == true ? window.NameBox.Text.Trim() : null;
    }

    public static string? AskForAddonLink(string modName)
    {
        var window = new ModListNameWindow(
            LocalizationService.Text(Strings.ModLink_AddonHeaderFormat, modName), Strings.ModLink_AddonDescription,
            Strings.ModLink_Button, SymbolRegular.Link24, placeholder: Strings.ModLink_AddonPlaceholder);
        return window.ShowDialog() == true ? window.NameBox.Text.Trim() : null;
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
        CaptureButton.IsEnabled = _allowEmpty || !string.IsNullOrWhiteSpace(NameBox.Text);

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowEmpty && string.IsNullOrWhiteSpace(NameBox.Text)) return;
        DialogResult = true;
    }
}
