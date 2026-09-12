using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One saved list in the left-hand list.
public sealed partial class ModListRowViewModel(
    ModList list, bool isActive, bool isActiveServer = false, bool isPublished = false)
    : ObservableObject
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

    //
    // The list THIS machine serves to its own clients. Badged because among a dozen personal lists
    // the one that other people are being handed is the one you must not edit carelessly.
    //
    // Passed in from ModListData.PublishedListId, like the two above. It was a flag on the list
    // itself and the flag never survived a save - see ModListData.PublishedListId.
    //
    public bool IsPublished { get; } = isPublished;

    public string Detail
    {
        get
        {
            var parts = new List<string> { List.Entries.Count == 1 ? "1 mod" : $"{List.Entries.Count} mods" };

            if (List.IsSnapshot) parts.Add("snapshot");

            if (IsPublished) parts.Add("published to this server");

            parts.Add(List.Origin switch
            {
                ModListOrigin.Imported => List.Source is null ? "imported" : $"from {List.Source}",
                ModListOrigin.Server => List.Source is null ? "from a server" : $"from {List.Source}",
                _ => "made here",
            });

            if (List.Revision > 1) parts.Add($"revision {List.Revision}");

            return string.Join(" · ", parts);
        }
    }
}

// One line of a plan, as the diff shows it.
public sealed record ModListActionRowViewModel(string Kind, string Name, string Detail, int Order, ModListAction Action)
{
    public bool CanPin => Action is { Kind: ModListActionKind.Disable, Installed: not null };

    public bool CanUnpin => Action is { Kind: ModListActionKind.Pinned, Installed: not null };
}

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
    public string ScopeLabel => ModListScopes.Label(Entry.EffectiveScope);
}

//
// The names the page puts on a scope, and the order the row button cycles them in.
//
// Kept in one place because the chip, the filter dropdown, the row's detail line and the message
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
    public static string Label(ModListEntryScope scope) => scope switch
    {
        ModListEntryScope.Everyone => "Server + Client + Headless",
        ServerAndClients => "Server + Client",
        ClientsAndHeadless => "Client + Headless",
        ModListEntryScope.Client => "Client only",
        ModListEntryScope.Headless => "Headless only",
        ModListEntryScope.Server => "Server only",

        // Not reachable from the button, but a hand-edited file can hold any combination and a row
        // that refuses to describe itself is worse than one that spells the flags out.
        _ => ModListEntryScopeConverter.Name(scope),
    };

    //
    // Where the button goes next.
    //
    // Ordered so the ONE change anybody makes often is one click: capture tags a plugin
    // Client + Headless, and the whole point of the feature is saying "actually, players only" for
    // the HUD mods. That step comes first. The full set leads into it for the same reason - a mod
    // with both halves starts there and is pruned the same way.
    //
    // Server + Client sits at the far end for the opposite reason: it is the answer to a question
    // most lists never ask - a mod both halves of the game need and the headless does not.
    //
    public static ModListEntryScope Next(ModListEntryScope scope) => scope switch
    {
        ModListEntryScope.Everyone => ClientsAndHeadless,
        ClientsAndHeadless => ModListEntryScope.Client,
        ModListEntryScope.Client => ModListEntryScope.Headless,
        ModListEntryScope.Headless => ModListEntryScope.Server,
        ModListEntryScope.Server => ServerAndClients,
        _ => ModListEntryScope.Everyone,
    };
}

// One choice in the contents panel's scope filter. A null Scope means "don't filter on it".
public sealed record ModListScopeFilter(string Label, ModListEntryScope? Scope);

// One choice in the contents panel's sort.
public sealed record ModListEntrySort(string Label, ListSortDirection Direction);

//
// The Mod lists page: what lists this install holds, what applying one would do, and applying it.
//
// Every decision this page makes is Core's - it captures, previews and applies through
// ModListService and renders what comes back. See [ModListApplier] for the order an apply runs in
// and why nothing is disabled until every download has worked.
//
public partial class ModListsViewModel : ObservableObject
{
    private readonly ModListService _service = AppServices.ModListWorkflow;

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

    //
    // The filter matches the scope EXACTLY rather than "contains this flag", which is what makes it
    // useful for the job it exists for: finding the entries you have already pruned, or the ones
    // still carrying the capture default. "Everything a headless takes" is a different question and
    // the pre-launch check is what answers it.
    //
    public IReadOnlyList<ModListScopeFilter> ScopeFilters { get; } =
    [
        new("All scopes", null),
        new(ModListScopes.Label(ModListEntryScope.Everyone), ModListEntryScope.Everyone),
        new(ModListScopes.Label(ModListScopes.ServerAndClients), ModListScopes.ServerAndClients),
        new(ModListScopes.Label(ModListScopes.ClientsAndHeadless), ModListScopes.ClientsAndHeadless),
        new(ModListScopes.Label(ModListEntryScope.Client), ModListEntryScope.Client),
        new(ModListScopes.Label(ModListEntryScope.Headless), ModListEntryScope.Headless),
        new(ModListScopes.Label(ModListEntryScope.Server), ModListEntryScope.Server),
    ];

    public IReadOnlyList<ModListEntrySort> EntrySorts { get; } =
    [
        new("Name A-Z", ListSortDirection.Ascending),
        new("Name Z-A", ListSortDirection.Descending),
    ];

    [ObservableProperty]
    private ModListScopeFilter _scopeFilter;

