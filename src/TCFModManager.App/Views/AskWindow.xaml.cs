using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Views;

public enum AskChoice
{
    First,
    Second,
    Cancel,
}

public sealed record AskSection(string Heading, IReadOnlyList<string> Items)
{
    private const int MaxShown = 10;

    public IReadOnlyList<string> Shown =>
        Items.Count <= MaxShown + 1
            ? Items
            : [.. Items.Take(MaxShown), Strings.Ask_More(Items.Count - MaxShown, Items.Count - MaxShown)];
}

public sealed class AskRequest
{
    public required string Title { get; init; }

    public required string Lead { get; init; }

    public IReadOnlyList<AskSection> Sections { get; init; } = [];

    public IReadOnlyList<string> Detail { get; init; } = [];

    public string? Footnote { get; init; }

    public required string First { get; init; }

    public string? Second { get; init; }

    public string? Cancel { get; init; } = Strings.Common_Cancel;

    public AskChoice Default { get; init; } = AskChoice.First;

    public AskChoice? Danger { get; init; }
}

public partial class AskWindow : FluentWindow
{
    private AskChoice _choice = AskChoice.Cancel;

    private AskWindow(AskRequest request)
    {
        InitializeComponent();

        Title = request.Title;
        WindowTitleBar.Title = request.Title;
        LeadText.Text = request.Lead;
        SectionList.ItemsSource = request.Sections;
        DetailList.ItemsSource = request.Detail;

        if (string.IsNullOrEmpty(request.Footnote)) FootnoteText.Visibility = Visibility.Collapsed;
        else FootnoteText.Text = request.Footnote;

        Wire(FirstButton, request.First, AskChoice.First, request);
        Wire(SecondButton, request.Second, AskChoice.Second, request);
        Wire(CancelButton, request.Cancel, AskChoice.Cancel, request);

        var owner = Application.Current?.MainWindow;
        Owner = owner is { IsVisible: true } ? owner : null;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        if (Owner is null)
        {
            Topmost = true;
            ShowInTaskbar = true;
        }

        Loaded += (_, _) => ButtonFor(request.Default)?.Focus();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public static AskChoice Show(AskRequest request)
    {
        var window = new AskWindow(request);
        window.ShowDialog();
        return window._choice;
    }

    private void Wire(Button button, string? label, AskChoice choice, AskRequest request)
    {
        if (label is null)
        {
            button.Visibility = Visibility.Collapsed;
            return;
        }

        button.Content = label;
        button.IsDefault = request.Default == choice;

        if (request.Danger == choice) button.Appearance = ControlAppearance.Danger;
        else if (choice == AskChoice.First) button.Appearance = ControlAppearance.Primary;

        button.Click += (_, _) =>
        {
            _choice = choice;
            DialogResult = true;
        };
    }

    private Button? ButtonFor(AskChoice choice)
    {
        var button = choice switch
        {
            AskChoice.First => FirstButton,
            AskChoice.Second => SecondButton,
            _ => CancelButton,
        };

        return button.Visibility == Visibility.Visible ? button : null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        e.Handled = true;
        Close();
    }
}
