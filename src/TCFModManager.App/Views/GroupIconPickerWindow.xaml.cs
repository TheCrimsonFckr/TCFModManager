using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class GroupIconPickerWindow : FluentWindow
{
    public sealed record Choice(string Key, string Name, ControlAppearance Appearance);

    private readonly string? _current;
    private string? _result;

    private GroupIconPickerWindow(string groupName, string? current)
    {
        _current = current;

        InitializeComponent();
        DialogFit.Attach(this);

        WindowTitleBar.Title = Title = LocalizationService.Text(Strings.GroupIcon_TitleFormat, groupName);
        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        Show(string.Empty);
        Loaded += (_, _) => SearchBox.Focus();
    }

    // The icon key picked, an empty string for no icon, or null when cancelled.
    public static string? Pick(string groupName, string? current)
    {
        var window = new GroupIconPickerWindow(groupName, current);
        return window.ShowDialog() == true ? window._result : null;
    }

    private void Show(string query)
    {
        var shown = GroupIcons.All
            .Where(i => query.Length == 0
                || i.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || i.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(i => new Choice(i.Key, i.Name,
                string.Equals(i.Key, _current, StringComparison.OrdinalIgnoreCase) ? ControlAppearance.Primary : ControlAppearance.Secondary))
            .ToList();

        IconList.ItemsSource = shown;
        NothingFound.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Show(SearchBox.Text.Trim());

    private void Icon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        _result = key;
        DialogResult = true;
    }

    private void None_Click(object sender, RoutedEventArgs e)
    {
        _result = string.Empty;
        DialogResult = true;
    }
}
