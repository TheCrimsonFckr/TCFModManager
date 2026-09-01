using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModFootprintStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _filePath;
    private readonly ModFootprintStore _store;

    public ModFootprintStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TCFModManagerFootprintStoreTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _filePath = Path.Combine(_directory, "mod_footprints.json");
        _store = new ModFootprintStore(_filePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static ModFootprint Footprint(string key = @"c:\spt\bepinex\plugins\example") => new()
    {
        FolderKey = key,
        TotalBytes = 12345,
        FileCount = 7,
        AssemblyCount = 2,
        BundleCount = 1,
        BundleBytes = 1024,
        HasPatcher = true,
        HasServerHalf = true,
        HarmonyPatchClassCount = 3,
        ModulePatchClassCount = 9,
        PerFrameMethods = ["Ticker.Update"],
        AnalysedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        Stamp = "7:12345:1000",
    };

    [Fact]
    public void LoadReturnsEmptyWhenThereIsNoFile()
    {
        Assert.Empty(_store.Load());
    }

    [Fact]
    public void RoundTripsEveryStoredCount()
    {
        var footprint = Footprint();
        _store.Save(new Dictionary<string, ModFootprint> { [footprint.FolderKey] = footprint });

        var loaded = _store.Load()[footprint.FolderKey];

        Assert.Equal(footprint.TotalBytes, loaded.TotalBytes);
        Assert.Equal(footprint.FileCount, loaded.FileCount);
        Assert.Equal(footprint.AssemblyCount, loaded.AssemblyCount);
        Assert.Equal(footprint.BundleBytes, loaded.BundleBytes);
        Assert.True(loaded.HasPatcher);
        Assert.True(loaded.HasServerHalf);
        Assert.Equal(footprint.HarmonyPatchClassCount, loaded.HarmonyPatchClassCount);
        Assert.Equal(footprint.ModulePatchClassCount, loaded.ModulePatchClassCount);
        Assert.Equal(footprint.Stamp, loaded.Stamp);
        Assert.Equal(footprint.AnalysedAt, loaded.AnalysedAt);
        Assert.Single(loaded.PerFrameMethods, m => m == "Ticker.Update");
    }

    [Fact]
    public void DerivedValuesComeBackFromTheCountsRatherThanTheFile()
    {
        var footprint = Footprint();
        _store.Save(new Dictionary<string, ModFootprint> { [footprint.FolderKey] = footprint });

        var json = File.ReadAllText(_filePath);
        var loaded = _store.Load()[footprint.FolderKey];

        // Nothing derived is written - a hand-edited file can misstate the counts, but it can never
        // make the level disagree with them.
        Assert.DoesNotContain("\"Level\"", json);
        Assert.DoesNotContain("\"Signals\"", json);
        Assert.DoesNotContain("\"Score\"", json);
        Assert.Equal(footprint.Level, loaded.Level);
        Assert.Equal(footprint.Signals, loaded.Signals);
    }

    [Fact]
    public void AFileFromAnotherSchemaVersionIsDiscarded()
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(new { SchemaVersion = 99, Footprints = new Dictionary<string, object>() }));

        Assert.Empty(_store.Load());
    }

    [Fact]
    public void ACorruptFileIsDiscardedRatherThanThrowing()
    {
        File.WriteAllText(_filePath, "{ not json");

        Assert.Empty(_store.Load());
    }

    [Fact]
    public void IsCurrentMatchesOnlyAnIdenticalStamp()
    {
        var footprint = Footprint();

        Assert.True(ModFootprintStore.IsCurrent(footprint, footprint.Stamp));
        Assert.False(ModFootprintStore.IsCurrent(footprint, "7:12345:2000"));
    }

    [Fact]
    public void IsCurrentRejectsNothingCachedAndAnEmptyStamp()
    {
        Assert.False(ModFootprintStore.IsCurrent(null, "7:12345:1000"));
        Assert.False(ModFootprintStore.IsCurrent(Footprint() with { Stamp = "" }, ""));
    }
}
