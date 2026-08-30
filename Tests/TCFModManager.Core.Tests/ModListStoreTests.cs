using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModListStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly ModListStore _store;
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    public ModListStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TCFModManagerModListTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _store = new ModListStore(Path.Combine(_directory, "mod_lists.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static ModList NewList(string name, ModListOrigin origin = ModListOrigin.Local, params ModListEntry[] entries) =>
        Snapshot(name, isSnapshot: false, updatedAt: Timestamp, origin: origin, entries: entries);

    private static ModList Snapshot(
        string name,
        bool isSnapshot = true,
        DateTimeOffset? updatedAt = null,
        ModListOrigin origin = ModListOrigin.Local,
        params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = name,
            Origin = origin,
            IsSnapshot = isSnapshot,
            CreatedAt = Timestamp,
            UpdatedAt = updatedAt ?? Timestamp,
        };

        list.Entries.AddRange(entries);
        return list;
    }

    private static ModListEntry Entry(string name, int? modId = null, int? versionId = null) =>
        new() { Name = name, ModId = modId, VersionId = versionId };

    [Fact]
    public void Load_ReturnsEmptyWhenFileMissing()
    {
        var data = _store.Load();

        Assert.Empty(data.Lists);
        Assert.Null(data.ActiveListId);
    }

    [Fact]
    public void Load_ReturnsEmptyWhenFileIsCorrupt()
    {
        File.WriteAllText(_store.FilePath, "{ not json");

        Assert.Empty(_store.Load().Lists);
    }

    [Fact]
    public void Add_RoundTripsThroughDisk()
    {
        var list = _store.Add(NewList("Fika night", ModListOrigin.Local, Entry("Realism", 1263, 55)));

        var loaded = new ModListStore(_store.FilePath).Find(list.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Fika night", loaded!.Name);
        Assert.Equal(ModListOrigin.Local, loaded.Origin);
        var entry = Assert.Single(loaded.Entries);
        Assert.Equal(1263, entry.ModId);
        Assert.Equal(55, entry.VersionId);
        Assert.True(entry.IsPinned);
    }

    [Fact]
    public void Add_ReplacesAListWithTheSameId()
    {
        var list = _store.Add(NewList("Fika night"));

        var newer = new ModList
        {
            Id = list.Id,
            Name = "Fika night",
            Revision = 2,
            Origin = ModListOrigin.Imported,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        _store.Add(newer);

        Assert.Single(_store.Load().Lists);
        Assert.Equal(2, _store.Find(list.Id)!.Revision);
    }

    [Fact]
    public void ReplaceEntries_BumpsTheRevision()
    {
        var list = _store.Add(NewList("Fika night", ModListOrigin.Local, Entry("Realism", 1263)));

        var updated = _store.ReplaceEntries(list.Id, [Entry("Realism", 1263), Entry("SAIN", 2426)]);

        Assert.NotNull(updated);
        Assert.Equal(2, updated!.Revision);
        Assert.Equal(2, _store.Find(list.Id)!.Entries.Count);
    }

    [Fact]
    public void ReplaceEntries_RefusesAnImportedList()
    {
        var list = _store.Add(NewList("From Dave", ModListOrigin.Imported, Entry("Realism", 1263)));

        Assert.Null(_store.ReplaceEntries(list.Id, [Entry("SAIN", 2426)]));
        Assert.Equal("Realism", Assert.Single(_store.Find(list.Id)!.Entries).Name);
    }

    [Fact]
    public void Rename_RefusesAServedList()
    {
        var list = _store.Add(NewList("Dave's server", ModListOrigin.Server));

        _store.Rename(list.Id, "Mine now");

        Assert.Equal("Dave's server", _store.Find(list.Id)!.Name);
    }

    [Fact]
    public void Fork_CopiesAnImportedListIntoAnEditableOne()
    {
        var source = _store.Add(NewList("From Dave", ModListOrigin.Imported, Entry("Realism", 1263, 55)));

        var fork = _store.Fork(source.Id, "From Dave (mine)", Timestamp);

        Assert.NotEqual(source.Id, fork.Id);
        Assert.Equal(ModListOrigin.Local, fork.Origin);
        Assert.Equal(source.Id, fork.DerivedFrom);
        Assert.Equal(1, fork.Revision);
        Assert.Equal(1263, Assert.Single(fork.Entries).ModId);
        Assert.True(fork.IsEditable);

        Assert.Equal(2, _store.Load().Lists.Count);
        Assert.Equal("From Dave", _store.Find(source.Id)!.Name);
    }

    [Fact]
    public void SetActive_IgnoresAnUnknownList()
    {
        _store.SetActive(Guid.NewGuid());

        Assert.Null(_store.Load().ActiveListId);
    }

    [Fact]
    public void SetActive_ThenGetActiveReturnsTheList()
    {
        var list = _store.Add(NewList("Fika night"));

        _store.SetActive(list.Id);

        Assert.Equal(list.Id, _store.GetActive()!.Id);
    }

    [Fact]
    public void Delete_ClearsTheActivePointerWhenItWasTheActiveList()
    {
        var list = _store.Add(NewList("Fika night"));
        _store.SetActive(list.Id);

        _store.Delete(list.Id);

        var data = _store.Load();
        Assert.Empty(data.Lists);
        Assert.Null(data.ActiveListId);
    }

    [Fact]
    public void Delete_LeavesADifferentActiveListAlone()
    {
        var active = _store.Add(NewList("Fika night"));
        var other = _store.Add(NewList("Solo"));
        _store.SetActive(active.Id);

        _store.Delete(other.Id);

        Assert.Equal(active.Id, _store.Load().ActiveListId);
    }

    [Fact]
    public void ASnapshotGoesToItsOwnSlotRatherThanTheList()
    {
        var snapshot = _store.Add(Snapshot("basic test"));

        var data = _store.Load();
        Assert.Empty(data.Lists);
        Assert.Equal(snapshot.Id, data.Snapshot!.Id);
        Assert.Equal(snapshot.Id, _store.GetSnapshot()!.Id);
    }

    [Fact]
    public void EachApplyOverwritesTheOneSnapshot()
    {
        _store.Add(Snapshot("basic test"));
        var second = _store.Add(Snapshot("Fika night"));

        Assert.Equal("Fika night", _store.GetSnapshot()!.Name);
        Assert.Equal(second.Id, _store.GetSnapshot()!.Id);
        Assert.Empty(_store.Load().Lists);
    }

    [Fact]
    public void SetSnapshotNullClearsIt()
    {
        _store.Add(Snapshot("basic test"));

        _store.SetSnapshot(null);

        Assert.Null(_store.GetSnapshot());
    }

    //
    // A file written before the single slot existed holds one snapshot per apply, each named after
    // the last. The newest fills the slot and the rest go.
    //
    [Fact]
    public void OldSnapshotListsAreLiftedOutOfTheListOnLoad()
    {
        var data = new ModListData();
        data.Lists.Add(NewList("basic test"));
        data.Lists.Add(Snapshot("Before basic test", updatedAt: Timestamp));
        data.Lists.Add(Snapshot("Before Before basic test", updatedAt: Timestamp.AddMinutes(5)));
        data.Lists.Add(Snapshot("Before Before Before basic test", updatedAt: Timestamp.AddMinutes(2)));
        _store.Save(data);

        var loaded = new ModListStore(_store.FilePath).Load();

        Assert.Equal("basic test", Assert.Single(loaded.Lists).Name);
        Assert.Equal("Before Before basic test", loaded.Snapshot!.Name);
    }

    [Fact]
    public void AnOldSnapshotDoesNotDisplaceOneAlreadyInTheSlot()
    {
        var data = new ModListData { Snapshot = Snapshot("kept") };
        data.Lists.Add(Snapshot("stale"));
        _store.Save(data);

        var loaded = new ModListStore(_store.FilePath).Load();

        Assert.Equal("kept", loaded.Snapshot!.Name);
        Assert.Empty(loaded.Lists);
    }

    [Fact]
    public void AnActivePointerAtALiftedSnapshotIsCleared()
    {
        var snapshot = Snapshot("Before basic test");
        var data = new ModListData { ActiveListId = snapshot.Id };
        data.Lists.Add(snapshot);
        _store.Save(data);

        Assert.Null(new ModListStore(_store.FilePath).Load().ActiveListId);
    }

    [Fact]
    public void Unresolved_ListsOnlyEntriesWithNoModId()
    {
        var list = NewList(
            "Mixed",
            ModListOrigin.Local,
            Entry("Realism", 1263, 55),
            Entry("FixPluginTypesSerialization"));

        Assert.Equal("FixPluginTypesSerialization", Assert.Single(list.Unresolved).Name);
    }
}
