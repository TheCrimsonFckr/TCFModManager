using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModListCaptureTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    private static ModListCandidate Candidate(
        string name,
        int? modId = null,
        string? version = null,
        bool disabled = false,
        string? guid = null,
        string[]? folders = null) =>
        new()
        {
            Name = name,
            ModId = modId,
            Version = version,
            Guid = guid,
            IsDisabled = disabled,
            Folders = folders ?? [],
        };

    private static ModListCapture.VersionLookup Versions(int modId, params (int Id, string Version)[] versions) =>
        id => id == modId
            ? [.. versions.Select(v => new ModVersionSummary { Id = v.Id, Version = v.Version })]
            : null;

    [Fact]
    public void Build_CarriesTheListMetadata()
    {
        var list = ModListCapture.Build("Fika night", [], Timestamp, sptVersion: "3.11.3");

        Assert.Equal("Fika night", list.Name);
        Assert.Equal(ModListOrigin.Local, list.Origin);
        Assert.Equal(ModListPolicy.Exclusive, list.Policy);
        Assert.Equal("3.11.3", list.SptVersion);
        Assert.Equal(1, list.Revision);
        Assert.Equal(Timestamp, list.CreatedAt);
        Assert.False(list.IsSnapshot);
        Assert.Empty(list.Entries);
    }

    [Fact]
    public void Build_SkipsDisabledModsByDefault()
    {
        var list = ModListCapture.Build(
            "Fika night",
            [Candidate("Realism", 1263), Candidate("SAIN", 2426, disabled: true)],
            Timestamp);

        Assert.Equal("Realism", Assert.Single(list.Entries).Name);
    }

    [Fact]
    public void Build_IncludesDisabledModsWhenAsked()
    {
        var list = ModListCapture.Build(
            "Everything",
            [Candidate("Realism", 1263), Candidate("SAIN", 2426, disabled: true)],
            Timestamp,
            includeDisabled: true);

        Assert.Equal(2, list.Entries.Count);
    }

    [Fact]
    public void BuildEntries_PinsTheVersionIdFromTheCachedVersions()
    {
        var entries = ModListCapture.BuildEntries(
            [Candidate("Realism", 1263, "1.4.2")],
            Versions(1263, (55, "1.4.2"), (54, "1.4.1")));

        var entry = Assert.Single(entries);
        Assert.Equal(55, entry.VersionId);
        Assert.Equal("1.4.2", entry.Version);
        Assert.True(entry.IsPinned);
    }

    [Fact]
    public void BuildEntries_MatchesAVersionThatOnlyDiffersInTrailingSegments()
    {
        var entries = ModListCapture.BuildEntries(
            [Candidate("WTT Black Division", 1263, "1.2.1.0")],
            Versions(1263, (55, "1.2.1")));

        Assert.Equal(55, Assert.Single(entries).VersionId);
    }

    [Fact]
    public void BuildEntries_LeavesTheEntryUnpinnedWhenTheVersionIsNotCached()
    {
        var entries = ModListCapture.BuildEntries(
            [Candidate("Realism", 1263, "1.0.0")],
            Versions(1263, (55, "1.4.2")));

        var entry = Assert.Single(entries);
        Assert.Null(entry.VersionId);
        Assert.Equal("1.0.0", entry.Version);
        Assert.True(entry.IsResolved);
        Assert.False(entry.IsPinned);
    }

    [Fact]
    public void BuildEntries_KeepsAModWithNoCatalogMatchAsUnresolved()
    {
        var entries = ModListCapture.BuildEntries(
            [Candidate("FixPluginTypesSerialization", version: "1.0.0", folders: ["FixPluginTypesSerialization"])],
            Versions(1263, (55, "1.4.2")));

        var entry = Assert.Single(entries);
        Assert.Null(entry.ModId);
        Assert.False(entry.IsResolved);
        Assert.Equal("fixplugintypesserialization", Assert.Single(entry.Folders));
    }

    [Fact]
    public void BuildEntries_DedupesByModId()
    {
        var entries = ModListCapture.BuildEntries(
            [Candidate("Realism", 1263, "1.4.2"), Candidate("Realism (server)", 1263, "1.4.2")]);

        Assert.Equal("Realism", Assert.Single(entries).Name);
    }

    [Fact]
    public void BuildEntries_DedupesUnresolvedModsByName()
    {
        var entries = ModListCapture.BuildEntries(
            [Candidate("SVM"), Candidate("svm")]);

        Assert.Single(entries);
    }

    [Fact]
    public void BuildEntries_SortsByName()
    {
        var entries = ModListCapture.BuildEntries(
            [Candidate("SAIN", 2426), Candidate("Realism", 1263), Candidate("Amands Graphics", 900)]);

        Assert.Equal(["Amands Graphics", "Realism", "SAIN"], entries.Select(e => e.Name));
    }

    [Fact]
    public void BuildEntries_LowercasesAndDedupesFolderNames()
    {
        var entries = ModListCapture.BuildEntries(
            [Candidate("Epic's All in One", 1263, "1.0.0", false, null, ["EpicsAIO", "epicsaio", "  "])]);

        Assert.Equal(["epicsaio"], Assert.Single(entries).Folders);
    }

    [Fact]
    public void Build_MarksASnapshotWhenAsked()
    {
        var list = ModListCapture.Build("Before Fika night", [], Timestamp, isSnapshot: true);

        Assert.True(list.IsSnapshot);
    }
}
