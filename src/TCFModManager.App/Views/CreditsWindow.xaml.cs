using System.Diagnostics;
using System.Windows;
using TCFModManager.App.Help;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

public sealed record CreditRow(string Name, string Detail, string? Link = null)
{
    public bool HasLink => !string.IsNullOrWhiteSpace(Link);
}

//
// Who has helped build the app. Opened from Options and from Help. Lists Help/Contributors.cs and
// nothing else - who appears here is decided there, by hand.
//
public partial class CreditsWindow : FluentWindow
{
    private CreditsWindow(Window? owner)
    {
        InitializeComponent();

        Title = Strings.Credits_Title;
        Owner = owner;
        WindowStartupLocation = owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        ContributorList.ItemsSource = Contributors.All
            .Select(c => new CreditRow(c.Name, c.Contribution(), c.Link))
            .ToList();
    }

    public static void Open(Window? owner) => new CreditsWindow(owner).ShowDialog();

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CreditRow { HasLink: true } row) return;

        try
        {
            Process.Start(new ProcessStartInfo(row.Link!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Credits", $"couldn't open {row.Link}: {ex.Message}");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
