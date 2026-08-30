using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModListFileTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 29, 21, 0, 0, TimeSpan.Zero);

    private static ModList List(
        ModListOrigin origin = ModListOrigin.Local,
        bool isSnapshot = false,
        params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Fika night",
            Description = "what we play on Thursdays",
            Revision = 3,
            Origin = origin,
            Policy = ModListPolicy.Exclusive,
            SptVersion = "3.11.3",
            IsSnapshot = isSnapshot,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        list.Entries.AddRange(entries);
        return list;
    }

    private static ModListEntry Entry(string name, int? modId = null, int? versionId = null, string? version = null) =>
        new() { Name = name, ModId = modId, VersionId = versionId, Version = version };

    [Fact]
    public void AListSurvivesTheRoundTrip()
    {
        var list = List(entries: [Entry("SAIN", 2426, 55, "3.2.0"), Entry("FixPluginTypesSerialization")]);

        var read = ModListFile.Read(ModListFile.Write(list));

        Assert.True(read.Succeeded);
        var imported = read.List!;
        Assert.Equal(list.Id, imported.Id);
        Assert.Equal("Fika night", imported.Name);
        Assert.Equal("what we play on Thursdays", imported.Description);
        Assert.Equal(3, imported.Revision);
        Assert.Equal(ModListPolicy.Exclusive, imported.Policy);
        Assert.Equal("3.11.3", imported.SptVersion);
        Assert.Equal(2, imported.Entries.Count);

        var sain = imported.Entries.Single(e => e.Name == "SAIN");
        Assert.Equal(2426, sain.ModId);
        Assert.Equal(55, sain.VersionId);
        Assert.Equal("3.2.0", sain.Version);
        Assert.True(sain.IsPinned);
        Assert.Single(imported.Unresolved);
    }

    [Fact]
    public void AnImportedListIsReadOnly()
    {
        var read = ModListFile.Read(ModListFile.Write(List(entries: [Entry("SAIN", 2426)])));

        Assert.Equal(ModListOrigin.Imported, read.List!.Origin);
        Assert.False(read.List.IsEditable);
    }

    [Fact]
    public void TheAuthorBecomesTheSource()
    {
        var read = ModListFile.Read(ModListFile.Write(List(), author: "Dave"));

        Assert.Equal("Dave", read.List!.Source);
    }

    [Fact]
    public void TheFileNameIsTheSourceWhenNobodySignedIt()
    {
        var read = ModListFile.Read(ModListFile.Write(List()), fallbackSource: "daves-server");

        Assert.Equal("daves-server", read.List!.Source);
    }

    [Fact]
    public void SomebodyElsesSnapshotIsNotImportedAsASnapshot()
    {
        var read = ModListFile.Read(ModListFile.Write(List(isSnapshot: true)));

        Assert.False(read.List!.IsSnapshot);
    }

    [Fact]
    public void AKeptIdMeansARevisionUpdatesRatherThanDuplicates()
    {
        var list = List(entries: [Entry("SAIN", 2426)]);
        var first = ModListFile.Read(ModListFile.Write(list)).List!;

        list.Revision = 4;
        list.Entries.Add(Entry("Realism", 1263));
        var second = ModListFile.Read(ModListFile.Write(list)).List!;

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(4, second.Revision);

        var store = new ModListStore(Path.Combine(Path.GetTempPath(), $"TCFMMImport_{Guid.NewGuid()}.json"));
        store.Add(first);
        store.Add(second);

        Assert.Single(store.Load().Lists);
        Assert.Equal(4, store.Find(first.Id)!.Revision);

        File.Delete(store.FilePath);
    }

    [Fact]
    public void AFileFromANewerAppIsRefused()
    {
        var json = ModListFile.Write(List()).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99");

        var read = ModListFile.Read(json);

        Assert.False(read.Succeeded);
        Assert.Contains("newer version", read.Error);
    }

    [Fact]
    public void RubbishIsRefusedRatherThanThrown()
    {
        Assert.Contains("readable", ModListFile.Read("{ not json").Error);
        Assert.Contains("empty", ModListFile.Read("   ").Error);
        Assert.Contains("doesn't contain", ModListFile.Read("{}").Error);
    }

    [Fact]
    public void AListWithNoNameIsRefused()
    {
        var json = ModListFile.Write(List()).Replace("\"Name\": \"Fika night\"", "\"Name\": \"\"");

        Assert.Contains("no name", ModListFile.Read(json).Error);
    }

    [Fact]
    public void BlankEntriesAreDroppedRatherThanCarriedThrough()
    {
        var list = List(entries: [Entry("SAIN", 2426), Entry("   ")]);

        Assert.Equal("SAIN", Assert.Single(ModListFile.Read(ModListFile.Write(list)).List!.Entries).Name);
    }

    [Fact]
    public void TheSuggestedFileNameIsSafeOnWindows()
    {
        var list = List();
        list.Name = "Dave's list: 3/11";

        var name = ModListFile.SuggestedFileName(list);

        Assert.EndsWith(ModListFile.Extension, name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('/', name);
    }

    [Fact]
    public void SavingAndLoadingUsesTheSameShape()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TCFMMFile_{Guid.NewGuid()}{ModListFile.Extension}");
        var list = List(entries: [Entry("SAIN", 2426, 55, "3.2.0")]);

        try
        {
            ModListFile.Save(list, path, "Dave");
            var read = ModListFile.Load(path);

            Assert.True(read.Succeeded);
            Assert.Equal(list.Id, read.List!.Id);
            Assert.Equal("Dave", read.List.Source);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AMissingFileIsRefusedRatherThanThrown()
    {
        var read = ModListFile.Load(Path.Combine(Path.GetTempPath(), $"nope_{Guid.NewGuid()}{ModListFile.Extension}"));

        Assert.False(read.Succeeded);
        Assert.Contains("couldn't be opened", read.Error);
    }
}
