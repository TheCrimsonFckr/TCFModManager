using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModConfigOptionsStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly ModConfigOptionsStore _store;

    public ModConfigOptionsStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TCFModManagerModConfigs_" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _store = new ModConfigOptionsStore(Path.Combine(_directory, "mod_configs.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AModWithNoEntryMerges()
    {
        Assert.Equal(ModConfigPolicy.Merge, _store.For("SomeMod").Policy);
        Assert.Equal(ModConfigPolicy.Merge, _store.PolicyFor(["SomeMod"]));
    }

    [Fact]
    public void APolicySurvivesTheRoundTripAndIgnoresFolderCase()
    {
        _store.SetPolicy("[SVM] Server Value Modifier", ModConfigPolicy.KeepMine);

        Assert.Equal(ModConfigPolicy.KeepMine, _store.PolicyFor(["[svm] server value modifier"]));
    }

    // The file is offered for hand-editing, so the enum is written as its name.
    [Fact]
    public void ThePolicyIsWrittenAsAName()
    {
        _store.SetPolicy("SomeMod", ModConfigPolicy.TakeNew);

        Assert.Contains("TakeNew", File.ReadAllText(_store.FilePath));
    }

    // Setting the default back leaves no entry behind, so the file only holds real choices.
    [Fact]
    public void SettingMergeBackRemovesTheEntry()
    {
        _store.SetPolicy("SomeMod", ModConfigPolicy.TakeNew);

        _store.SetPolicy("SomeMod", ModConfigPolicy.Merge);

        Assert.Empty(_store.Load());
    }

    [Fact]
    public void AFileThatNoLongerParsesReadsAsEveryModOnTheDefault()
    {
        File.WriteAllText(_store.FilePath, "{ not json");

        Assert.Empty(_store.Load());
        Assert.Equal(ModConfigPolicy.Merge, _store.PolicyFor(["SomeMod"]));
    }
}
