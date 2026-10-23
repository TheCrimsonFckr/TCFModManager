using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One saved list in the left-hand list.
public sealed partial class ModListRowViewModel(
    ModList list, bool isActive, bool isActiveServer = false, bool isPublished = false)
    : LocalizedViewModel
{
    public ModList List { get; } = list;

    public Guid Id => List.Id;

    public string Name => List.Name;

    public bool IsEditable => List.IsEditable;

    // The install's own list, the one it chose to follow.
    public bool IsActive { get; } = isActive;

    //
    // The server's list, followed alongside the one above rather than instead of it. Its own badge
    // because "following" means something different here: the server decides what is on it.
    //
    public bool IsActiveServer { get; } = isActiveServer;

    //
    // Came from a server, whether or not it is being followed.
    //
    // Badged on ORIGIN rather than on being active, because that is the question being asked when
    // you look down the list: which of these did I write and which did a server hand me. Gating it
    // on "active" meant a list you had fetched but not applied was indistinguishable from your own,
    // which is exactly backwards - the one you have not applied yet is the one you most need to
    // recognise.
    //
    public bool IsFromServer => List.Origin == ModListOrigin.Server;

    // Imported from an sp-mod Mod List page, so it has a page to be read again.
    public bool IsFromSpMod => List.SpModSource is not null;

    //
    // The list THIS machine serves to its own clients. Badged because among a dozen personal lists
    // the one that other people are being handed is the one you must not edit carelessly.
    //
    // Passed in from ModListData.PublishedListId, like the two above. It was a flag on the list
    // itself and the flag never survived a save - see ModListData.PublishedListId.
    //
    public bool IsPublished { get; } = isPublished;

    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    public string Detail
    {
        get
        {
            //
            // A row of separate facts, not a sentence - which is why these stay as pieces where the
            // rest of S4 turns pieces into whole sentences. Each one stands on its own, the
            // separator is a key, and writing it as sentences would mean one per combination.
            //
            var parts = new List<string>
            {
                List.Entries.Count == 1
                    ? Strings.ModLists_DetailOneMod
                    : Text(Strings.ModLists_DetailModsFormat, List.Entries.Count),
            };

            if (List.IsSnapshot) parts.Add(Strings.ModLists_DetailSnapshot);

            if (IsPublished) parts.Add(Strings.ModLists_DetailPublished);

            parts.Add(List.Origin switch
            {
                ModListOrigin.Imported when List.SpModSource is not null => List.Source is null
                    ? Strings.ModLists_DetailFromSpMod
                    : Text(Strings.ModLists_DetailFromSpModFormat, List.Source),
                ModListOrigin.Imported => List.Source is null
                    ? Strings.ModLists_DetailImported
                    : Text(Strings.ModLists_DetailFromFormat, List.Source),
                ModListOrigin.Server => List.Source is null
                    ? Strings.ModLists_DetailFromServer
                    : Text(Strings.ModLists_DetailFromFormat, List.Source),
                _ => Strings.ModLists_DetailMadeHere,
            });

            if (List.Revision > 1) parts.Add(Text(Strings.ModLists_DetailRevisionFormat, List.Revision));

            return string.Join(Strings.Common_FactSeparator, parts);
        }
    }
}

//
// One line of a plan, laid out like a line of the list's contents - title, what the list records
// about it, its scope - with a coloured badge saying where this machine stands against the entry.
// Tone picks the badge colour - Good (green), Warn (caution yellow), Disabled (blue), Bad (red),
// Neutral (grey) - and Icon is the shared status glyph for the same state, so a badge reads as the
// icon Installed would show for that mod.
//
public sealed record ModListActionRowViewModel(
    string Kind,
    string Name,
    string Detail,
    int Order,
    ModListAction Action,
    string EntryDetail,
    string? ScopeLabel,
    string Status,
    string Tone,
    string Icon)
{
    public bool HasScope => ScopeLabel is not null;

    public bool CanPin => Action is { Kind: ModListActionKind.Disable, Installed: not null };

    public bool CanUnpin => Action is { Kind: ModListActionKind.Pinned, Installed: not null };
}

// One coloured count at the top of a preview: "1 missing", "72 already right".
public sealed record PlanChip(string Text, string Tone, string Icon);

