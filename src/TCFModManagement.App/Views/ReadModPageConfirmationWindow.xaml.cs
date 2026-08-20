using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Wpf.Ui.Controls;

namespace TCFModManagement.App.Views;

// One row in ReadModPageConfirmationWindow - a mod name plus the button that opens its sp-mod.com page.
public sealed class ModPageLink(string name, string? url) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public string? Url { get; } = url;
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    private bool _isOpened = string.IsNullOrWhiteSpace(url);
    public bool IsOpened
    {
        get => _isOpened;
        set
        {
            if (_isOpened == value) return;
            _isOpened = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpened)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ButtonLabel)));
        }
    }

    public string ButtonLabel => !HasUrl ? "No page available" : IsOpened ? "Opened" : "Open page";

    public event PropertyChangedEventHandler? PropertyChanged;
}

// 
// Modal gate shown before mods are queued for install/update, requiring every listed mod's page
// to be opened before the Continue button unlocks. Implemented as a plain FluentWindow so it can
// be shown via ShowDialog() from any call site.
// 
public partial class ReadModPageConfirmationWindow : FluentWindow
{
    private readonly List<ModPageLink> _links;

    // Single-mod gate - used by a direct Install/Update click.
    public ReadModPageConfirmationWindow(string modName, string? modPageUrl)
        : this([new ModPageLink(modName, modPageUrl)])
    {
    }

    // Multi-mod gate - used for a batch of missing dependencies.
    public ReadModPageConfirmationWindow(IReadOnlyList<ModPageLink> links)
    {
        _links = links.ToList();
        InitializeComponent();

        WindowTitleBar.Title = Title = _links.Count == 1
            ? $"Read {_links[0].Name}'s page first"
            : $"Read {_links.Count} mod pages first";

        LinksList.ItemsSource = _links;
        foreach (var link in _links) link.PropertyChanged += (_, _) => UpdateContinueEnabled();

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        UpdateContinueEnabled();
    }

    // Shows the gate for one mod and returns true only if Continue was clicked.
    public static bool Confirm(string modName, string? modPageUrl) =>
        new ReadModPageConfirmationWindow(modName, modPageUrl).ShowDialog() == true;

    // Shows the gate for a batch of mods and returns true only if Continue was clicked.
    public static bool ConfirmAll(IReadOnlyList<ModPageLink> links) =>
        new ReadModPageConfirmationWindow(links).ShowDialog() == true;

    private void UpdateContinueEnabled() => ContinueButton.IsEnabled = _links.All(l => l.IsOpened);

    private void OpenLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModPageLink link } || !link.HasUrl) return;

        Process.Start(new ProcessStartInfo(link.Url!) { UseShellExecute = true });
        link.IsOpened = true;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
