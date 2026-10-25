using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class AddonFitOverrideTests : IDisposable
{
    private const int FikaSyncId = 212;
    private const int IcebreakerId = 3301;

    private static readonly DateTimeOffset Timestamp = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-override-" + Guid.NewGuid().ToString("N"));
    private readonly ModInstallManifestService _manifest;

    public AddonFitOverrideTests()
    {
        Directory.CreateDirectory(_root);
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
        _manifest.Save(new ModInstallManifest
        {
            Mods =
            [
                Record(IcebreakerId, isAddon: false, "Icebreaker", "1.1.3"),
                Record(FikaSyncId, isAddon: true, "Icebreaker Fika Sync", "1.0.0"),
            ],
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void FitOverride_IsRecordedMovedAndCleared_AndLeftOutWhileNull()
    {
        Assert.True(_manifest.SetFitOverride(FikaSyncId, "1.1.3"));
        Assert.False(_manifest.SetFitOverride(FikaSyncId, "1.1.3"));
        Assert.Equal("1.1.3", _manifest.Load().Find(FikaSyncId, isAddon: true)?.FitOverride);

        Assert.True(_manifest.SetFitOverride(FikaSyncId, "1.2.0"));
        Assert.Equal("1.2.0", _manifest.Load().Find(FikaSyncId, isAddon: true)?.FitOverride);

        Assert.True(_manifest.SetFitOverride(FikaSyncId, null));
        Assert.DoesNotContain("\"FitOverride\"", File.ReadAllText(_manifest.FilePath));
    }

    [Fact]
    public void FitOverride_IsOnlySetOnAnAddon_NeverOnTheModWithTheSameNumber()
    {
        Assert.False(_manifest.SetFitOverride(IcebreakerId, "1.1.3"));
        Assert.Null(_manifest.Load().Find(IcebreakerId, isAddon: false)?.FitOverride);
    }

    [Fact]
    public async Task AnOverriddenAddonThatNothingFits_IsOverridden_NotNoCompatibleVersion()
    {
        var listing = new Addon { Id = FikaSyncId, Versions = [V(1, "1.0.0", "1.1.0")] };

        var overridden = await AddonImpact.AssessAsync(FikaSyncId, listing, "1.0.0", "1.2.0", "4.1.6", Lookup(listing), overridden: true);
        var plain = await AddonImpact.AssessAsync(FikaSyncId, listing, "1.0.0", "1.2.0", "4.1.6", Lookup(listing));

        Assert.Equal(AddonImpactVerdict.Overridden, overridden.Verdict);
        Assert.Equal(AddonImpactVerdict.NoCompatibleVersion, plain.Verdict);
    }

    [Fact]
    public async Task AnOverriddenAddon_StillGetsAVersionThatReallyFits()
    {
        var listing = new Addon { Id = FikaSyncId, Versions = [V(2, "1.1.0", "~1.2.0"), V(1, "1.0.0", "1.1.0")] };

        var result = await AddonImpact.AssessAsync(FikaSyncId, listing, "1.0.0", "1.2.0", "4.1.6", Lookup(listing), overridden: true);

        Assert.Equal(AddonImpactVerdict.NeedsChange, result.Verdict);
        Assert.Equal("1.1.0", result.Target?.Version);
    }

    [Fact]
    public void Capture_CarriesTheOverride_OnAnAddonOnly()
    {
        var entries = ModListCapture.BuildEntries(
        [
            new ModListCandidate { Name = "Icebreaker Fika Sync", ModId = FikaSyncId, IsAddon = true, Version = "1.0.0", FitOverride = true },
            new ModListCandidate { Name = "Icebreaker", ModId = IcebreakerId, Version = "1.1.3", FitOverride = true },
        ]);

        Assert.True(entries.Single(e => e.IsAddon).FitOverride);
        Assert.False(entries.Single(e => !e.IsAddon).FitOverride);
    }

    [Fact]
    public void Refresh_FollowsTheInstall_WhenOnlyTheOverrideChanged()
    {
        var entry = new ModListEntry { Name = "Icebreaker Fika Sync", ModId = FikaSyncId, IsAddon = true, Version = "1.0.0" };

        var gained = ModListRefresh.Build(
            [entry],
            [new ModListCandidate { Name = "Icebreaker Fika Sync", ModId = FikaSyncId, IsAddon = true, Version = "1.0.0", FitOverride = true }]);
        var lost = ModListRefresh.Build(
            gained.Entries,
            [new ModListCandidate { Name = "Icebreaker Fika Sync", ModId = FikaSyncId, IsAddon = true, Version = "1.0.0" }]);

        Assert.True(gained.Entries[0].FitOverride);
        Assert.False(lost.Entries[0].FitOverride);
        Assert.Empty(gained.Changed);
    }

    [Fact]
    public void ShareFile_RoundTripsTheOverride_AndLeavesItOutWhenFalse()
    {
        var overridden = List(new ModListEntry
        {
            Name = "Icebreaker Fika Sync", ModId = FikaSyncId, IsAddon = true, Version = "1.0.0", FitOverride = true,
        });
        var plain = List(new ModListEntry { Name = "Icebreaker Fika Sync", ModId = FikaSyncId, IsAddon = true, Version = "1.0.0" });

        var imported = ModListFile.Read(ModListFile.Write(overridden));

        Assert.True(Assert.Single(imported.List!.Entries).FitOverride);
        Assert.Equal(ModListFile.AddonSchemaVersion, ModListFile.SchemaVersionFor(overridden));
        Assert.DoesNotContain("FitOverride", ModListFile.Write(plain));
    }

    [Fact]
    public void AMissingParent_IsNotHeldBackByAnOverriddenAddonsRange()
    {
        var addons = new Dictionary<int, Addon>
        {
            [FikaSyncId] = new() { Id = FikaSyncId, Name = "Icebreaker Fika Sync", ModId = IcebreakerId, Versions = [V(1, "1.0.0", "1.1.0")] },
        };
        var catalog = new Dictionary<int, Mod>
        {
            [IcebreakerId] = new()
            {
                Id = IcebreakerId, Name = "Icebreaker",
                Versions =
                [
                    new ModVersionSummary { Id = 2, Version = "1.1.3", SptVersionConstraint = "~4.1", PublishedAt = Timestamp },
                    new ModVersionSummary { Id = 1, Version = "1.1.0", SptVersionConstraint = "~4.1", PublishedAt = Timestamp.AddDays(-30) },
                ],
            },
        };

        ModListEntry Sync(bool overridden) => new()
        {
            Name = "Icebreaker Fika Sync", ModId = FikaSyncId, IsAddon = true, Version = "1.0.0", VersionId = 1, FitOverride = overridden,
        };

        var plain = Assert.Single(ModListDependencies.MissingParents([Sync(false)], addons, catalog, "4.1.6", new HashSet<int>()));
        var overriddenParent = Assert.Single(ModListDependencies.MissingParents([Sync(true)], addons, catalog, "4.1.6", new HashSet<int>()));

        Assert.Equal("1.1.0", plain.Version);
        Assert.Equal("1.1.3", overriddenParent.Version);
    }

    private static InstalledModRecord Record(int id, bool isAddon, string name, string version) => new()
    {
        ModId = id,
        IsAddon = isAddon,
        Name = name,
        Version = version,
        InstalledAt = Timestamp,
    };

    private static AddonVersionSummary V(int id, string version, string constraint) => new()
    {
        Id = id,
        Version = version,
        ModVersionConstraint = constraint,
        PublishedAt = Timestamp.AddDays(-id),
    };

    private static AddonVersionsLookup Lookup(Addon listing) => (_, _) =>
        Task.FromResult<IReadOnlyList<AddonVersion>>(
        [
            .. (listing.Versions ?? []).Select(v => new AddonVersion
            {
                Id = v.Id, Version = v.Version, ModVersionConstraint = v.ModVersionConstraint, PublishedAt = v.PublishedAt,
            }),
        ]);

    private static ModList List(params ModListEntry[] entries)
    {
        var list = new ModList { Id = Guid.NewGuid(), Name = "Fika night", CreatedAt = Timestamp, UpdatedAt = Timestamp };
        list.Entries.AddRange(entries);
        return list;
    }
}
