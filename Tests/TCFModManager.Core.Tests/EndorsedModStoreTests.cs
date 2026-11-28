using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class EndorsedModStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"endorsed-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void SetIsKeptAcrossInstances()
    {
        new EndorsedModStore(_path).Set(2706, true);

        Assert.True(new EndorsedModStore(_path).Contains(2706));
        Assert.False(new EndorsedModStore(_path).Contains(902));
    }

    [Fact]
    public void ChangedOnlyWhenTheStateChanges()
    {
        var store = new EndorsedModStore(_path);
        var raised = new List<int?>();
        store.Changed += raised.Add;

        store.Set(2706, true);
        store.Set(2706, true);
        store.Set(902, false);
        store.Set(2706, false);

        Assert.Equal([2706, 2706], raised);
        Assert.False(store.Contains(2706));
    }

    [Fact]
    public void ClearForgetsEveryMod()
    {
        var store = new EndorsedModStore(_path);
        store.Set(1, true);
        store.Set(2, true);
        int? cleared = 0;
        store.Changed += id => cleared = id;

        store.Clear();

        Assert.Null(cleared);
        Assert.False(new EndorsedModStore(_path).Contains(1));
    }
}