    [ObservableProperty]
    private ModListEntrySort _entrySort;

    [ObservableProperty]
    private string _entrySearch = "";

    partial void OnScopeFilterChanged(ModListScopeFilter value) => RefreshView();

    partial void OnEntrySearchChanged(string value) => RefreshView();

    partial void OnEntrySortChanged(ModListEntrySort value)
    {
        ApplySort();
        Notify();
    }

    //
    // Sorted on the name the row DISPLAYS, not the one the entry stores.
    //
    // Those differ often enough to matter: an entry captured before listing titles were resolved
    // stores the folder name, so a list sorted on stored names shows "Item Value Watermark" filed
    // under A for "acidphantasm-itemvaluewatermark". Across 76 rows that reads as no order at all.
    //
    private void ApplySort()
    {
        EntriesView.SortDescriptions.Clear();
        EntriesView.SortDescriptions.Add(new SortDescription(nameof(ModListEntryRowViewModel.Name), EntrySort.Direction));
    }

    private void RefreshView()
    {
        EntriesView.Refresh();
        Notify();
    }

    //
    // Matched against the displayed name, the stored name and the folders on disk - the three things
    // a row can be recognised by, and all three are visible on it. Searching only the title would
    // miss the case the folder line exists for: knowing a mod by the folder it drops into.
    //
    private bool Matches(object item)
    {
        if (item is not ModListEntryRowViewModel row) return false;

        if (ScopeFilter?.Scope is { } scope && row.Entry.EffectiveScope != scope) return false;

        var search = EntrySearch?.Trim();
        if (string.IsNullOrEmpty(search)) return true;

        return row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Entry.Folders.Any(f => f.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    public ObservableCollection<ModListActionRowViewModel> PlanRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionIsEditable))]
    [NotifyPropertyChangedFor(nameof(SelectionIsImported))]
    [NotifyPropertyChangedFor(nameof(SelectionIsFromServer))]
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
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    //
    // Both of these were missing, and a command whose CanExecute is never re-raised is evaluated
    // once - at construction, with nothing selected - and stays disabled for the life of the page.
    // That is exactly how Publish came out permanently greyed.
    //
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    [NotifyCanExecuteChangedFor(nameof(CycleScopeCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshVersionsCommand))]
    private ModListRowViewModel? _selected;

    [ObservableProperty]
    private string _newListName = string.Empty;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "No mod list is being followed.";

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRevert))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshFromServerCommand))]
    private bool _isBusy;

    public bool HasSelection => Selected is not null;

    public bool SelectionIsEditable => Selected?.IsEditable == true;

    public bool SelectionIsImported => Selected is not null && !Selected.IsEditable;

    // A list a SERVER handed this install - the only kind there is anywhere to refresh it FROM.
    public bool SelectionIsFromServer => Selected?.IsFromServer == true;

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

    // Whether the LIST has anything on it, which is a different question from whether anything is
    // showing - a filter that matches nothing must not read as an empty list.
    public bool HasEntries => Entries.Count > 0;

    public int VisibleEntryCount => EntriesView.Cast<object>().Count();

    public bool IsFiltered =>
        ScopeFilter?.Scope is not null || !string.IsNullOrWhiteSpace(EntrySearch);

    // A list with entries, a filter on, and nothing matching it. Its own state because the advice
    // is different: nothing is wrong, and the fix is to widen the filter rather than add mods.
    public bool HasNoMatches => HasEntries && VisibleEntryCount == 0;

    //
    // Says both numbers while a filter is on. "12 mods on this list" over a list of 76 is not a
    // smaller list, it is a hidden one, and the count is exactly where that gets misread.
    //
    public string EntriesHeader
    {
        get
        {
            var total = Entries.Count;

            if (IsFiltered) return $"{VisibleEntryCount} of {Mods(total)} on this list";

            return $"{Mods(total)} on this list";
        }
    }

    private static string Mods(int count) => count == 1 ? "1 mod" : $"{count} mods";

    public string UnsavedLabel => UnsavedCount == 1 ? "1 unsaved change" : $"{UnsavedCount} unsaved changes";

    partial void OnSelectedChanged(ModListRowViewModel? value)
    {
        var dropped = UnsavedCount;

        EditName = value?.Name ?? string.Empty;

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
        if (dropped > 0) StatusMessage = $"Dropped {dropped} unsaved change(s) - they were never written to the list.";
    }

    //
    // Fills the contents panel from the selected list.
    //
    // Read off the row's own ModList rather than the store: Refresh has just loaded it, and going
    // back for a second read would let the panel and the row disagree about the same list.
    //
    private void ShowEntries()
    {
        Entries.Clear();

        _titles = CatalogTitles();
        RefreshPinLookup();

        if (Selected is { } row)
        {
            foreach (var entry in ModListEntries.Sorted(row.List.Entries)) Entries.Add(Row(entry));
        }

        // Whatever was in the buffer is gone; the panel now shows the file again.
        UnsavedCount = 0;
        Notify();
    }

    //
    // Listing titles by mod id, rebuilt whenever the panel is.
    //
    // A list captured before entries carried a listing title stores the folder name, so without
    // this the row reads "acidphantasm-itemvaluewatermark" for a mod published as "Item Value
    // Watermark" - and then prints the folder underneath as well, saying the same thing twice.
    // Looking the title up here fixes the lists already on disk rather than only the next capture.
    //
    private Dictionary<(int Id, bool IsAddon), string> _titles = [];

    private static Dictionary<(int Id, bool IsAddon), string> CatalogTitles()
    {
        var titles = new Dictionary<(int, bool), string>();

        foreach (var mod in AppServices.ModCache.AllMods)
            if (!string.IsNullOrWhiteSpace(mod.Name)) titles.TryAdd((mod.Id, false), mod.Name!.Trim());

        // Addons are numbered in their own sequence, so the addon flag is part of the key.
        foreach (var addon in AppServices.Addons.AllAddons)
            if (!string.IsNullOrWhiteSpace(addon.Name)) titles.TryAdd((addon.Id, true), addon.Name!.Trim());

        return titles;
    }

    //
    // What the entry stores, unless the catalog knows the mod under a better name. An unresolved
    // entry, an addon the cache hasn't loaded and an empty catalog all fall back to the stored name.
    //
    private ModListEntryRowViewModel Row(ModListEntry entry)
    {
        var name = entry.ModId is { } id && _titles.TryGetValue((id, entry.IsAddon), out var title)
            ? title
            : entry.Name;

        return new ModListEntryRowViewModel(entry, name, EntryDetail(entry, name), IsPinnedHere(entry));
    }

    //
    // Whether this install has the entry's mod pinned against a list's disable sweep. Asked of the
    // entry's own folders and name, plus the folders the install record gives its mod id - an entry
    // added from the catalog carries no folders of its own.
    //
    private IReadOnlySet<string> _pins = new HashSet<string>();

    private Dictionary<(int Id, bool IsAddon), List<string>> _recordFolders = [];

    private void RefreshPinLookup()
    {
        _pins = AppServices.ModLists.GetPins();

        _recordFolders = _pins.Count == 0
            ? []
            : AppServices.InstallManifest.Load().Mods
                .GroupBy(r => (r.ModId, r.IsAddon))
                .ToDictionary(g => g.Key, g => g.SelectMany(r => r.Folders).ToList());
    }

    private bool IsPinnedHere(ModListEntry entry)
    {
        if (_pins.Count == 0) return false;

        var keys = entry.Folders.Append(entry.Name);

        if (entry.ModId is { } id && _recordFolders.TryGetValue((id, entry.IsAddon), out var folders))
            keys = keys.Concat(folders);

        return keys.Any(k => !string.IsNullOrWhiteSpace(k) && _pins.Contains(k.Trim().ToLowerInvariant()));
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(EntriesHeader));
        OnPropertyChanged(nameof(VisibleEntryCount));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    //
    // What one entry says about itself. The three states a list entry can be in are the whole
    // point of the line: a pinned version is fetched exactly, an unpinned one comes down at the
    // newest published, and one with no mod id at all is somebody's manual job.
    //
    private static string EntryDetail(ModListEntry entry, string name)
    {
        var parts = new List<string>();

        if (entry.IsAddon) parts.Add("addon");

        //
        // Said in the line rather than only as a chip, because this is the field that decides
        // whether somebody else's machine acts on the entry at all.
        //
        //
        // Only the scopes that mean somebody skips the entry are spelled out here. Everyone and
        // Client + Headless are the two the chip already says and nobody has to think about; the
        // rest change what a machine does, so they get a sentence.
        //
        if (entry.EffectiveScope == ModListEntryScope.Server)
            parts.Add("server only - players skip it");
        else if (entry.EffectiveScope == ModListEntryScope.Client)
            parts.Add("players only - a headless skips it");
        else if (entry.EffectiveScope == ModListEntryScope.Headless)
            parts.Add("headless only - players skip it");
        else if (entry.EffectiveScope == ModListScopes.ServerAndClients)
            parts.Add("server and players - a headless skips it");

        //
        // The folders on disk this entry covers.
        //
        // The name above it is the sp-mod.com listing title wherever one matched, which is often
        // nothing like what the mod calls its own folder - and the folder is what you go looking for
        // when something has to be sorted out by hand. Absent for a mod added from the catalog that
        // nobody here has installed, which is the honest answer for one.
        //
        if (InstalledAs(entry, name) is { } folders) parts.Add($"installed as {folders}");

        if (!entry.IsResolved) parts.Add("not on sp-mod.com - installed by hand");
        else if (entry.Version is { } version) parts.Add(entry.IsPinned ? $"version {version}" : $"version {version}, not locked");
        else parts.Add("newest published version");

        return string.Join(" · ", parts);
    }

    //
    // The folders to print under the name, or null when saying so would only repeat it.
    //
    // A list captured before entries carried the listing title stores the folder as the name, and a
    // mod the catalog never matched still does - printing "installed as" under either says the same
    // thing twice, which is what Chris saw.
    //
    private static string? InstalledAs(ModListEntry entry, string name)
    {
        if (entry.Folders.Count == 0) return null;

        var folders = string.Join(", ", entry.Folders);

        // Compared against the name actually shown above it, not the stored one: a mod the catalog
        // never matched is already named after its folder, and repeating it says nothing.
        return string.Equals(folders, name.Trim(), StringComparison.OrdinalIgnoreCase) ? null : folders;
    }

    private void ClearPlan()
    {
        _preview = null;
        PlanRows.Clear();
        PlanSummary = null;
        VersionWarning = null;
        ApplyCommand.NotifyCanExecuteChanged();
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
            ? $"Undo \"{snapshot.Name}\""
            : null;

        Selected = Lists.FirstOrDefault(l => l.Id == keep);

        // Setting Selected only rebuilds the panel when it actually changed, and a refresh after an
        // edit hands back a row for the same list - same id, new contents.
        ShowEntries();

        var active = Lists.FirstOrDefault(l => l.IsActive);
        if (active is not null) StatusMessage = $"Following \"{active.Name}\".";
    }

    // Saves what's installed right now as a new list.
    [RelayCommand]
    private async Task CaptureAsync()
    {
        var name = NewListName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Give the list a name first.";
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
            Refresh(captured.Id);
            StatusMessage = $"Captured {captured.Entries.Count} enabled mod(s) as \"{captured.Name}\".";
        });
    }

    // Works out what applying the selected list would do. Nothing moves and nothing downloads.
    [RelayCommand(CanExecute = nameof(CanUseStoredList))]
    private async Task PreviewAsync()
    {
        if (Selected is not { } row) return;

        await RunAsync(async () =>
        {
            var preview = await _service.PreviewAsync(row.List);

            if (preview is null)
            {
                StatusMessage = AppMessages.NoSptInstallFolder;
                return;
            }

            ShowPlan(preview);
        });
    }

    private void ShowPlan(ModListPreview preview)
    {
        _preview = preview;
        PlanRows.Clear();

        foreach (var row in Rows(preview.Plan)) PlanRows.Add(row);

        var plan = preview.Plan;

        var counts = new List<string>();
        void Count(int n, string label) { if (n > 0) counts.Add($"{n} {label}"); }

        Count(plan.Install.Count(), "to install");
        Count(plan.Update.Count(), "to update");
        Count(plan.Enable.Count(), "to enable");
        Count(plan.Disable.Count(), "to disable");
        Count(plan.Pinned.Count(), "pinned");
        Count(plan.Keep.Count(), "already right");
        Count(plan.Manual.Count(), "to fetch yourself");

        PlanSummary = plan.IsNoOp && plan.Manual.Count() == 0
            ? "This install already matches the list - nothing to do."
            : string.Join(", ", counts) + ".";

        VersionWarning = preview.List.SptVersion is { } captured
            && preview.Install.SptVersion is { } current
            && !string.Equals(captured, current, StringComparison.OrdinalIgnoreCase)
                ? $"This list was made on SPT {captured} and you're running {current}. Versions locked for one won't always work on the other."
                : null;

        ApplyCommand.NotifyCanExecuteChanged();
        StatusMessage = plan.RequiresGameClosed
            ? "Close SPT before applying - disabling a mod can't happen while it's running."
            : $"Previewed \"{preview.List.Name}\".";
    }

    private static IEnumerable<ModListActionRowViewModel> Rows(ModListPlan plan) =>
        plan.Actions
            .Select(a => new ModListActionRowViewModel(Label(a), a.Name, Detail(a), Order(a.Kind), a))
            .OrderBy(r => r.Order)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

    private static string Label(ModListAction action) => action.Kind switch
    {
        ModListActionKind.Install => "Install",
        ModListActionKind.Update => action.IsRepair ? "Reinstall" : action.IsDowngrade ? "Downgrade" : "Update",
        ModListActionKind.Enable => action.NeedsUpdateAfterEnable
            ? action.IsRepair ? "Enable + reinstall" : "Enable + update"
            : "Enable",
        ModListActionKind.Disable => "Disable",
        ModListActionKind.Pinned => "Pinned",
        ModListActionKind.Manual => "Fetch yourself",
        _ => "Unchanged",
    };

    //
    // Prefixed with "Addon - " where it applies: two rows can otherwise read identically while
    // meaning different things, since an addon and a mod are numbered separately and an addon's
    // name rarely says what it attaches to.
    //
    private static string Detail(ModListAction action)
    {
        var detail = DetailFor(action);
        if (!action.IsAddon) return detail;

        return detail.Length == 0 ? "Addon" : $"Addon - {detail}";
    }

    private static string DetailFor(ModListAction action) => action.Kind switch
    {
        ModListActionKind.Install => action.TargetVersion is null ? "newest published" : $"version {action.TargetVersion}",
        ModListActionKind.Update or ModListActionKind.Enable when action.IsRepair =>
            $"version {action.TargetVersion ?? action.InstalledVersion} is installed but files are missing",

        ModListActionKind.Update or ModListActionKind.Enable when action.TargetVersion is not null
            && action.InstalledVersion is not null && action.TargetVersion != action.InstalledVersion =>
            $"{action.InstalledVersion} to {action.TargetVersion}",
        ModListActionKind.Disable => "not on this list",
        ModListActionKind.Pinned => "not on this list - kept, you pinned it",
        ModListActionKind.Manual => "not on sp-mod.com - install it by hand",
        _ => action.InstalledVersion is null ? string.Empty : $"version {action.InstalledVersion}",
    };

    private static int Order(ModListActionKind kind) => kind switch
    {
        ModListActionKind.Install => 0,
        ModListActionKind.Update => 1,
        ModListActionKind.Enable => 2,
        ModListActionKind.Disable => 3,
        ModListActionKind.Pinned => 4,
        ModListActionKind.Manual => 5,
        _ => 6,
    };

    //
    // Pins or unpins the mod on a Disable or Pinned row, then plans the same list again so the row
    // and the summary say what an apply would now do.
    //
    [RelayCommand]
    private async Task TogglePinAsync(ModListActionRowViewModel? row)
    {
        if (row is not { Action.Installed: { } installed } || _preview is not { } preview) return;
        if (!row.CanPin && !row.CanUnpin) return;

        await RunAsync(async () =>
        {
            AppServices.ModLists.SetPinned(ModListPlanner.PinKeys(installed), pinned: row.CanPin);

            RefreshPinLookup();
            for (var i = 0; i < Entries.Count; i++) Entries[i] = Row(Entries[i].Entry);

            var replanned = await _service.PreviewAsync(preview.List);

            if (replanned is null)
            {
                ClearPlan();
                StatusMessage = AppMessages.NoSptInstallFolder;
                return;
            }

            ShowPlan(replanned);
            if (replanned.Plan.RequiresGameClosed) return;

            StatusMessage = row.CanPin
                ? $"Pinned \"{row.Name}\" - no list will set it aside."
                : $"Unpinned \"{row.Name}\".";
        });
    }

    private bool CanApply => !IsBusy && _preview is not null && !HasUnsavedChanges;

    //
    // Applies the previewed plan. The preview is reused rather than rebuilt, so what runs is what
    // was shown - a rescan in between would silently change it.
    //
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_preview is not { } preview) return;

        await RunAsync(async () =>
        {
            var result = await _service.ApplyAsync(preview, ModListPrompts.Default);

            if (result.Completed)
            {
                StatusMessage = Completed(result);
                ClearPlan();
                Refresh(preview.List.Id);
                return;
            }

            //
            // An apply that stopped part way has, at most, enabled some mods - nothing is ever
            // disabled unless every download worked. Putting those back leaves the install exactly
            // as it was found rather than in a state nobody asked for.
            //
            var undone = 0;

            if (result.Moves.Count > 0)
            {
                undone = ModDisableService.Revert(result.Moves, AppServices.SptEnvironment.InstallPath).Moved.Count;
            }

            StatusMessage = ModListProblems.Describe(result)
                + (undone > 0 ? $" Put {undone} mod(s) back the way they were." : string.Empty)
                + FailureDetail(result);

            ClearPlan();
            Refresh(preview.List.Id);
        });
    }

    private static string Completed(ModListApplyResult result)
    {
        var parts = new List<string>();
        void Count(int n, string label) { if (n > 0) parts.Add($"{n} {label}"); }

        Count(result.Fetched.Fetched.Count, "downloaded");
        Count(result.Enabled.Moved.Count, "enabled");
        Count(result.Disabled.Moved.Count, "disabled");

        var message = parts.Count == 0 ? "Applied - nothing needed changing." : "Applied: " + string.Join(", ", parts) + ".";

        var manual = result.Manual.Count;
        if (manual > 0) message += $" {manual} mod(s) still need installing by hand.";

        var failedMoves = result.Enabled.Failed.Count + result.Disabled.Failed.Count;
        if (failedMoves > 0) message += $" {failedMoves} couldn't be moved.";

        return message;
    }

    private static string FailureDetail(ModListApplyResult result) =>
        result.Fetched.Failed.Count == 0
            ? string.Empty
            : " " + string.Join("; ", result.Fetched.Failed.Take(3).Select(f => $"{f.ModName}: {f.Reason}"));

    //
    // Puts the install back the way it was before the last list was applied, then clears the undo
    // point. Normally moves mods without downloading anything, since the snapshot only names mods
    // that were installed at the time.
    //
    [RelayCommand(CanExecute = nameof(CanRevert))]
    private async Task RevertAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _service.RevertAsync(ModListPrompts.Default);

            if (result is null)
            {
                StatusMessage = "Nothing to undo.";
                Refresh();
                return;
            }

            if (result.Completed)
            {
                StatusMessage = "Put the install back the way it was before the last apply."
                    + $" {result.Enabled.Moved.Count} enabled, {result.Disabled.Moved.Count} disabled.";
            }
            else
            {
                if (result.Moves.Count > 0) ModDisableService.Revert(result.Moves, AppServices.SptEnvironment.InstallPath);
                StatusMessage = $"Couldn't undo. {ModListProblems.Describe(result)}";
            }

            ClearPlan();
            Refresh();
        });
    }

    //
    // Adds mods to the selected list by hand, from what is installed and from the catalog.
    //
    // Edits the panel, not the file: nothing is written until Save, and nothing on disk moves until
    // Apply. Those are three separate steps on purpose.
    //
    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private async Task AddModsAsync()
    {
        if (Selected is not { } row) return;

        await RunAsync(async () =>
        {
            var options = await _service.AddOptionsAsync();

            if (options.Installed.Count == 0 && options.Catalog.Count == 0)
            {
                StatusMessage = "Nothing to add from - no SPT install folder is set and the sp-mod.com catalog hasn't loaded yet.";
                return;
            }

            // The picker is handed what the panel holds, unsaved edits included, so a mod added a
            // minute ago and not yet saved still shows there as already on the list.
            var chosen = ModListAddModWindow.Pick(row.Name, [.. Entries.Select(e => e.Entry)], options);
            if (chosen.Count == 0) return;

            var added = 0;

            foreach (var entry in chosen)
            {
                if (Entries.Any(e => ModListEntries.SameMod(e.Entry, entry))) continue;

                Entries.Add(Row(entry));
                added++;
            }

            if (added == 0)
            {
                StatusMessage = "Nothing added - the list already names those mods.";
                return;
            }

            Notify();
            UnsavedCount += added;

            StatusMessage = $"Added {added} mod(s) to \"{row.Name}\". Save to write it to the list"
                + " - nothing is installed or enabled until you Apply.";
        });
    }

    //
    // Takes one mod off the panel. The mod stays installed and enabled whatever happens next: saving
    // changes what the list says, and only applying an Exclusive list afterwards sets it aside.
    //
    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void RemoveEntry(ModListEntryRowViewModel? entry)
    {
        if (Selected is not { } row || entry is null) return;
        if (!Entries.Remove(entry)) return;

        UnsavedCount++;
        Notify();

        StatusMessage = $"Took \"{entry.Name}\" off \"{row.Name}\". Save to write it to the list"
            + " - the mod stays installed and enabled either way.";
    }

    //
    // Moves one entry around the machines it is for.
    //
    // Capture infers this from where a mod's files land, which is right nearly always - this is for
    // the exception, and there are now two kinds. The originals: the Server Map mod and fika-server
    // sit in user\mods on the server, and a client told to install them is being sent on an errand
    // it cannot complete. The new one: nothing on disk separates a bot overhaul from a HUD widget,
    // so capture gives the headless every plugin and this is where the ones it does not need come
    // back off.
    //
    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void CycleScope(ModListEntryRowViewModel? entry)
    {
        if (Selected is null || entry is null) return;

        var index = Entries.IndexOf(entry);
        if (index < 0) return;

        var next = ModListScopes.Next(entry.Entry.EffectiveScope);

        // ModListEntry is a class with init-only properties, not a record, so this is a rebuild
        // rather than a `with`. Every field is carried across deliberately - a missed one here
        // would silently drop a version pin.
        var source = entry.Entry;

        Entries[index] = Row(new ModListEntry
        {
            Name = source.Name,
            ModId = source.ModId,
            IsAddon = source.IsAddon,
            VersionId = source.VersionId,
            Version = source.Version,
            Guid = source.Guid,
            Folders = [.. source.Folders],

            // Cycling back round to Everyone stores null - see ModListEntry.Scope - so the entry
            // ends up exactly as it was before anyone touched it, rather than carrying a value that
            // means what silence already meant.
            Scope = next,
        });
        UnsavedCount++;

        // The scope filter may no longer match this row, so the counts move even though the list
        // did not.
        Notify();

        StatusMessage = next switch
        {
            ModListEntryScope.Server =>
                $"\"{entry.Name}\" is now server only - a player applying this list will skip it entirely.",
            ModListScopes.ClientsAndHeadless =>
                $"\"{entry.Name}\" now goes to players and to a headless client.",
            ModListEntryScope.Client =>
                $"\"{entry.Name}\" is now players only - a headless client will skip it.",
            ModListEntryScope.Headless =>
                $"\"{entry.Name}\" is now headless only - players will skip it.",
            ModListScopes.ServerAndClients =>
                $"\"{entry.Name}\" now goes to the server and to players - a headless client will skip it.",
            _ => $"\"{entry.Name}\" now applies to every machine.",
        };
    }

    //
    // Marks this list as the one this machine serves, and writes it into the Server Map mod's config
    // folder so the server picks it up.
    //
    // One step rather than export-then-copy: the app already knows where that folder is, and a file
    // the operator has to move by hand is a file that ends up in the wrong place - which is exactly
    // what happened the first time. The server re-reads on the file's timestamp, so there is nothing
    // to restart.
    //
    [RelayCommand(CanExecute = nameof(CanPublish))]
    private void Publish()
    {
        if (Selected is not { } row) return;

        var installPath = new SettingsService().Load().SptInstallPath;

        if (!ServerMapConfigFolder.TryFind(installPath, out var directory))
        {
            StatusMessage = "This machine isn't running a server with the Server Map mod - there's"
                + " nowhere to publish to. The mod goes on the server, not here.";
            return;
        }

        try
        {
            //
            // Always the preferred name, never the list's own. A folder holding one arbitrarily
            // named list works, but two of them is ambiguous and the server then serves neither -
            // writing the name it prefers means republishing under a new list name replaces the old
            // file instead of sitting beside it.
            //
            var path = Path.Combine(directory, PublishedFileName);

            //
            // The revision moves HERE when the contents differ from what is already published, and
            // nowhere else on this path - see ModListPublication. Without it an edited list goes out
            // under the number its receivers already hold, and they never ask for it again.
            //
            var list = row.List;
            var bumped = false;

            if (ModListPublication.NeedsNewRevision(list, File.Exists(path) ? ModListFile.Load(path).List : null))
            {
                // Refused for a list somebody else wrote, whose numbering is theirs - the same guard
                // an apply is under. Publishing still writes the file.
                list = AppServices.ModLists.BumpRevision(row.Id) ?? list;
                bumped = list.Revision != row.List.Revision;
            }

            ModListFile.Save(list, path);
            AppServices.ModLists.SetPublished(row.Id);
            Refresh(row.Id);

            AppLog.Info("ServerMap", $"published \"{row.Name}\" revision {list.Revision} to {path}");

            StatusMessage = bumped
                ? $"Published \"{row.Name}\" as revision {list.Revision} - it had changed since the last"
                  + " publish, so connected clients will fetch it on their own."
                : $"Published \"{row.Name}\" (revision {list.Revision}) - the server serves it from now on.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Couldn't write the list to the server's config folder: {ex.Message}";
        }
    }

    //
    // A served list is not republishable: it came from somewhere else, and serving it back would
    // make this machine claim authorship of somebody else's list.
    //
    private bool CanPublish() => Selected is { List.Origin: not ModListOrigin.Server, List.IsSnapshot: false };

    // Matches PublishedModList.PreferredFileName on the server side.
    private const string PublishedFileName = "published.tcfmodlist";

    //
    // Re-reads the installed version of every mod on this list.
    //
    // The alternative was taking a mod off the list and putting it back, which is what re-captures
    // the version it has now - correct, and ridiculous once a list runs to seventy entries and an
    // update round has moved a dozen of them.
    //
    // It only changes versions. Scope stays exactly as set, entries for mods this machine does not
    // have are left alone, and nothing is written to the list - the changes land in the panel as
    // unsaved, so Save is still the thing that commits them and Discard still throws them away.
    //
    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task RefreshVersionsAsync()
    {
        if (Selected is not { } row) return;

        IsBusy = true;

        try
        {
            var result = await _service.RefreshVersionsAsync(Entries.Select(e => e.Entry));

            if (result is null)
            {
                StatusMessage = AppMessages.NoSptInstallFolder;
                return;
            }

            if (result.Changed.Count == 0)
            {
                StatusMessage = result.NotInstalled == Entries.Count
                    ? "None of the mods on this list are installed here, so there are no versions to read."
                    : $"Every mod on \"{row.Name}\" that is installed here already names the version you have.";
                return;
            }

            var index = 0;
            foreach (var entry in result.Entries) Entries[index++] = Row(entry);

            UnsavedCount += result.Changed.Count;
            Notify();

            // Named rather than counted: "12 versions updated" is not something anybody can check,
            // and the whole point of the button is to be able to see what it decided.
            var named = string.Join(", ", result.Changed
                .Take(5)
                .Select(c => $"{c.Name} {c.From ?? "unlocked"} -> {c.To ?? "unlocked"}"));

            var rest = result.Changed.Count > 5 ? $", and {result.Changed.Count - 5} more" : "";

            StatusMessage = $"Updated {Mods(result.Changed.Count)} to the version installed here:"
                + $" {named}{rest}. Save to keep it.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    //
    // Writes the panel to the list. This is the only thing on this page that changes a saved list's
    // contents, and it changes nothing else: no downloads, no folders moved, no revision.
    //
    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void Save()
    {
        if (Selected is not { } row) return;

        var count = Entries.Count;

        AppServices.ModLists.ReplaceEntries(row.Id, ModListEntries.Sorted(Entries.Select(e => e.Entry)));
        UnsavedCount = 0;
        Refresh(row.Id);

        StatusMessage = $"Saved \"{row.Name}\" - {count} mod(s). Nothing on disk changed;"
            + " Apply is what installs, enables and sets mods aside.";
    }

    // Throws the unsaved edits away and shows the list as it is stored.
    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void Discard()
    {
        if (Selected is not { } row) return;

        ShowEntries();
        StatusMessage = $"Put \"{row.Name}\" back the way it was saved.";
    }

    // Keeps the panel in the order a stored list holds - by name, the order capture writes.
    // Puts the list's contents back in view after a preview. Changes nothing either way.
    [RelayCommand]
    private void ClosePlan()
    {
        ClearPlan();
        StatusMessage = Selected is { } row ? $"Showing what \"{row.Name}\" names." : StatusMessage;
    }

    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void Rename()
    {
        if (Selected is not { } row) return;

        var name = EditName.Trim();
        if (name.Length == 0 || name == row.Name) return;

        AppServices.ModLists.Rename(row.Id, name);
        Refresh(row.Id);
        StatusMessage = $"Renamed to \"{name}\".";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Selected is not { } row) return;

        AppServices.ModLists.Delete(row.Id);
        Refresh();
        StatusMessage = $"Deleted \"{row.Name}\". Nothing on disk changed.";
    }

    // Imported and server lists are read-only; editing one starts a local copy that points back.
    [RelayCommand(CanExecute = nameof(SelectionIsImported))]
    private void Fork()
    {
        if (Selected is not { } row) return;

        var fork = AppServices.ModLists.Fork(row.Id, $"{row.Name} (mine)", DateTimeOffset.UtcNow);
        Refresh(fork.Id);
        StatusMessage = $"Made \"{fork.Name}\" - the original is untouched.";
    }

    //
    // Asks the server for its list again, whether or not the revision has moved.
    //
    // The revision cannot carry this on its own: it counts APPLIES, so an operator who edits the
    // list they publish and republishes it hands out different contents under the same number, and
    // the automatic path - which fetches only when the number moves - correctly declines to ask.
    // Deleting the held copy to make it re-download was the only way through, and deleting the list
    // to get the list is not a workflow.
    //
    // Here rather than only on the Server map page because this is where the list lives and where
    // somebody looking at a stale copy is standing. The fetch itself is the map page's, so both
    // routes store identically.
    //
    [RelayCommand(CanExecute = nameof(CanRefreshFromServer))]
    private async Task RefreshFromServerAsync()
    {
        if (Selected is not { IsFromServer: true } row) return;

        var gate = AppServices.ServerMap;

        if (!gate.IsConfigured)
        {
            StatusMessage = "This install has no server address set, so there is nowhere to ask."
                + " The Server map page is where it goes.";
            return;
        }

        IsBusy = true;

        try
        {
            var before = row.List.Entries.ToList();
            var fetched = await gate.FetchAndStoreAsync();

            if (fetched is null)
            {
                // The gate has already worded why, and its wording is the one the map page shows.
                StatusMessage = $"The list wasn't fetched. {gate.ListStatus}";
                return;
            }

            Refresh(fetched.Id);

            //
            // A different list entirely, which is what publishing a NEW list looks like from here.
            // The old one is left alone rather than quietly replaced - it is still a true record of
            // what that server was serving, and only the user can say whether they still want it.
            //
            if (fetched.Id != row.Id)
            {
                StatusMessage = $"{gate.ServerName} is now publishing a different list -"
                    + $" \"{fetched.Name}\" is saved here. \"{row.Name}\" is untouched.";
                return;
            }

            StatusMessage = DescribeRefresh(before, fetched);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefreshFromServer() => SelectionIsFromServer && !IsBusy;

    //
    // What the refresh actually brought back, named rather than counted - the whole reason for
    // pressing it is not being sure the copy in hand is the one being served, and "3 changes" does
    // not answer that.
    //
    private static string DescribeRefresh(List<ModListEntry> before, ModList after)
    {
        var added = after.Entries
            .Where(e => !ModListEntries.Contains(before, e))
            .Select(e => e.Name)
            .ToList();

        var removed = before
            .Where(b => !ModListEntries.Contains(after.Entries, b))
            .Select(b => b.Name)
            .ToList();

        // Version and scope, because those are the two things an operator changes without the list
        // getting longer or shorter, and the two a client is wrong about silently.
        var changed = after.Entries
            .Select(e => (Now: e, Was: before.FirstOrDefault(b => ModListEntries.SameMod(b, e))))
            .Where(p => p.Was is not null
                        && (p.Was.VersionId != p.Now.VersionId
                            || !string.Equals(p.Was.Version, p.Now.Version, StringComparison.OrdinalIgnoreCase)
                            || p.Was.EffectiveScope != p.Now.EffectiveScope))
            .Select(p => p.Now.Name)
            .ToList();

        if (added.Count == 0 && removed.Count == 0 && changed.Count == 0)
        {
            return $"\"{after.Name}\" is already what the server is publishing - nothing changed"
                + $" (revision {after.Revision}, {Mods(after.Entries.Count)}).";
        }

        var parts = new List<string>();
        if (added.Count > 0) parts.Add($"added {Named(added)}");
        if (removed.Count > 0) parts.Add($"removed {Named(removed)}");
        if (changed.Count > 0) parts.Add($"changed {Named(changed)}");

        return $"Refreshed \"{after.Name}\" from the server: {string.Join("; ", parts)}."
            + " Applying it is still a separate step.";
    }

    // Five names and then a count, so a wholesale change does not become a paragraph.
    private static string Named(List<string> names) =>
        names.Count <= 5
            ? string.Join(", ", names.Order(StringComparer.OrdinalIgnoreCase))
            : string.Join(", ", names.Order(StringComparer.OrdinalIgnoreCase).Take(5))
              + $" and {names.Count - 5} more";

    [RelayCommand]
    private void StopFollowing()
    {
        if (Selected?.IsActiveServer == true) AppServices.ModLists.SetActiveServer(null);
        else AppServices.ModLists.SetActive(null);
        Refresh();
        StatusMessage = "No mod list is being followed. Nothing on disk changed.";
    }

    // Writes a manifest, never mod files - "install mod 2426 at version 5", not somebody's archive.
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Export()
    {
        if (Selected is not { } row) return;

        var dialog = new SaveFileDialog
        {
            Title = "Share this mod list",
            Filter = ModListFile.FileFilter,
            FileName = ModListFile.SuggestedFileName(row.List),
            AddExtension = true,
            DefaultExt = ModListFile.Extension,
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            ModListFile.Save(row.List, dialog.FileName);
            StatusMessage = $"Saved \"{row.Name}\" - send that file to anyone running this app.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Couldn't write the file - {ex.Message}";
        }
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a shared mod list",
            Filter = ModListFile.FileFilter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        var read = ModListFile.Load(dialog.FileName);

        if (!read.Succeeded)
        {
            StatusMessage = $"Couldn't read that file - {read.Error}";
            return;
        }

        var list = read.List!;
        var existing = AppServices.ModLists.Find(list.Id);

        AppServices.ModLists.Add(list);
        Refresh(list.Id);

        StatusMessage = existing is null
            ? $"Imported \"{list.Name}\" - preview it to see what applying it would do."
            : $"Updated \"{list.Name}\" from revision {existing.Revision} to {list.Revision}.";
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (IsBusy) return;

        IsBusy = true;

        try
        {
            await work();
        }
        catch (ModInstallException ex)
        {
            // Reverting an apply touches the install, so it can be refused like any other move -
            // and ModInstallException's Message is the reason name, not a sentence.
            AppLog.Error("ModLists", ex.ToString());
            StatusMessage = ModInstallProblems.Describe(ex);
        }
        catch (Exception ex)
        {
            AppLog.Error("ModLists", ex.ToString());
            StatusMessage = $"Something went wrong - {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
