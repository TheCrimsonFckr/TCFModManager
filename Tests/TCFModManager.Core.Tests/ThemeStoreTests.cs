using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ThemeStoreTests : IDisposable
{
    private readonly string _folder;
    private readonly ThemeStore _store;

    public ThemeStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "TCFModManagerThemeTests_" + Guid.NewGuid());
        _store = new ThemeStore(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void LoadAll_IsEmptyBeforeAnythingIsSaved()
    {
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Save_GivesANewThemeAnIdAndWritesItUnderIt()
    {
        var saved = _store.Save(new ThemeDefinition { Name = "Mine" });

        Assert.True(ThemeStore.IsCustomId(saved.Id));
        Assert.True(File.Exists(Path.Combine(_folder, saved.Id + ".json")));
        Assert.Equal("Mine", _store.Find(saved.Id)!.Name);
    }

    [Fact]
    public void Save_ReplacesAPresetStyleIdWithAGuid()
    {
        var saved = _store.Save(new ThemeDefinition { Id = "tarkov", Name = "Copy of Tarkov" });

        Assert.NotEqual("tarkov", saved.Id);
        Assert.True(ThemeStore.IsCustomId(saved.Id));
    }

    [Fact]
    public void Save_OverwritesTheSameId()
    {
        var saved = _store.Save(new ThemeDefinition { Name = "Before" });
        saved.Name = "After";
        _store.Save(saved);

        Assert.Equal("After", Assert.Single(_store.LoadAll()).Name);
    }

    [Fact]
    public void LoadAll_SortsByNameAndSkipsADamagedFile()
    {
        _store.Save(new ThemeDefinition { Name = "zulu" });
        _store.Save(new ThemeDefinition { Name = "Alpha" });
        File.WriteAllText(Path.Combine(_folder, Guid.NewGuid() + ".json"), "{ broken");
        File.WriteAllText(Path.Combine(_folder, "notes.json"), "{}");

        var names = _store.LoadAll().Select(t => t.Name).ToList();

        Assert.Equal(["Alpha", "zulu"], names);
        Assert.Contains(Directory.EnumerateFiles(_folder), f => f.Contains(".damaged-", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadAll_TrustsTheFileNameOverTheIdInside()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(
            Path.Combine(_folder, id + ".json"),
            ThemeFile.Write(new ThemeDefinition { Id = Guid.NewGuid().ToString(), Name = "Moved" }));

        Assert.Equal(id.ToString(), Assert.Single(_store.LoadAll()).Id);
    }

    [Fact]
    public void Find_ReturnsNullForAPresetIdOrAMissingTheme()
    {
        Assert.Null(_store.Find("tarkov"));
        Assert.Null(_store.Find(Guid.NewGuid().ToString()));
    }

    [Fact]
    public void Delete_RemovesOnlyThatTheme()
    {
        var keep = _store.Save(new ThemeDefinition { Name = "Keep" });
        var drop = _store.Save(new ThemeDefinition { Name = "Drop" });

        Assert.True(_store.Delete(drop.Id));
        Assert.False(_store.Delete(drop.Id));
        Assert.False(_store.Delete("tarkov"));
        Assert.Equal(keep.Id, Assert.Single(_store.LoadAll()).Id);
    }

    [Fact]
    public void Import_ANewThemeKeepsItsIdSoAReimportReplacesIt()
    {
        var shared = new ThemeDefinition { Id = Guid.NewGuid().ToString(), Name = "Shared v1" };

        var first = _store.Import(ThemeFile.Write(shared));
        shared.Name = "Shared v2";
        var second = _store.Import(ThemeFile.Write(shared));

        Assert.False(first.Replaced);
        Assert.True(second.Replaced);
        Assert.Equal(shared.Id, second.Theme!.Id);
        Assert.Equal("Shared v2", Assert.Single(_store.LoadAll()).Name);
    }

    [Fact]
    public void Import_ReportsAnUnreadableFile()
    {
        var import = _store.Import("nope");

        Assert.False(import.Succeeded);
        Assert.Equal(ThemeFileProblem.Unreadable, import.Problem);
        Assert.Empty(_store.LoadAll());
    }
}
