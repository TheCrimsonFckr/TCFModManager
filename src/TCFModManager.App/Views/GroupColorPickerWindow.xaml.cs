using System.Windows;
using System.Windows.Media;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public partial class GroupColorPickerWindow : FluentWindow
{
    public sealed record Choice(string Key, string Name, Brush? Brush, ControlAppearance Appearance)
    {
        public bool IsNone => Key.Length == 0;
    }

    private string? _result;

    private GroupColorPickerWindow(string groupName, string? current)
    {
        InitializeComponent();
        DialogFit.Attach(this);

        WindowTitleBar.Title = Title = LocalizationService.Text(Strings.GroupColor_TitleFormat, groupName);
        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        var none = new Choice(string.Empty, Strings.GroupColor_None, null,
            string.IsNullOrEmpty(current) ? ControlAppearance.Primary : ControlAppearance.Secondary);

        ColorList.ItemsSource = GroupColors.All
            .Select(c => new Choice(c.Key, c.Name, GroupColors.Solid(c.Key),
                string.Equals(c.Key, current, StringComparison.OrdinalIgnoreCase) ? ControlAppearance.Primary : ControlAppearance.Secondary))
            .Prepend(none)
            .ToList();
    }

    // The colour key picked, an empty string for no colour, or null when cancelled.
    public static string? Pick(string groupName, string? current)
    {
        var window = new GroupColorPickerWindow(groupName, current);
        return window.ShowDialog() == true ? window._result : null;
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        _result = key;
        DialogResult = true;
    }
}
