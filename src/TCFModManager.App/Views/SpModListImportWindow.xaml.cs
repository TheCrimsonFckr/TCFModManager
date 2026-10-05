using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

//
// One card of the review, laid out like the cards on sp-mod's own list page and on Browse. Ticked
// writes straight through to the review row, which is what is stored. The dependencies badge is the
// one part that moves: it is worked out again whenever any tick changes (see RefreshDependencies).
//
// One line of a dependencies badge's tooltip: the name, with a green tick or a red cross.
public sealed class SpModDependencyTip(string name, bool covered)
{
    public string Name { get; } = name;

    // The window's template picks the icon and its colour from this, so the colour stays a DynamicResource.
    public bool Covered { get; } = covered;
}

public sealed partial class SpModImportRow : ObservableObject
{
    private readonly Action? _changed;

    public SpModImportRow(SpModReviewRow? row, string name, string? detail, Action? changed)
    {
        Row = row;
        Name = name;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail;
        _changed = changed;
    }

    public SpModReviewRow? Row { get; }

    public string Name { get; }

    public string? Version { get; init; }

    // "by author · downloads · updated date" - whatever is known; null hides the line.
    public string? Facts { get; init; }

    public string? Detail { get; }

    public string? Thumbnail { get; init; }

    public SymbolRegular PlaceholderSymbol { get; init; } = SymbolRegular.PuzzleCube24;

    public string? Url { get; init; }

    public string? SptBadge { get; init; }

    public bool IsDependency { get; init; }

    public string? DependencyOfTip { get; init; }

    // The dependencies sp-mod lists on the card, by name. Empty for rows with no card.
    public IReadOnlyList<SpModCardDependency> Dependencies { get; init; } = [];

    [ObservableProperty]
    private string? _dependenciesBadge;

    [ObservableProperty]
    private ControlAppearance _dependenciesAppearance = ControlAppearance.Success;

    // Covered first, then missing - each group in the order sp-mod lists them.
    [ObservableProperty]
    private IReadOnlyList<SpModDependencyTip> _dependencyTips = [];

    // False for the information-only rows: entries going away, and dependencies with no version.
    public bool CanTick => Row is not null;

    public bool Ticked
    {
        get => Row?.Ticked ?? false;
        set
        {
            if (Row is null || Row.Ticked == value) return;

            Row.Ticked = value;
            OnPropertyChanged();
            _changed?.Invoke();
        }
    }

    //
    // A dependency is covered when a ticked row carries its name. One the review has no row for at
    // all - already installed, or simply on the list under a name sp-mod shows differently - falls
    // back to what sp-mod's own card said.
    //
    public void RefreshDependencies(ISet<string> ticked, ISet<string> reviewed)
    {
        if (Dependencies.Count == 0)
        {
            DependenciesBadge = null;
            DependencyTips = [];
            return;
        }

        var covered = new List<string>();
        var missing = new List<string>();

        foreach (var dependency in Dependencies)
        {
            var ok = ticked.Contains(dependency.Name) || (!reviewed.Contains(dependency.Name) && dependency.OnList);
            (ok ? covered : missing).Add(dependency.Name);
        }

        DependenciesAppearance = missing.Count > 0 ? ControlAppearance.Danger : ControlAppearance.Success;
        DependenciesBadge = missing.Count > 0
            ? Strings.SpModImport_BadgeMissing(missing.Count)
            : Strings.SpModImport_BadgeSatisfied(covered.Count);

        DependencyTips = [.. covered.Select(name => new SpModDependencyTip(name, true)),
                          .. missing.Select(name => new SpModDependencyTip(name, false))];
    }
}

public sealed record SpModImportSection(string Title, string? Note, IReadOnlyList<SpModImportRow> Rows)
{
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}

