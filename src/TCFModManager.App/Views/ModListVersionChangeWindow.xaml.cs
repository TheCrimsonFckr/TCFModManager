using System.Windows;
using TCFModManager.App.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

// One row - the mod, what the list asked for, what is actually available, and whether to take it.
public sealed class ModListVersionChangeRow(ModListVersionChange change)
{
    public ModListVersionChange Change { get; } = change;

    public string Name { get; } = change.Mod.Name ?? change.Action.Name;

    public string Detail { get; } =
        $"Wanted {change.Wanted} - newest published is {change.Available.Version ?? "unknown"}";

    // Ticked by default: the substitution is the useful answer, and leaving every box empty would
    // turn "asking" into busywork. Unticking one skips that mod rather than installing something
    // the list didn't name.
    public bool IsAccepted { get; set; } = true;
}

//
// Asks which of a list's unavailable versions should be installed at the newest version instead.
//
// Applying a list never substitutes a version on its own - the list named a specific build, and
// quietly installing a different one is the kind of thing that desyncs a Fika group without anyone
// noticing. This is where that decision is made visible.
//
public partial class ModListVersionChangeWindow : FluentWindow
{
    private readonly List<ModListVersionChangeRow> _rows;

    public ModListVersionChangeWindow(IReadOnlyList<ModListVersionChange> changes)
    {
        _rows = [.. changes.Select(c => new ModListVersionChangeRow(c))];
        InitializeComponent();

        WindowTitleBar.Title = Title = _rows.Count == 1
            ? $"{_rows[0].Name}'s version is no longer published"
            : $"{_rows.Count} versions are no longer published";

        ChangesList.ItemsSource = _rows;

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
    }

    //
    // Returns the changes the user accepted - empty when there is nothing to ask about, and empty
    // when they cancelled, which skips every affected mod rather than the whole apply.
    //
    public static IReadOnlyList<ModListVersionChange> Approve(IReadOnlyList<ModListVersionChange> changes)
    {
        if (changes.Count == 0) return [];

        var window = new ModListVersionChangeWindow(changes);
        if (window.ShowDialog() != true) return [];

        return [.. window._rows.Where(r => r.IsAccepted).Select(r => r.Change)];
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
