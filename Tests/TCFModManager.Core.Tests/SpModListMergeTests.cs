using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class SpModListMergeTests : IDisposable
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Friday = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory;
    private readonly ModListStore _store;

    public SpModListMergeTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TCFModManagerSpModMergeTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _store = new ModListStore(Path.Combine(_directory, "mod_lists.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static SpModListPage Page(string? name = "Fika night", string? spt = "4.1.6", params SpModListItem[] items) => new()
    {
        ListId = 4242,
        Source = new Uri("https://sp-mod.com/list/4242/fika-night"),
        Name = name,
        Author = "someone",
        UpdatedAt = Monday.AddDays(-1),
        SptVersion = spt,
        Pass = SpModListReadPass.Structural,
        Items = items,
        Notices = [],
    };

    private static SpModListItem Mod(int id, string version = "1.0.0", string? name = null) =>
        new(SpModListItemKind.Mod, id, name ?? $"Mod {id}", version);

    private static SpModListItem Addon(int id, int parent, string version = "2.0.0") =>
        new(SpModListItemKind.Addon, id, $"Addon {id}", version, parent);

    private static ModListEntry Entry(int id, string version, bool addon = false) =>
        new() { Name = $"Extra {id}", ModId = id, IsAddon = addon, Version = version };

    private static readonly SpModListItem[] Base = [Mod(1), Mod(2), Addon(1, parent: 2)];

    // ---- choices ----

    [Fact]
    public void ExcludedItemsAreLeftOutAndRemembered()
    {
        var list = Page(items: Base).ToModList("x", Monday, new SpModListChoices(["mod/2", "addon/1"], []));

        Assert.Equal([1], list.Entries.Select(e => e.ModId));
        Assert.Equal(["addon/1", "mod/2"], list.SpModSource!.Excluded);
    }

    [Fact]
    public void ModAndAddonRefsDoNotCollide()
    {
        // Mod 1 and addon 1 are different things; excluding one leaves the other.
        var list = Page(items: Base).ToModList("x", Monday, new SpModListChoices(["addon/1"], []));

        Assert.Contains(list.Entries, e => e.ModId == 1 && !e.IsAddon);
        Assert.DoesNotContain(list.Entries, e => e.ModId == 1 && e.IsAddon);
    }

    [Fact]
    public void AddedEntriesFollowThePageAndAreRemembered()
    {
        var parent = Entry(90, "3.0.0");
        var list = Page(items: Base).ToModList("x", Monday, new SpModListChoices([], [parent, Entry(1, "9.9.9")]));

        Assert.Equal([1, 2, 1, 90], list.Entries.Select(e => e.ModId));
        Assert.Equal("1.0.0", list.Entries[0].Version);
        Assert.Same(parent, list.Entries[^1]);
        Assert.Equal(["mod/90"], list.SpModSource!.Added);
    }

    [Fact]
    public void ARetargetCarriesItsSptVersion()
    {
        var list = Page(items: Base).ToModList("x", Monday,
            new SpModListChoices([], [], Retargeted: true, DependenciesAdded: true, SptVersion: "4.2.0"));

        Assert.Equal("4.2.0", list.SptVersion);
        Assert.True(list.SpModSource!.Retargeted);
        Assert.True(list.SpModSource.DependenciesAdded);
    }

    [Fact]
    public void TheStoredChoicesRebuildTheSameList()
    {
        var first = Page(items: Base).ToModList("x", Monday,
            new SpModListChoices(["mod/1"], [Entry(90, "3.0.0")], Retargeted: true, SptVersion: "4.2.0"));

        var again = Page(items: Base).ToModList("x", Friday, SpModListChoices.From(first));

        Assert.Equal(first.Entries.Select(e => (e.ModId, e.IsAddon, e.Version)), again.Entries.Select(e => (e.ModId, e.IsAddon, e.Version)));
        Assert.Equal("4.2.0", again.SptVersion);
        Assert.Equal(first.SpModSource!.Excluded, again.SpModSource!.Excluded);
        Assert.Equal(first.SpModSource.Added, again.SpModSource.Added);
        Assert.False(SpModListImport.Merge(first, again).HasChanges);
    }

    [Fact]
    public void AnAddedEntryThatLaterAppearsOnThePageIsThePagesNow()
    {
        var first = Page(items: Base).ToModList("x", Monday, new SpModListChoices([], [Entry(90, "3.0.0")]));

        var again = Page(items: [.. Base, Mod(90, "3.1.0")]).ToModList("x", Friday, SpModListChoices.From(first));

        var entry = Assert.Single(again.Entries, e => e.ModId == 90);
        Assert.Equal("3.1.0", entry.Version);
        Assert.Empty(again.SpModSource!.Added);
    }

    [Fact]
    public void AListWithNoSourceHasNoChoices()
    {
        var plain = new ModList { Id = Guid.NewGuid(), Name = "mine", CreatedAt = Monday };

        Assert.Same(SpModListChoices.None, SpModListChoices.From(plain));
    }

    // ---- merge and revision ----

    [Fact]
    public void AFirstImportIsNewAtRevisionOne()
    {
        var update = SpModListImport.Merge(null, Page(items: Base).ToModList("x", Monday));

        Assert.True(update.IsNew);
        Assert.True(update.HasChanges);
        Assert.Null(update.Diff);
        Assert.Equal(1, update.List.Revision);
    }

    [Fact]
    public void ReadingAnUnchangedPageKeepsTheRevision()
    {
        var stored = Page(items: Base).ToModList("x", Monday);
        stored.Revision = 3;

        var update = SpModListImport.Merge(stored, Page(items: Base).ToModList("x", Friday));

        Assert.False(update.IsNew);
        Assert.False(update.HasChanges);
        Assert.Equal(3, update.List.Revision);
        Assert.Equal(Monday, update.List.CreatedAt);
        Assert.Equal(stored.UpdatedAt, update.List.UpdatedAt);
        Assert.Equal(Friday, update.List.SpModSource!.ReadAt);
    }

    [Fact]
    public void AddedRemovedAndChangedEntriesBumpTheRevisionOnce()
    {
        var stored = Page(items: Base).ToModList("x", Monday);
        stored.Revision = 3;

        var update = SpModListImport.Merge(stored,
            Page(items: [Mod(1, "1.1.0"), Addon(1, parent: 2), Mod(5)]).ToModList("x", Friday));

        Assert.True(update.HasChanges);
        Assert.Equal(4, update.List.Revision);
        Assert.Equal(Monday, update.List.CreatedAt);

        var diff = update.Diff!;
        Assert.Equal([5], diff.Added.Select(e => e.ModId));
        Assert.Equal([2], diff.Removed.Select(e => e.ModId));
        var change = Assert.Single(diff.VersionChanged);
        Assert.Equal(("1.0.0", "1.1.0"), (change.From.Version, change.To.Version));
    }

    [Fact]
    public void AModRenamedOnSpModIsCarriedWithoutABump()
    {
        var stored = Page(items: Base).ToModList("x", Monday);

        var update = SpModListImport.Merge(stored,
            Page(items: [Mod(1, name: "Mod One, renamed"), Mod(2), Addon(1, parent: 2)]).ToModList("x", Friday));

        Assert.False(update.HasChanges);
        Assert.Equal(1, update.List.Revision);
        Assert.Equal("Mod One, renamed", update.List.Entries[0].Name);
    }

    [Theory]
    [InlineData("Fika night (v2)", "4.1.6")]
    [InlineData("Fika night", "4.2.0")]
    [InlineData("Fika night", null)]
    public void ANewListNameOrTargetIsAChange(string name, string? spt)
    {
        var stored = Page(items: Base).ToModList("x", Monday);

        var update = SpModListImport.Merge(stored, Page(name, spt, Base).ToModList("x", Friday));

        Assert.True(update.HasChanges);
        Assert.Equal(2, update.List.Revision);
        Assert.Equal(name != "Fika night", update.Diff!.NameChanged);
        Assert.Equal(spt != "4.1.6", update.Diff.SptVersionChanged);
    }

    [Fact]
    public void AnExcludedItemIsNotOfferedAgainAsNew()
    {
        var stored = Page(items: Base).ToModList("x", Monday, new SpModListChoices(["mod/2"], []));

        var update = SpModListImport.Merge(stored, Page(items: Base).ToModList("x", Friday, SpModListChoices.From(stored)));

        Assert.False(update.HasChanges);
    }

    // ---- storage ----

    [Fact]
    public void TheSourceSurvivesTheStore()
    {
        var list = Page(items: Base).ToModList("x", Monday, new SpModListChoices(["mod/2"], [Entry(90, "3.0.0")], Retargeted: true));

        _store.Add(list);
        var back = new ModListStore(_store.FilePath).Find(list.Id)!;

        Assert.NotNull(back.SpModSource);
        Assert.Equal(4242, back.SpModSource!.ListId);
        Assert.Equal(list.SpModSource!.Url, back.SpModSource.Url);
        Assert.Equal(["mod/2"], back.SpModSource.Excluded);
        Assert.Equal(["mod/90"], back.SpModSource.Added);
        Assert.True(back.SpModSource.Retargeted);
    }

    [Fact]
    public void ReimportingReplacesRatherThanDuplicates()
    {
        var first = Page(items: Base).ToModList("x", Monday);
        _store.Add(first);

        var update = SpModListImport.Merge(_store.Find(first.Id), Page(items: [Mod(1)]).ToModList("x", Friday));
        _store.Add(update.List);

        var stored = Assert.Single(_store.Load().Lists);
        Assert.Equal(2, stored.Revision);
        Assert.Equal([1], stored.Entries.Select(e => e.ModId));
    }

    [Fact]
    public void AnExportDropsTheShareTokenButKeepsTheSource()
    {
        var page = Page(items: Base);
        var list = new SpModListPage
        {
            ListId = page.ListId,
            Source = new Uri("https://sp-mod.com/list/4242/fika-night?share=s3cret"),
            Name = page.Name,
            Pass = page.Pass,
            Items = page.Items,
            Notices = [],
        }.ToModList("x", Monday);

        var json = ModListFile.Write(list);
        var back = ModListFile.Read(json).List!;

        Assert.DoesNotContain("s3cret", json);
        Assert.Equal("https://sp-mod.com/list/4242/fika-night", back.SpModSource!.Url);
        Assert.Equal(4242, back.SpModSource.ListId);
        Assert.Equal("https://sp-mod.com/list/4242/fika-night?share=s3cret", list.SpModSource!.Url);
    }

    [Fact]
    public void AListWithoutASourceWritesNoSourceField()
    {
        var plain = new ModList { Id = Guid.NewGuid(), Name = "mine", CreatedAt = Monday };

        Assert.DoesNotContain(nameof(ModList.SpModSource), ModListFile.Write(plain));
    }

    [Fact]
    public void AForkIsNoLongerAnSpModList()
    {
        var list = Page(items: Base).ToModList("x", Monday);
        _store.Add(list);

        var fork = _store.Fork(list.Id, "my copy", Friday);

        Assert.Null(fork.SpModSource);
        Assert.Equal(list.Id, fork.DerivedFrom);
        Assert.Null(_store.Find(fork.Id)!.SpModSource);
    }

    private static ModListCandidate Installed(int id, ModListEntryScope scope, bool addon = false) =>
        new() { Name = $"Mod {id}", ModId = id, IsAddon = addon, Scope = scope };

    [Fact]
    public void AnApplyLearnsEachInstalledModsScopeOntoAnSpModList()
    {
        var list = Page(items: Base).ToModList("x", Monday);
        _store.Add(list);

        var learned = _store.LearnScopes(list.Id, [
            Installed(1, ModListEntryScope.Server),
            Installed(2, ModListEntryScope.Client | ModListEntryScope.Headless),
            Installed(1, ModListEntryScope.Everyone, addon: true),
        ]);

        Assert.NotNull(learned);
        var stored = _store.Find(list.Id)!;
        Assert.Equal(ModListEntryScope.Server, stored.Entries.Single(e => e.ModId == 1 && !e.IsAddon).Scope);
        Assert.Equal(ModListEntryScope.Client | ModListEntryScope.Headless, stored.Entries.Single(e => e.ModId == 2).Scope);

        // A mod the scanner couldn't place stays as it was rather than taking a guess.
        Assert.Null(stored.Entries.Single(e => e.IsAddon).Scope);

        // The page's read time is not an edit time.
        Assert.Equal(list.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(list.Revision, stored.Revision);
    }

    [Fact]
    public void NothingInstalledMeansNothingLearned()
    {
        var list = Page(items: Base).ToModList("x", Monday);
        _store.Add(list);

        Assert.Null(_store.LearnScopes(list.Id, [Installed(77, ModListEntryScope.Server)]));
    }

    [Fact]
    public void ALearnedScopeIsKeptAndALocalListIsLeftAlone()
    {
        var spMod = Page(items: [Mod(1)]).ToModList("x", Monday);
        spMod.Entries[0] = ModListEntries.WithScope(spMod.Entries[0], ModListEntryScope.Server);

        // Already known: not re-learned from a different reading.
        Assert.Null(ModListScopeLearning.Learn(spMod, [Installed(1, ModListEntryScope.Client)]));

        // On a list made here, no scope is somebody's choice.
        var local = new ModList { Id = Guid.NewGuid(), Name = "mine", CreatedAt = Monday, UpdatedAt = Monday, Entries = [new() { Name = "Mod 1", ModId = 1 }] };
        Assert.Null(ModListScopeLearning.Learn(local, [Installed(1, ModListEntryScope.Server)]));
    }

    [Fact]
    public void ARefreshFromSpModKeepsWhatWasLearned()
    {
        var first = Page(items: Base).ToModList("x", Monday);
        _store.Add(first);
        _store.LearnScopes(first.Id, [Installed(2, ModListEntryScope.Server)]);

        var update = SpModListImport.Merge(_store.Find(first.Id), Page(items: [Mod(1), Mod(2, "1.1.0"), Mod(3)]).ToModList("x", Friday));

        Assert.Equal(ModListEntryScope.Server, update.List.Entries.Single(e => e.ModId == 2).Scope);
        Assert.Null(update.List.Entries.Single(e => e.ModId == 3).Scope);
    }
}