// One mod on the selected list, as the contents panel shows it.
public sealed record ModListEntryRowViewModel(ModListEntry Entry, string Name, string Detail, bool IsPinnedHere = false)
{
    //
    // Shown on every row, including Everyone.
    //
    // Hiding the default was the first cut and it was wrong: this is a value you cycle, so an
    // unlabelled row reads as "not set" rather than "set to everyone", and the button that changes
    // it has no visible starting point. Every state gets a label, always visible.
    //
    public string ScopeLabel => ScopeUnchecked ? Strings.ModLists_ScopeUnchecked : ModListScopes.Label(Entry.EffectiveScope);

    //
    // An sp-mod list's entry whose files haven't been seen yet. Its stored scope is only the default,
    // and showing that as "Server + Client + Headless" would read as a decision already made, so the
    // chip says it hasn't been checked and the tooltip says when it will be.
    //
    public bool ScopeUnchecked { get; init; }

    public string? ScopeToolTip => ScopeUnchecked ? Strings.ModLists_ScopeUncheckedToolTip : null;

    //
    // The card half, as Browse and the sp-mod import window show a mod: thumbnail, who made it and
    // how widely it is used, the SPT its version is for, and a link to its page. All of it from the
    // catalog already loaded - nothing here asks sp-mod anything - so an entry the catalog doesn't
    // know (one added by hand, or a mod not on sp-mod) is the plain row it always was.
    //
    public string? Thumbnail { get; init; }

    public Wpf.Ui.Controls.SymbolRegular PlaceholderSymbol => Entry.IsAddon
        ? Wpf.Ui.Controls.SymbolRegular.PuzzlePiece24
        : Wpf.Ui.Controls.SymbolRegular.PuzzleCube24;

    public string? Facts { get; init; }

    public string? Category { get; init; }

    public string? Teaser { get; init; }

    public string? CardFacts => string.IsNullOrEmpty(Category)
        ? Facts
        : string.IsNullOrEmpty(Facts) ? Category : Facts + Strings.Common_FactSeparator + Category;

    public string Summary => string.IsNullOrEmpty(Facts) ? Detail : Facts + Strings.Common_FactSeparator + Detail;

    public string? SptBadge { get; init; }

    public string? Url { get; init; }

    // For Browse's "Has dependencies" badge, which looks it up once per mod and remembers it.
    public Mod? Mod { get; init; }

    public HeadlessAdvice? Advice { get; init; }

    public ModListEntryScope? AdviceScope { get; init; }

    public bool AdviceFromParent { get; init; }

    public bool AdviceKept { get; init; }

    public bool AdviceKeepsOff => !AdviceKept && Advice is { } advice && HeadlessAdviceScopes.KeepsOff(advice.Verdict, Entry.EffectiveScope);

    public bool AdviceIsWarning => Advice is { } advice && HeadlessAdviceScopes.IsWarning(advice.Verdict, Entry.EffectiveScope);

    public bool NeedsAttention => AdviceKeepsOff || AdviceIsWarning;

    public bool HasAdviceAction => AdviceScope is not null;

    public string? AdviceLabel =>
        AdviceKeepsOff ? Strings.ModLists_AdviceKeepOffLabel
        : AdviceIsWarning ? Strings.ModLists_AdviceNeedsBadge
        : null;

    public string? AdviceQuote => AdviceLabel is null ? null : LocalizationService.Text(Strings.ModLists_AdviceQuoteFormat, FirstQuote);

    public string? AdviceBadge =>
        Advice?.Verdict == HeadlessVerdict.Needs && !AdviceIsWarning ? Strings.ModLists_AdviceNeedsBadge : null;

    public string? AdviceActionLabel => AdviceScope is { } scope
        ? LocalizationService.Text(Strings.ModLists_AdviceSetScopeFormat, ModListScopes.Label(scope))
        : null;

    public string? AdviceToolTip => Advice is { Quotes.Count: > 0 } advice
        ? LocalizationService.Text(
            AdviceFromParent ? Strings.ModLists_AdviceParentQuotesFormat : Strings.ModLists_AdviceQuotesFormat,
            string.Join(Environment.NewLine, advice.Quotes))
        : null;

    private string FirstQuote => Advice?.Quotes.FirstOrDefault() ?? string.Empty;
}

//
// The names the page puts on a scope.
//
// Kept in one place because the row's scope dropdown, the filter dropdown and the message
// after a change all have to agree - four spellings of "Client + Headless" is how a user ends up
// unsure whether they are looking at the same setting.
//
public static class ModListScopes
{
    public const ModListEntryScope ClientsAndHeadless = ModListEntryScope.Client | ModListEntryScope.Headless;

