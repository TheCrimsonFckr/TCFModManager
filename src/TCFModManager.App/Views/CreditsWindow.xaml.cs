using System.Diagnostics;
using System.Globalization;
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
// Who has helped build the app. Opened from Options and from Help.
//
// Translations are read from every shipped language's own resources rather than listed here, so
// a translator who puts their name in Meta_TranslationCredit appears without a code change.
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

        var translations = Translations();
        TranslationList.ItemsSource = translations;
        TranslationsHeader.Visibility = translations.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public static void Open(Window? owner) => new CreditsWindow(owner).ShowDialog();

    private static List<CreditRow> Translations()
    {
        var rows = new List<CreditRow>();

        foreach (var culture in AppLanguage.Available)
        {
            var credit = CreditFor(culture);
            if (string.IsNullOrWhiteSpace(credit)) continue;

            rows.Add(new CreditRow(AppLanguage.DisplayName(culture), credit));
        }

        return rows;
    }

    //
    // The language's own credit line, or nothing. English has none, and a language that hasn't set
    // one falls back to the neutral resources - English's empty one - which is the same answer.
    //
    private static string? CreditFor(CultureInfo culture)
    {
        try
        {
            return Strings.ResourceManager.GetString("Meta_TranslationCredit", culture);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Credits", $"couldn't read the translation credit for {culture.Name}: {ex.Message}");
            return null;
        }
    }

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