//
// Imports an sp-mod Mod List, or refreshes one already imported.
//
// Three states in one window: the address, a busy panel while sp-mod is asked, and the review.
// Nothing is stored here - the window hands back an SpModListUpdate and the Mod lists page stores
// it, the same way a file import is stored.
//
// One page read per import, made when the user presses Read, plus the batched API lookups for the
// retarget and dependencies. Nothing reads sp-mod's lists index (R14).
//
public partial class SpModListImportWindow : FluentWindow
{
    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);

    private const string ListsUrl = "https://sp-mod.com/lists";

    private static readonly HttpClient PageHttp = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static readonly string UserAgent = new SpModApiOptions().UserAgent;

    private readonly SpModListApi _api = new(AppServices.SpModApi);

    private ModList? _stored;
    private SpModListPage? _page;
    private SpModListReview? _review;
    private bool _retarget;
    private CancellationTokenSource? _busy;

    private SpModListUpdate? _result;

    private SpModListImportWindow(ModList? stored)
    {
        _stored = stored;

        InitializeComponent();

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        if (stored is not null) WindowTitleBar.Title = Title = Text(Strings.SpModImport_RefreshTitleFormat, stored.Name);
        else Title = Strings.SpModImport_Title;

        Activated += (_, _) => PickUpClipboard();
        Loaded += async (_, _) =>
        {
            if (stored?.SpModSource is { } source)
            {
                LinkBox.Text = source.Url;
                await ReadAsync(source.Url);
            }
            else
            {
                ShowLink();
                LinkBox.Focus();
            }
        };
    }

    // Imports a new list. Null when the window was closed without creating one.
    public static SpModListUpdate? Import() => Run(null);

    // Reads an imported list's page again and reviews the changes. Null when nothing was updated.
    public static SpModListUpdate? Refresh(ModList stored) => Run(stored);

    private static SpModListUpdate? Run(ModList? stored)
    {
        var window = new SpModListImportWindow(stored);
        return window.ShowDialog() == true ? window._result : null;
    }

    // ---- step 1: the address ----

    private void ShowLink(string? error = null)
    {
        LinkPanel.Visibility = Visibility.Visible;
        BusyPanel.Visibility = Visibility.Collapsed;
        ReviewPanel.Visibility = Visibility.Collapsed;

        LinkError.Text = error ?? string.Empty;
        LinkError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;

        PrimaryButton.Content = Strings.SpModImport_Read;
        SummaryText.Text = string.Empty;
        UpdateReadButton();
    }

    //
    // A list address on the clipboard fills the box - the round trip from "Find lists on sp-mod"
    // is copy the address, come back. Only into an empty or invalid box, so it never overwrites
    // something the user typed.
    //
    private void PickUpClipboard()
    {
        if (LinkPanel.Visibility != Visibility.Visible) return;
        if (SpModListImport.TryParseListUrl(LinkBox.Text, out _, out _)) return;

        try
        {
            if (!Clipboard.ContainsText()) return;

            var text = Clipboard.GetText().Trim();
            if (SpModListImport.TryParseListUrl(text, out _, out _)) LinkBox.Text = text;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another program has the clipboard open. Nothing to pick up this time.
        }
    }

    private void LinkBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateReadButton();

    private async void LinkBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !PrimaryButton.IsEnabled) return;

        e.Handled = true;
        await ReadAsync(LinkBox.Text);
    }

    private void UpdateReadButton()
    {
        if (PrimaryButton is null || LinkPanel.Visibility != Visibility.Visible) return;
        PrimaryButton.IsEnabled = !string.IsNullOrWhiteSpace(LinkBox.Text);
    }

    private void FindLists_Click(object sender, RoutedEventArgs e) => OpenInBrowser(ListsUrl);

    private void OpenOnSpMod_Click(object sender, RoutedEventArgs e)
    {
        if (_page is not null) OpenInBrowser(_page.Source.ToString());
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("SpModListImport", $"couldn't open {url}: {ex.Message}");
        }
    }

    // ---- reading ----

    private async Task ReadAsync(string text)
    {
        if (!SpModListImport.TryParseListUrl(text, out _, out _))
        {
            ShowLink(Strings.SpModImport_NotAListUrl);
            return;
        }

        await BusyAsync(Strings.SpModImport_Reading, async ct =>
        {
            var read = await SpModListImport.ReadAsync(text, PageHttp, UserAgent, ct);

            if (read.Page is not { } page)
            {
                ShowLink(Describe(read));
                return;
            }

            _page = page;

            // Importing a list that is already here is a refresh of it: same id, same choices.
            _stored ??= AppServices.ModLists.Find(SpModListImport.ListIdFor(page.ListId));
            _retarget = _stored?.SpModSource?.Retargeted ?? false;

            await ResolveAsync(previous: null, ct);
        });
    }

    //
    // The retarget (when switched on) and the dependency check, then the review.
    //
    // previous carries the window's current ticks when it rebuilds; Added is dropped from it,
    // because a dependency picked for one SPT is not the version wanted for another and the fresh
    // check offers it again at the right one.
    //
    private async Task ResolveAsync(SpModListChoices? previous, CancellationToken ct)
    {
        var page = _page!;
        var install = InstalledSpt;

        IReadOnlyList<ModListEntry> entries = page.ToModList(FallbackName, DateTimeOffset.UtcNow).Entries;
        var parents = page.Addons
            .Where(a => a.ParentModId is not null)
            .ToDictionary(a => a.Id, a => a.ParentModId!.Value);

        SpModRetarget? retarget = null;

        if (_retarget && install is not null)
        {
            BusyText.Text = Text(Strings.SpModImport_RetargetingFormat, install);

            retarget = await SpModListResolver.RetargetAsync(entries, parents, install, _api,
                new Progress<SpModResolveProgress>(p =>
                    BusyText.Text = Text(Strings.SpModImport_RetargetingProgressFormat, install, p.Done, p.Total)), ct);

            if (retarget.Succeeded) entries = retarget.Entries;
        }

        SpModDependencies? dependencies = null;
        var dependencySpt = retarget is { Succeeded: true } ? install : page.SptVersion ?? install;

        if (dependencySpt is not null)
        {
            BusyText.Text = Strings.SpModImport_CheckingDependencies;

            dependencies = await SpModListResolver.MissingDependenciesAsync(entries, parents, dependencySpt, _api,
                new Progress<SpModResolveProgress>(p =>
                    BusyText.Text = Text(Strings.SpModImport_CheckingDependenciesProgressFormat, p.Done, p.Total)), ct);
        }

        _review = SpModListReview.Build(page, _stored, retarget, dependencies, previous);
        ShowReview();
    }

    //
    // Runs one step with the busy panel up. Cancel stops it and goes back to where it started -
    // the address, or the review when it was a rebuild.
    //
    private async Task BusyAsync(string message, Func<CancellationToken, Task> work)
    {
        var cameFromReview = ReviewPanel.Visibility == Visibility.Visible;

        LinkPanel.Visibility = Visibility.Collapsed;
        ReviewPanel.Visibility = Visibility.Collapsed;
        BusyPanel.Visibility = Visibility.Visible;
        BusyText.Text = message;
        PrimaryButton.IsEnabled = false;
        SummaryText.Text = string.Empty;

        using var cts = new CancellationTokenSource();
        _busy = cts;

        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (cameFromReview && _review is not null) ShowReview();
            else ShowLink();
        }
        finally
        {
            _busy = null;
        }
    }

    private static string? InstalledSpt =>
        string.IsNullOrWhiteSpace(AppServices.SptEnvironment.InstalledVersion) ? null : AppServices.SptEnvironment.InstalledVersion;

    private string FallbackName => _page is null
        ? Strings.SpModImport_Title
        : Text(Strings.SpModImport_FallbackNameFormat, _page.ListId);

    private static string Describe(SpModListRead read) => read.Failure switch
    {
        SpModListFailure.NotAListUrl => Strings.SpModImport_NotAListUrl,
        SpModListFailure.Network => Text(Strings.SpModImport_FailedNetworkFormat, read.Detail),
        SpModListFailure.HttpStatus when read.Status == System.Net.HttpStatusCode.NotFound => Strings.SpModImport_FailedNotFound,
        SpModListFailure.HttpStatus => Text(Strings.SpModImport_FailedStatusFormat, (int?)read.Status),
        SpModListFailure.Blocked => Strings.SpModImport_FailedBlocked,
        _ => Strings.SpModImport_FailedNotAListPage,
    };

    // ---- step 2: the review ----

    private void ShowReview()
    {
        var review = _review!;
        var page = review.Page;

        LinkPanel.Visibility = Visibility.Collapsed;
        BusyPanel.Visibility = Visibility.Collapsed;
        ReviewPanel.Visibility = Visibility.Visible;

        ListNameText.Text = page.Name ?? FallbackName;
        ListFactsText.Text = Facts(page);

        var notices = Notices(review);
        NoticesText.Text = notices;
        NoticesText.Visibility = notices.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        ShowSptPanel(review);

        var sections = Sections(review);
        _cards = [.. sections.SelectMany(section => section.Rows)];
        RefreshDependencies();

        SectionsList.ItemsSource = sections;
        SectionsScroller.ScrollToTop();

        PrimaryButton.Content = review.IsRefresh ? Strings.SpModImport_Update : Strings.SpModImport_Create;
        UpdateSummary();
    }

    private string Facts(SpModListPage page)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(page.Author)) parts.Add(Text(Strings.SpModImport_ByFormat, page.Author));
        if (page.UpdatedAt is { } updated) parts.Add(Text(Strings.SpModImport_UpdatedFormat, updated.LocalDateTime.ToString("d")));

        parts.Add(Strings.SpModImport_CountMods(page.Mods.Count()));
        if (page.Addons.Any()) parts.Add(Strings.SpModImport_CountAddons(page.Addons.Count()));

        parts.Add(page.SptVersion is { } spt ? Text(Strings.SpModImport_TargetFormat, spt) : Strings.SpModImport_NoTarget);

        return string.Join(Strings.Common_FactSeparator, parts);
    }

    private static string Notices(SpModListReview review)
    {
        var lines = new List<string>();
        var notices = review.Page.Notices;

        if (notices.Any(n => n.Kind == SpModListNoticeKind.LayoutChanged)) lines.Add(Strings.SpModImport_NoticeLayoutChanged);

        foreach (var gap in notices.Where(n => n.Kind == SpModListNoticeKind.CountMismatch))
        {
            lines.Add(Text(gap.ItemKind == SpModListItemKind.Addon
                ? Strings.SpModImport_NoticeAddonsShortFormat
                : Strings.SpModImport_NoticeModsShortFormat, gap.Expected, gap.Read));
        }

        if (notices.Count(n => n.Kind == SpModListNoticeKind.OptedOut) is > 0 and var optedOut)
            lines.Add(Strings.SpModImport_NoticeOptedOut(optedOut));

        if (notices.Count(n => n.Kind == SpModListNoticeKind.Unavailable) is > 0 and var unavailable)
            lines.Add(Strings.SpModImport_NoticeUnavailable(unavailable));

        if (notices.Count(n => n.Kind == SpModListNoticeKind.MissingVersion) is > 0 and var unversioned)
            lines.Add(Strings.SpModImport_NoticeNoVersion(unversioned));

        if (review.Dependencies is { Succeeded: false } deps)
        {
            lines.Add(deps.Failure == SpModResolveFailure.UnknownSptVersion
                ? Text(Strings.SpModImport_DependenciesUnknownSptFormat, deps.SptVersion)
                : Text(Strings.SpModImport_DependenciesFailedFormat, deps.Detail));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void ShowSptPanel(SpModListReview review)
    {
        var install = InstalledSpt;
        var target = review.Page.SptVersion;

        if (install is null || string.Equals(target, install, StringComparison.OrdinalIgnoreCase))
        {
            SptPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SptPanel.Visibility = Visibility.Visible;

        var message = target is null
            ? Text(Strings.SpModImport_SptNoTargetFormat, install)
            : Text(Strings.SpModImport_SptMismatchFormat, target, install);

        if (review.Retarget is { Succeeded: false } failed)
        {
            message += Environment.NewLine + Text(Strings.SpModImport_RetargetFailedFormat, install, failed.Detail);
            SptBar.Severity = InfoBarSeverity.Warning;
        }
        else
        {
            SptBar.Severity = InfoBarSeverity.Informational;
        }

        SptBar.Message = message;

        var label = Text(Strings.SpModImport_UseVersionsForFormat, install);
        RetargetSwitch.OnContent = label;
        RetargetSwitch.OffContent = label;
        RetargetSwitch.IsChecked = _retarget;
    }

    private async void RetargetSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_review is null) return;

        _retarget = RetargetSwitch.IsChecked == true;

        var ticks = _review.ToChoices();
        var previous = new SpModListChoices(ticks.Excluded, []);

        await BusyAsync(Strings.SpModImport_Reading, ct => ResolveAsync(previous, ct));
    }

    private List<SpModImportSection> Sections(SpModListReview review)
    {
        var sections = new List<SpModImportSection>();
        var target = review.Retargeted ? review.Retarget!.TargetSptVersion : review.Page.SptVersion ?? InstalledSpt;

        void Add(SpModReviewSection section, string title, string? note)
        {
            var rows = review.In(section).Select(Row).ToList();
            if (rows.Count > 0) sections.Add(new SpModImportSection(Text(Strings.SpModImport_SectionCountFormat, title, rows.Count), note, rows));
        }

        Add(SpModReviewSection.MissingParents, Strings.SpModImport_SectionMissingParents, Strings.SpModImport_MissingParentsNote);
        Add(SpModReviewSection.Mods, Strings.SpModImport_SectionMods, null);
        Add(SpModReviewSection.Addons, Strings.SpModImport_SectionAddons, null);
        Add(SpModReviewSection.Dependencies, Strings.SpModImport_SectionDependencies, Strings.SpModImport_DependenciesNote);
        Add(SpModReviewSection.NoVersion, Strings.SpModImport_SectionNoVersion, Text(Strings.SpModImport_NoVersionNoteFormat, target));

        if (review.Unaddable.Count > 0)
        {
            sections.Add(new SpModImportSection(
                Text(Strings.SpModImport_SectionCountFormat, Strings.SpModImport_SectionUnaddable, review.Unaddable.Count),
                Text(Strings.SpModImport_UnaddableNoteFormat, review.Dependencies!.SptVersion),
                [.. review.Unaddable.Select(d => new SpModImportRow(null, d.Name,
                    Text(Strings.SpModImport_FactNeededByFormat, string.Join(Strings.Common_ListSeparator, d.NeededBy)), null))]));
        }

        if (review.Removed.Count > 0)
        {
            sections.Add(new SpModImportSection(
                Text(Strings.SpModImport_SectionCountFormat, Strings.SpModImport_SectionRemoved, review.Removed.Count),
                Strings.SpModImport_RemovedNote,
                [.. review.Removed.Select(e => new SpModImportRow(null, e.Name, VersionFact(e.Version), null))]));
        }

        return sections;
    }

    private List<SpModImportRow> _cards = [];

    //
    // A card: what sp-mod's list page showed beside the entry, or - for the rows the page never
    // showed (missing parents with no card, dependencies) - what the catalog knows about it.
    //
    private SpModImportRow Row(SpModReviewRow row)
    {
        var parts = new List<string>();

        if (row.Change == SpModReviewChange.New) parts.Add(Strings.SpModImport_FactNew);

        if (row.FromVersion is not null) parts.Add(Text(Strings.SpModImport_FactRetargetedFormat, row.FromVersion, row.Entry.Version));
        else if (row.Entry.Version is null) parts.Add(Strings.SpModImport_FactNoVersion);

        if (row.Change == SpModReviewChange.VersionChanged) parts.Add(Text(Strings.SpModImport_FactWasFormat, row.StoredVersion));
        if (row.ParentName is not null) parts.Add(Text(Strings.SpModImport_FactAddonForFormat, row.ParentName));
        if (row.NeededBy.Count > 0) parts.Add(Text(Strings.SpModImport_FactNeededByFormat, string.Join(Strings.Common_ListSeparator, row.NeededBy)));
        if (row.Conflict) parts.Add(Strings.SpModImport_FactConflict);
        if (row.NotCompatible && !_review!.Retargeted) parts.Add(Strings.SpModImport_FactNotCompatible);
        if (row.ParentUnknown) parts.Add(Strings.SpModImport_FactParentNotOnList);

        var entry = row.Entry;
        var card = row.Card;

        string? thumbnail = card?.Thumbnail, author = card?.Author, url = card?.Url;
        long? downloads = card?.Downloads;
        var updated = card?.UpdatedAt;

        if (entry.ModId is int id && (thumbnail is null || author is null || downloads is null || url is null))
        {
            if (entry.IsAddon && AppServices.Addons.ById(id) is { } addon)
            {
                thumbnail ??= addon.Thumbnail;
                author ??= addon.Owner?.Name;
                downloads ??= addon.Downloads;
                updated ??= addon.UpdatedAt;
                url ??= addon.DetailUrl;
            }
            else if (!entry.IsAddon && AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id) is { } mod)
            {
                thumbnail ??= mod.Thumbnail;
                author ??= mod.Owner?.Name;
                downloads ??= mod.Downloads;
                updated ??= mod.UpdatedAt;
                url ??= mod.DetailUrl;
            }

            url ??= SpModListImport.PageFor(entry.IsAddon, id);
        }

        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(author)) facts.Add(Text(Strings.SpModImport_ByFormat, author));
        if (downloads is { } count) facts.Add(Text(Strings.Common_DownloadsFormat, count));
        if (updated is { } when) facts.Add(Text(Strings.SpModImport_UpdatedFormat, when.LocalDateTime.ToString("d")));

        // A retargeted version is for this install's SPT, not the one the page showed it for.
        var spt = row.FromVersion is not null ? _review!.Retarget?.TargetSptVersion : card?.SptVersion;

        return new SpModImportRow(row, entry.Name, string.Join(Strings.Common_FactSeparator, parts), CardsChanged)
        {
            Version = entry.Version,
            Facts = facts.Count > 0 ? string.Join(Strings.Common_FactSeparator, facts) : null,
            Thumbnail = thumbnail,
            PlaceholderSymbol = entry.IsAddon ? SymbolRegular.PuzzlePiece24 : SymbolRegular.PuzzleCube24,
            Url = url,
            SptBadge = spt is null ? null : Text(Strings.SpModImport_BadgeSptFormat, spt),
            IsDependency = card?.IsDependency == true || row.Section == SpModReviewSection.Dependencies,
            DependencyOfTip = row.NeededBy.Count > 0
                ? Text(Strings.SpModImport_FactNeededByFormat, string.Join(Strings.Common_ListSeparator, row.NeededBy))
                : null,
            Dependencies = card?.Dependencies ?? [],
        };
    }

    private void CardsChanged()
    {
        RefreshDependencies();
        UpdateSummary();
    }

    private void RefreshDependencies()
    {
        var ticked = _cards.Where(c => c.Ticked).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewed = _cards.Where(c => c.CanTick).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var card in _cards) card.RefreshDependencies(ticked, reviewed);
    }

    private void OpenRowPage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SpModImportRow { Url: { } url }) OpenInBrowser(url);
    }

    private static string VersionFact(string? version) => version is null
        ? Strings.SpModImport_FactNoVersion
        : Text(Strings.ModLists_AddFactVersionFormat, version);

    //
    // Versions for this install's SPT were asked for and didn't arrive. Storing now would save the
    // page's versions under a list that says it was retargeted - on a refresh, quietly undoing the
    // retarget it was made with - so it waits until the lookup works or the option is switched off.
    //
    private bool RetargetMissing =>
        _retarget && InstalledSpt is not null && _review?.Retarget is not { Succeeded: true };

    private void UpdateSummary()
    {
        if (_review is null) return;

        if (RetargetMissing)
        {
            SummaryText.Text = Text(Strings.SpModImport_RetargetBlockedFormat, InstalledSpt);
            PrimaryButton.IsEnabled = false;
            return;
        }

        var ticked = _review.TickedRows.Count();
        var total = _review.Rows.Count;

        var summary = Text(Strings.SpModImport_TickedFormat, ticked, total);

        if (_review.IsRefresh && !_review.ToUpdate(FallbackName, DateTimeOffset.UtcNow).HasChanges)
            summary += Strings.Common_FactSeparator + Strings.SpModImport_NoChanges;

        SummaryText.Text = summary;
        PrimaryButton.IsEnabled = ticked > 0;
    }

    // ---- buttons ----

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReviewPanel.Visibility == Visibility.Visible && _review is not null && !RetargetMissing)
        {
            _result = _review.ToUpdate(FallbackName, DateTimeOffset.UtcNow);
            DialogResult = true;
            return;
        }

        if (LinkPanel.Visibility == Visibility.Visible) await ReadAsync(LinkBox.Text);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy is { } busy)
        {
            busy.Cancel();
            return;
        }

        DialogResult = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _busy?.Cancel();
        base.OnClosing(e);
    }
}