    public const ModListEntryScope ServerAndClients = ModListEntryScope.Server | ModListEntryScope.Client;

    //
    // Named as the machines they reach, in one order - server, client, headless - rather than as
    // "everyone" and a set of exceptions to it. Every label is then read the same way: what is in
    // the name gets the mod and what is not, does not.
    //
    public static string Label(ModListEntryScope scope) =>
        Key(scope) is { } key ? LocalizationService.Get(key) : ModListEntryScopeConverter.Name(scope);

    //
    // The key rather than the text, for the filter entries: those hold a key and read the text back
    // on every get, so they follow a language change instead of keeping whichever language built
    // the list.
    //
    // Null for a combination the button cannot produce. A hand-edited file can hold any of them,
    // and a row that refuses to describe itself is worse than one that spells the flags out - so
    // those fall through to the converter's own name, which is data rather than prose.
    //
    public static string? Key(ModListEntryScope scope) => scope switch
    {
        ModListEntryScope.Everyone => nameof(Strings.ModLists_ScopeEveryone),
        ServerAndClients => nameof(Strings.ModLists_ScopeServerAndClients),
        ClientsAndHeadless => nameof(Strings.ModLists_ScopeClientsAndHeadless),
        ModListEntryScope.Client => nameof(Strings.ModLists_ScopeClientOnly),
        ModListEntryScope.Headless => nameof(Strings.ModLists_ScopeHeadlessOnly),
        ModListEntryScope.Server => nameof(Strings.ModLists_ScopeServerOnly),
        _ => null,
    };
}

// One choice in the contents panel's scope filter. A null Scope means "don't filter on it".
public sealed class ModListScopeFilter(string key, ModListEntryScope? scope, bool adviceOnly = false) : LocalizedViewModel
{
    public ModListEntryScope? Scope { get; } = scope;

    public bool AdviceOnly { get; } = adviceOnly;

    // Read on every get rather than captured, so choosing a language relabels the dropdown without
    // rebuilding it - which would drop the selection and reset the filter.
    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

// One choice in the contents panel's sort.
public sealed class ModListEntrySort(string key, ListSortDirection direction) : LocalizedViewModel
{
    public ListSortDirection Direction { get; } = direction;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

//
// The Mod lists page: what lists this install holds, what applying one would do, and applying it.
//
// Every decision this page makes is Core's - it captures, previews and applies through
// ModListService and renders what comes back. See [ModListApplier] for the order an apply runs in
// and why nothing is disabled until every download has worked.
//
public partial class ModListsViewModel : LocalizedViewModel
{
    private readonly ModListService _service = AppServices.ModListWorkflow;

    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private ModListPreview? _preview;

    public ObservableCollection<ModListRowViewModel> Lists { get; } = [];

    //
    // What the selected list names - and the edit buffer Save writes back, which is why NOTHING
    // filters or reorders this collection. The panel below shows EntriesView instead.
    //
    // Filtering this directly would mean Save wrote out only the rows that happened to be visible,
    // silently dropping every entry the search box was hiding. That is a data-loss bug wearing the
    // costume of a UI feature, and the two-collection split is what makes it impossible.
    //
    public ObservableCollection<ModListEntryRowViewModel> Entries { get; } = [];

    //
    // What the contents panel actually renders: the same rows, searched, filtered by scope and
    // sorted, without the underlying buffer ever moving.
    //
    public ICollectionView EntriesView { get; }

    public ModListsViewModel()
    {
        EntriesView = new CollectionViewSource { Source = Entries }.View;
        EntriesView.Filter = Matches;

        _scopeFilter = ScopeFilters[0];
        _entrySort = EntrySorts[0];

        ApplySort();
    }

