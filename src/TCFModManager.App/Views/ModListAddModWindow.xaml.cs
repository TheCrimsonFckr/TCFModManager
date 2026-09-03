using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

//
// One mod the picker offers, and whether it has been ticked.
//
// IsChosen lives on the row rather than on the ListBox because the visible collection is replaced
// whenever the source or the search changes: a tick has to outlive the container that drew it.
//
public sealed partial class ModListAddRow : ObservableObject
{
    [ObservableProperty]
    private bool _isChosen;

    public required ModListAddOption Option { get; init; }

    public required string Detail { get; init; }

    // False for a mod the list already names - shown, so it is clear it is there, but not tickable.
    public required bool CanChoose { get; init; }

    public ModListEntry Entry => Option.Entry;

    public string Name => Option.Entry.Name;

    public bool Matches(string query) =>
        Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (Option.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
}

//
// Picks mods to add to a list, from what this install has and from the cached sp-mod.com catalog.
//
// Nothing here downloads, installs or moves anything: it names mods, and applying the list is what
// acts on them. That is why a mod nobody here has can be added at all - the point of a list is
// often to describe an install someone else should end up with.
//
public partial class ModListAddModWindow : FluentWindow
{
    //
    // The catalog runs to thousands of listings and nobody scrolls that. Showing the first slice
    // and saying so is honest about it; typing is how you reach the rest.
    //
    private const int MaxRows = 200;

    private readonly List<ModListAddRow> _installed;
    private readonly List<ModListAddRow> _catalog;

    private bool _showingCatalog;

    // Set while selection is being restored after the visible collection changed, so the handler
    // that records a tick doesn't treat that restore as the user ticking things.
    private bool _syncing;

    private ModListAddModWindow(ModList list, ModListAddOptions options)
    {
        _installed = [.. options.Installed.Select(o => Row(o, list, fromCatalog: false))];
        _catalog = [.. options.Catalog.Select(o => Row(o, list, fromCatalog: true))];

        InitializeComponent();

        WindowTitleBar.Title = Title = $"Add mods to \"{list.Name}\"";
        InstalledSourceButton.Content = $"Installed ({_installed.Count})";
        CatalogSourceButton.Content = $"sp-mod.com ({_catalog.Count})";

        // The catalog is the only source worth opening on when there was no install to read.
        _showingCatalog = _installed.Count == 0;
        ShowSource();

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
    }

    //
    // The mods the user picked, or nothing when they cancelled.
    //
    // Deduped on the way out, and the installed row wins where a mod appears in both sources: it
    // carries the version that is actually here, where the catalog row is deliberately unpinned.
    //
    public static IReadOnlyList<ModListEntry> Pick(ModList list, ModListAddOptions options)
    {
        var window = new ModListAddModWindow(list, options);
        if (window.ShowDialog() != true) return [];

        var chosen = new List<ModListEntry>();

        foreach (var entry in window._installed.Concat(window._catalog).Where(r => r.IsChosen).Select(r => r.Entry))
        {
            if (!ModListEntries.Contains(chosen, entry)) chosen.Add(entry);
        }

        return chosen;
    }

    private static ModListAddRow Row(ModListAddOption option, ModList list, bool fromCatalog)
    {
        var alreadyOn = ModListEntries.Contains(list.Entries, option.Entry);

        return new ModListAddRow
        {
            Option = option,
            CanChoose = !alreadyOn,
            Detail = Detail(option, alreadyOn, fromCatalog),
        };
    }

    private static string Detail(ModListAddOption option, bool alreadyOn, bool fromCatalog)
    {
        var parts = new List<string>();

        if (alreadyOn) parts.Add("already on this list");
        if (option.Entry.IsAddon) parts.Add("addon");

        if (fromCatalog)
        {
            if (!string.IsNullOrWhiteSpace(option.Author)) parts.Add($"by {option.Author}");

            // Said plainly rather than left to be inferred from the version being absent: a list
            // entry with no version means "the newest published", and that is a real choice.
            parts.Add(option.IsInstalled ? "installed - adds at the newest published version" : "newest published version");
        }
        else
        {
            parts.Add(option.Entry.Version is { } version ? $"version {version}" : "version not known");
            if (option.IsDisabled) parts.Add("disabled");
        }

        return string.Join(" · ", parts);
    }

    private void InstalledSource_Click(object sender, RoutedEventArgs e)
    {
        if (!_showingCatalog) return;

        _showingCatalog = false;
        ShowSource();
    }

    private void CatalogSource_Click(object sender, RoutedEventArgs e)
    {
        if (_showingCatalog) return;

        _showingCatalog = true;
        ShowSource();
    }

    private void ShowSource()
    {
        InstalledSourceButton.Appearance = _showingCatalog ? ControlAppearance.Secondary : ControlAppearance.Primary;
        CatalogSourceButton.Appearance = _showingCatalog ? ControlAppearance.Primary : ControlAppearance.Secondary;

        SourceNote.Text = _showingCatalog
            ? "Mods published on sp-mod.com. Adding one only names it - applying the list is what downloads it."
            : "Mods this install has, recorded at the version you're running.";

        Render();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Render();

    //
    // Rebuilds the visible slice, then puts the ticks back on whatever survived into it. Both
    // halves matter: a tick made under one search has to still be there after another.
    //
    private void Render()
    {
        // The search box raises TextChanged as the XAML is loaded, before the rest of the window
        // exists. AddButton is the last element this method touches, so it is the one to check.
        if (RowsBox is null || AddButton is null) return;

        var query = SearchBox.Text.Trim();
        var source = _showingCatalog ? _catalog : _installed;

        List<ModListAddRow> matched = query.Length == 0 ? source : [.. source.Where(r => r.Matches(query))];
        var shown = matched.Count > MaxRows ? matched.Take(MaxRows).ToList() : matched;

        _syncing = true;
        RowsBox.ItemsSource = shown;
        RowsBox.SelectedItems.Clear();

        foreach (var row in shown.Where(r => r.IsChosen)) RowsBox.SelectedItems.Add(row);

        _syncing = false;

        EmptyNote.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyNote.Text = source.Count == 0
            ? "Nothing to add from here."
            : "Nothing matches that.";

        ShowCount(matched.Count, shown.Count);
        UpdateChosen();
    }

    private void ShowCount(int matched, int shown)
    {
        var scope = _showingCatalog ? "sp-mod.com" : "installed";

        CountNote.Text = matched == shown
            ? $"{shown} of {(_showingCatalog ? _catalog.Count : _installed.Count)} {scope} mod(s) shown."
            : $"Showing the first {shown} of {matched} matches - keep typing to narrow it down.";
    }

    private void RowsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;

        foreach (ModListAddRow row in e.AddedItems) row.IsChosen = true;
        foreach (ModListAddRow row in e.RemovedItems) row.IsChosen = false;

        UpdateChosen();
    }

    private void UpdateChosen()
    {
        var count = _installed.Concat(_catalog).Count(r => r.IsChosen);

        ChosenNote.Text = count == 0 ? "Nothing picked yet." : $"{count} mod(s) picked.";
        AddButton.Content = count == 0 ? "Add" : $"Add {count}";
        AddButton.IsEnabled = count > 0;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
