using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class OptionsPage : Page
{
    public OptionsViewModel ViewModel { get; } = new();

    // What the search has hidden, so clearing it only puts back what it took away - a card whose
    // own Visibility is bound (Server map) goes back to whatever its binding says.
    private readonly HashSet<UIElement> _hiddenBySearch = [];

    // Expanders the search opened because the match was inside them; closed again when the search
    // moves on, so an empty search leaves the page as it opens.
    private readonly HashSet<Expander> _openedBySearch = [];

    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public OptionsPage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        // Leaving the page drops the search, as Help does, so the next visit opens unfiltered.
        Unloaded += (_, _) => SettingsSearch.Text = string.Empty;
    }

    // Same reason as ServerMapPage: the key file can appear or change while this app is open, and
    // the section that shows it is right here.
    // The held size too, since a removal or Undo elsewhere changes it.
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AppServices.ServerMap.RefreshLocalKey();
        ViewModel.RefreshRemovedModsSize();
        ViewModel.Profiles.Refresh();
        ViewModel.Followed.Refresh();
    }

    private void DataFiles_Click(object sender, RoutedEventArgs e) =>
        new DataFilesWindow { Owner = Window.GetWindow(this) }.Show();

    private void Credits_Click(object sender, RoutedEventArgs e) => CreditsWindow.Open(Window.GetWindow(this));

    private void SettingsSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || SettingsSearch.Text.Length == 0) return;

        SettingsSearch.Text = string.Empty;
        e.Handled = true;
    }

    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    //
    // Every word typed has to appear somewhere in a setting - its name, hint, notes, the labels of
    // its buttons and switches, its tooltips, and whatever sits inside its expander. A heading shows
    // while anything under it does. Works on the page itself rather than a list of keywords, so a
    // setting added to the XAML is searchable with nothing else to keep in step.
    //
    private void ApplySearch()
    {
        foreach (var element in _hiddenBySearch) Restore(element);
        _hiddenBySearch.Clear();

        foreach (var expander in _openedBySearch) expander.IsExpanded = false;
        _openedBySearch.Clear();

        var words = (SettingsSearch.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            SettingsNoMatches.Visibility = Visibility.Collapsed;
            return;
        }

        TextBlock? heading = null;
        var headingHasMatch = false;
        var any = false;

        foreach (var child in SettingsRoot.Children.OfType<UIElement>())
        {
            if (ReferenceEquals(child, SettingsSearch) || ReferenceEquals(child, SettingsNoMatches)) continue;

            if (child is TextBlock section)
            {
                CloseSection(heading, headingHasMatch);
                heading = section;
                headingHasMatch = false;
                continue;
            }

            // Already hidden by its own binding - not part of this install's page, so not a match.
            if (child.Visibility != Visibility.Visible) continue;

            if (!Matches(TextOf(child), words))
            {
                Hide(child);
                continue;
            }

            // Found only inside a closed expander: open it, so what matched is on screen.
            if (child is Expander { IsExpanded: false } expander && !Matches(HeaderTextOf(expander), words))
            {
                expander.IsExpanded = true;
                _openedBySearch.Add(expander);
            }

            headingHasMatch = true;
            any = true;
        }

        CloseSection(heading, headingHasMatch);
        SettingsNoMatches.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CloseSection(TextBlock? heading, bool hasMatch)
    {
        if (heading is not null && !hasMatch) Hide(heading);
    }

    private void Hide(UIElement element)
    {
        // SetCurrentValue rather than a plain set, so a bound Visibility keeps its binding.
        element.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
        _hiddenBySearch.Add(element);
    }

    private static void Restore(UIElement element)
    {
        if (BindingOperations.GetBindingExpressionBase(element, VisibilityProperty) is { } binding)
        {
            binding.UpdateTarget();
        }
        else
        {
            element.ClearValue(VisibilityProperty);
        }
    }

    private static bool Matches(string text, string[] words)
    {
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        return words.All(w => compare.IndexOf(text, w, Loose) >= 0);
    }

    private static string HeaderTextOf(Expander expander)
    {
        var text = new StringBuilder();
        Collect(expander.Header, text, []);
        if (expander.ToolTip is string tip) text.Append(tip).Append(' ');
        return text.ToString();
    }

    private static string TextOf(UIElement element)
    {
        var text = new StringBuilder();
        Collect(element, text, []);
        return text.ToString();
    }

    //
    // The words in a setting, walked through the logical tree - which, unlike the visual tree, also
    // holds the content of an expander that has never been opened. A CardControl's Header is read
    // directly as well, in case the control doesn't put it in the logical tree.
    //
    private static void Collect(object? node, StringBuilder text, HashSet<object> seen)
    {
        if (node is null || !seen.Add(node)) return;

        switch (node)
        {
            case string s:
                text.Append(s).Append(' ');
                return;

            case TextBlock block:
                text.Append(block.Text).Append(' ');
                break;
        }

        if (node is FrameworkElement { ToolTip: string tip }) text.Append(tip).Append(' ');
        if (node is Wpf.Ui.Controls.CardControl card) Collect(card.Header, text, seen);

        if (node is DependencyObject parent)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent)) Collect(child, text, seen);
        }
    }
}