    public ObservableCollection<ModListActionRowViewModel> PlanRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionIsEditable))]
    [NotifyPropertyChangedFor(nameof(SelectionIsImported))]
    [NotifyPropertyChangedFor(nameof(SelectionIsFromServer))]
    [NotifyPropertyChangedFor(nameof(SelectionIsFromSpMod))]
    [NotifyPropertyChangedFor(nameof(SelectionIsActive))]
    [NotifyPropertyChangedFor(nameof(SelectionDetail))]
    [NotifyPropertyChangedFor(nameof(ShowContents))]
    [NotifyPropertyChangedFor(nameof(CanEditList))]
    [NotifyPropertyChangedFor(nameof(CanUseStoredList))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddModsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForkCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshFromServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshFromSpModCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenOnSpModCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    //
    // Both of these were missing, and a command whose CanExecute is never re-raised is evaluated
    // once - at construction, with nothing selected - and stays disabled for the life of the page.
    // That is exactly how Publish came out permanently greyed.
    //
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyCanExecuteChangedFor(nameof(TakeAdviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(KeepAdviceCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartRenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshVersionsCommand))]
    private ModListRowViewModel? _selected;

    [ObservableProperty]
    private string _newListName = string.Empty;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusMessage = Strings.ModLists_NoneFollowed;

    public bool HasStatus => IsBusy || !string.IsNullOrWhiteSpace(StatusMessage);

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCardRows))]
    private bool _compactRows = new SettingsService().Load().ModListsCompactRows;

    public bool ShowCardRows => !CompactRows;

    partial void OnCompactRowsChanged(bool value)
    {
        var settings = new SettingsService();
        var current = settings.Load();
        current.ModListsCompactRows = value;
        settings.Save(current);
    }

    [RelayCommand]
    private void UseCardRows() => CompactRows = false;

    [RelayCommand]
    private void UseCompactRows() => CompactRows = true;

    //
    // The one undo point: how the install stood before the last apply. Null when nothing has been
    // applied, and cleared once used - a revert is the end of the chain, not another step in it.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRevert))]
    [NotifyPropertyChangedFor(nameof(CanRevert))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private string? _revertLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    [NotifyPropertyChangedFor(nameof(ShowContents))]
    private string? _planSummary;

    // How many adds and removes are waiting to be saved. Zero means what is on screen is what is
    // in the file.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(CanUseStoredList))]
    [NotifyPropertyChangedFor(nameof(UnsavedLabel))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    private int _unsavedCount;

    // Shown when the selected list was captured on a different SPT version than this install.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersionWarning))]
    private string? _versionWarning;

    // Monitor mode (§9): said before an apply whose installs and updates will only be downloaded.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadOnlyNotice))]
    private string? _downloadOnlyNotice;

    public bool HasDownloadOnlyNotice => DownloadOnlyNotice is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRevert))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshFromServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportFromSpModCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshFromSpModCommand))]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private bool _isBusy;

    public bool HasSelection => Selected is not null;

    public bool SelectionIsEditable => Selected?.IsEditable == true;

    public bool SelectionIsImported => Selected is not null && !Selected.IsEditable;

    // A list a SERVER handed this install - the only kind there is anywhere to refresh it FROM.
    public bool SelectionIsFromServer => Selected?.IsFromServer == true;

    public bool SelectionIsFromSpMod => Selected?.IsFromSpMod == true;

    public bool SelectionIsActive => Selected?.IsActive == true;

    // The same one-line summary the left-hand row shows, repeated under the name so the detail
    // pane says what it is without the eye having to go back to the list.
    public string SelectionDetail => Selected?.Detail ?? string.Empty;

    public bool HasLists => Lists.Count > 0;

    public bool HasRevert => RevertLabel is not null;

    public bool CanRevert => HasRevert && !IsBusy;

    public bool HasPlan => PlanSummary is not null;

    public bool HasVersionWarning => VersionWarning is not null;

    //
    // The contents panel and the plan share one cell and swap. Previewing answers a different
    // question - what applying this would do, rather than what it says - and two long lists at
    // once is the congestion this page was corrected for once already.
    //
    public bool ShowContents => HasSelection && !HasPlan;

    //
    // Edits are held here until Save, and Save writes the list and nothing else.
    //
    // The split is the point: saving changes what the list *says*, applying changes what is on
    // disk. Nothing on this page installs, enables or disables a mod except Apply.
    //
    public bool HasUnsavedChanges => UnsavedCount > 0;

    // Preview and Apply work off the stored list, so they wait for the edits to be stored. Letting
    // them run against a list that no longer matches what is on screen is the one confusing state
    // an editable list can get into.
    public bool CanEditList => SelectionIsEditable;

    public bool CanUseStoredList => HasSelection && !HasUnsavedChanges;

    public string UnsavedLabel => Strings.ModLists_Unsaved(UnsavedCount);

    partial void OnSelectedChanged(ModListRowViewModel? value)
    {
        var dropped = UnsavedCount;

        EditName = value?.Name ?? string.Empty;
        IsRenaming = false;
        StatusMessage = string.Empty;

        //
        // The search is about one list's contents, so it does not follow you to the next one -
        // landing on a list showing "0 of 23 mods" because of a search typed three lists ago reads
        // as a broken page. The scope filter and the sort DO carry over: those are how you want to
        // look at lists in general, not at this one.
        //
        EntrySearch = "";

        ShowEntries();
        ClearPlan();

        // Switching lists drops whatever was not saved. Said out loud rather than silently - nothing
        // here is destructive, but work disappearing without a word is its own kind of bug.
        if (dropped > 0)
        {
            StatusMessage = Strings.ModLists_Dropped(dropped);
        }
    }

    //
    // Fills the contents panel from the selected list.
    //
    // Read off the row's own ModList rather than the store: Refresh has just loaded it, and going
    // back for a second read would let the panel and the row disagree about the same list.
    //
    //
    // The entry rows and the plan rows are records carrying text that was composed when they were
    // built, so a language change cannot reach them the way it reaches a computed property. They
    // are rebuilt instead - both from what is already in memory, no disk and no network.
    //
    // The list rows on the left need nothing here: their Detail is computed, and each row is a
    // LocalizedViewModel in its own right.
    //
    protected internal override void RefreshText()
    {
        base.RefreshText();

        ShowEntries();

        if (_preview is { } preview) ShowPlan(preview);
    }

    private void ClearPlan()
    {
        _preview = null;
        PlanRows.Clear();
        DependencyRows.Clear();
        _dependencyOffers = null;
        DependencyProblem = null;
        DependencyNoVersionNote = null;
        OnPropertyChanged(nameof(HasDependencyRows));
        OnPropertyChanged(nameof(CanAddDependenciesToList));
        PlanChips.Clear();
        PlanSummary = null;
        VersionWarning = null;
        DownloadOnlyNotice = null;
        SetApplyBlockers([]);
        UpdateBlockerWatch();
        NotifyPlanWording();
    }

    [RelayCommand]
    public void Refresh(Guid? select = null)
    {
        var data = AppServices.ModLists.Load();
        var keep = select ?? Selected?.Id;

        Lists.Clear();

        foreach (var list in data.Lists.OrderByDescending(l => l.UpdatedAt))
            Lists.Add(new ModListRowViewModel(
                list,
                data.ActiveListId == list.Id,
                data.ActiveServerListId == list.Id,
                data.PublishedListId == list.Id));

        OnPropertyChanged(nameof(HasLists));

        RevertLabel = _service.PendingRevert() is { } snapshot
            ? Text(Strings.ModLists_UndoLabelFormat, snapshot.Name)
            : null;

        Selected = Lists.FirstOrDefault(l => l.Id == keep);

        // Setting Selected only rebuilds the panel when it actually changed, and a refresh after an
        // edit hands back a row for the same list - same id, new contents.
        ShowEntries();

        var active = Lists.FirstOrDefault(l => l.IsActive);
        if (active is not null && Selected?.Id == active.Id) StatusMessage = Text(Strings.ModLists_FollowingFormat, active.Name);
    }

    [RelayCommand]
    private void CreateEmpty()
    {
        if (ModListNameWindow.AskForEmpty() is not { } name) return;

        var list = _service.CreateEmpty(name);
        Refresh(list.Id);
        StatusMessage = Text(Strings.ModLists_CreatedEmptyFormat, list.Name);
    }

    [RelayCommand]
    private async Task CaptureNewAsync()
    {
        if (ModListNameWindow.AskForCapture() is not { } name) return;

        NewListName = name;
        await CaptureAsync();
    }

    // Saves what's installed right now as a new list.
    [RelayCommand]
    private async Task CaptureAsync()
    {
        var name = NewListName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = Strings.ModLists_NameFirst;
            return;
        }

        await RunAsync(async () =>
        {
            var captured = await _service.CaptureAsync(name);

            if (captured is null)
            {
                StatusMessage = AppMessages.NoSptInstallFolder;
                return;
            }

            NewListName = string.Empty;
            _announceAdviceFor = captured.Id;
            Refresh(captured.Id);
            StatusMessage = Strings.ModLists_Captured(
                captured.Entries.Count, captured.Entries.Count, captured.Name);
            _announceAfter = StatusMessage;
        });
    }
}
