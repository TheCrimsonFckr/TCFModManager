using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class ModListsViewModel
{
    //
    // The filter matches the scope EXACTLY rather than "contains this flag", which is what makes it
    // useful for the job it exists for: finding the entries you have already pruned, or the ones
    // still carrying the capture default. "Everything a headless takes" is a different question and
    // the pre-launch check is what answers it.
    //
    public IReadOnlyList<ModListScopeFilter> ScopeFilters { get; } =
    [
        new(nameof(Strings.ModLists_ScopeFilterAll), null),
        new(ModListScopes.Key(ModListEntryScope.Everyone)!, ModListEntryScope.Everyone),
        new(ModListScopes.Key(ModListScopes.ServerAndClients)!, ModListScopes.ServerAndClients),
        new(ModListScopes.Key(ModListScopes.ClientsAndHeadless)!, ModListScopes.ClientsAndHeadless),
        new(ModListScopes.Key(ModListEntryScope.Client)!, ModListEntryScope.Client),
        new(ModListScopes.Key(ModListEntryScope.Headless)!, ModListEntryScope.Headless),
        new(ModListScopes.Key(ModListEntryScope.Server)!, ModListEntryScope.Server),
        new(nameof(Strings.ModLists_ScopeFilterAdvice), null, adviceOnly: true),
    ];

    public IReadOnlyList<ModListScopeFilter> ScopeChoices { get; } =
    [
        new(ModListScopes.Key(ModListEntryScope.Everyone)!, ModListEntryScope.Everyone),
        new(ModListScopes.Key(ModListScopes.ClientsAndHeadless)!, ModListScopes.ClientsAndHeadless),
        new(ModListScopes.Key(ModListEntryScope.Client)!, ModListEntryScope.Client),
        new(ModListScopes.Key(ModListEntryScope.Headless)!, ModListEntryScope.Headless),
        new(ModListScopes.Key(ModListEntryScope.Server)!, ModListEntryScope.Server),
        new(ModListScopes.Key(ModListScopes.ServerAndClients)!, ModListScopes.ServerAndClients),
    ];

    public IReadOnlyList<ModListEntrySort> EntrySorts { get; } =
    [
        new(nameof(Strings.ModLists_SortNameAscending), ListSortDirection.Ascending),
        new(nameof(Strings.ModLists_SortNameDescending), ListSortDirection.Descending),
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
        if (ScopeFilter?.AdviceOnly == true && !row.NeedsAttention) return false;

        var search = EntrySearch?.Trim();
        if (string.IsNullOrEmpty(search)) return true;

        return row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Entry.Folders.Any(f => f.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    // Whether the LIST has anything on it, which is a different question from whether anything is
    // showing - a filter that matches nothing must not read as an empty list.
    public bool HasEntries => Entries.Count > 0;

    public int VisibleEntryCount => EntriesView.Cast<object>().Count();

    public bool IsFiltered =>
        ScopeFilter?.Scope is not null || ScopeFilter?.AdviceOnly == true || !string.IsNullOrWhiteSpace(EntrySearch);

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

            if (IsFiltered)
            {
                return Text(
                    Strings.ModLists_EntriesHeaderFilteredFormat, VisibleEntryCount, total);
            }

            return Text(Strings.ModLists_EntriesHeaderFormat, total);
        }
    }

    private void ShowEntries()
    {
        Entries.Clear();

        _titles = CatalogTitles();
        _keptKeys = _kept.Load();
        _mods = AppServices.ModCache.AllMods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        RefreshPinLookup();

        if (Selected is { } row)
        {
            foreach (var entry in ModListEntries.Sorted(row.List.Entries)) Entries.Add(Row(entry));
        }

        // Whatever was in the buffer is gone; the panel now shows the file again.
        UnsavedCount = 0;
        Notify();

        LoadAdvice();
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

        var row = new ModListEntryRowViewModel(entry, name, EntryDetail(entry, name), IsPinnedHere(entry))
        {
            ScopeUnchecked = Selected?.List.SpModSource?.IsScopeUnchecked(entry) == true,
        };
        if (entry.ModId is not { } modId) return row;

        row = WithAdvice(row, AppServices.HeadlessAdvice.Known);

        var url = SpModListImport.PageFor(entry.IsAddon, modId);

        if (entry.IsAddon)
        {
            return AppServices.Addons.ById(modId) is { } addon
                ? row with
                {
                    Thumbnail = addon.Thumbnail,
                    Teaser = addon.Teaser,
                    Facts = CardFacts(addon.Owner?.Name, addon.Downloads),
                    Url = addon.DetailUrl ?? url,
                }
                : row with { Url = url };
        }

        if (!_mods.TryGetValue(modId, out var mod)) return row with { Url = url };

        return row with
        {
            Thumbnail = mod.Thumbnail,
            Teaser = mod.Teaser,
            Category = mod.Category?.Title,
            Facts = CardFacts(mod.Owner?.Name, mod.Downloads),
            SptBadge = SptBadgeFor(mod, entry),
            Url = mod.DetailUrl ?? url,
            Mod = mod,
        };
    }

    private Dictionary<int, Mod> _mods = [];

    private readonly HeadlessAdviceDismissals _kept = new();

    private HashSet<string> _keptKeys = [];

    private static int? ParentOf(int addonId) => AppServices.Addons.ById(addonId)?.ModId;

    private static HeadlessSubject? SubjectOf(ModListEntry entry) => entry.ModId is not { } id
        ? null
        : entry.IsAddon ? HeadlessSubject.ForAddon(id) : HeadlessSubject.ForMod(id);

    private static HeadlessAdviceForEntry? AdviceFor(ModListEntry entry, Func<HeadlessSubject, HeadlessAdvice?> known)
    {
        if (entry.ModId is not { } id) return null;

        if (!entry.IsAddon) return known(HeadlessSubject.ForMod(id)) is { } own ? new(own, FromParent: false) : null;

        var parent = ParentOf(id) is { } parentId ? known(HeadlessSubject.ForMod(parentId)) : null;
        return HeadlessAdviceScopes.ForAddon(known(HeadlessSubject.ForAddon(id)), parent);
    }

    private ModListEntryRowViewModel WithAdvice(ModListEntryRowViewModel row, Func<HeadlessSubject, HeadlessAdvice?> known)
    {
        if (AdviceFor(row.Entry, known) is not { Advice.Verdict: not HeadlessVerdict.None } found) return row;

        var kept = found.Advice.Verdict == HeadlessVerdict.Avoid
            && Selected is { } list
            && SubjectOf(row.Entry) is { } subject
            && _keptKeys.Contains(HeadlessAdviceDismissals.Key(list.Id, subject, found.Advice.Verdict));

        return row with
        {
            Advice = found.Advice,
            AdviceFromParent = found.FromParent,
            AdviceKept = kept,
            AdviceScope = kept ? null : HeadlessAdviceScopes.Suggest(found.Advice.Verdict, row.Entry.EffectiveScope),
        };
    }

    private static string? CardFacts(string? author, int? downloads)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(author)) parts.Add(Text(Strings.Browse_ByAuthorFormat, author));
        if (downloads is { } count) parts.Add(Text(Strings.Common_DownloadsFormat, count));
        return parts.Count > 0 ? string.Join(Strings.Common_FactSeparator, parts) : null;
    }

    //
    // The SPT range of the version the entry names - the pinned build, or the version it was last
    // read at - as the catalog knows it. Read through SptVersionRangeFormatter, so a constraint shows
    // as the releases it covers rather than its raw operators.
    //
    private static string? SptBadgeFor(Mod mod, ModListEntry entry)
    {
        var versions = mod.Versions ?? [];
        var version = versions.FirstOrDefault(v => entry.VersionId is { } id && v.Id == id)
            ?? versions.FirstOrDefault(v => string.Equals(v.Version, entry.Version, StringComparison.OrdinalIgnoreCase));

        var range = SptVersionRangeFormatter.Format(version?.SptVersionConstraint);
        return range is null ? null : Text(Strings.ModLists_EntrySptBadgeFormat, range);
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

        if (entry.IsAddon) parts.Add(Strings.ModLists_EntryAddon);
        if (entry is { IsAddon: true, FitOverride: true }) parts.Add(Strings.ModLists_EntryInstalledAnyway);

        //
        // The folders on disk this entry covers.
        //
        // The name above it is the sp-mod.com listing title wherever one matched, which is often
        // nothing like what the mod calls its own folder - and the folder is what you go looking for
        // when something has to be sorted out by hand. Absent for a mod added from the catalog that
        // nobody here has installed, which is the honest answer for one.
        //
        if (InstalledAs(entry, name) is { } folders) parts.Add(Text(Strings.ModLists_EntryInstalledAsFormat, folders));

        if (!entry.IsResolved) parts.Add(Strings.ModLists_EntryNotOnForge);
        else if (entry.Version is { } version)
        {
            parts.Add(entry.IsPinned
                ? Text(Strings.ModLists_EntryVersionFormat, version)
                : Text(Strings.ModLists_EntryVersionUnlockedFormat, version));
        }
        else parts.Add(Strings.ModLists_EntryNewestPublished);

        return string.Join(Strings.Common_FactSeparator, parts);
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

        var folders = string.Join(Strings.Common_ListSeparator, entry.Folders);

        // Compared against the name actually shown above it, not the stored one: a mod the catalog
        // never matched is already named after its folder, and repeating it says nothing.
        return string.Equals(folders, name.Trim(), StringComparison.OrdinalIgnoreCase) ? null : folders;
    }
}
