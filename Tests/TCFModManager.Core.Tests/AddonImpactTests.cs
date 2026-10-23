using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class AddonImpactTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static AddonVersionSummary V(int id, string version, string? constraint, int daysAgo) => new()
    {
        Id = id,
        Version = version,
        ModVersionConstraint = constraint,
        PublishedAt = Day.AddDays(-daysAgo),
    };

    private static AddonVersion Full(AddonVersionSummary v) => new()
    {
        Id = v.Id,
        Version = v.Version,
        ModVersionConstraint = v.ModVersionConstraint,
        PublishedAt = v.PublishedAt,
    };

    private static Addon Listing(params AddonVersionSummary[] versions) => new() { Id = 7, Versions = [.. versions] };

    // Essential Texture Pack on sp-mod, cached 2026-10-15.
    private static readonly AddonVersionSummary[] Essential =
    [
        V(4, "1.1.1", "~1.17", 0),
        V(3, "1.1.0", "^1.4.0", 30),
        V(2, "1.0.1", "1.*", 60),
        V(1, "1.0.0", "~1", 90),
    ];

    private sealed class FakeLookup(params AddonVersionSummary[] published)
    {
        public int Calls { get; private set; }

        public int? LastAddonId { get; private set; }

        public Exception? Throw { get; init; }

        public Task<IReadOnlyList<AddonVersion>> Get(int addonId, CancellationToken ct)
        {
            Calls++;
            LastAddonId = addonId;
            if (Throw is not null) throw Throw;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<AddonVersion>>([.. published.Select(Full)]);
        }
    }

    [Fact]
    public async Task AnInstalledVersionThatFitsTheTarget_IsUnaffected_WithoutALookup()
    {
        var lookup = new FakeLookup();

        var result = await AddonImpact.AssessAsync(7, Listing(V(1, "1.0.0", "~1", 0)), "1.0.0", "1.19.0", "4.1.6", lookup.Get);

        Assert.Equal(AddonImpactVerdict.Unaffected, result.Verdict);
        Assert.Null(result.Target);
        Assert.Equal(0, lookup.Calls);
    }

    [Fact]
    public async Task ANewerVersionThatFits_IsNeedsChange_ToIt()
    {
        var lookup = new FakeLookup();
        var listing = Listing(V(2, "1.1.0", "^2.0.0", 0), V(1, "1.0.0", "^1.0.0", 30));

        var result = await AddonImpact.AssessAsync(7, listing, "1.0.0", "2.0.1", "4.1.6", lookup.Get);

        Assert.Equal(AddonImpactVerdict.NeedsChange, result.Verdict);
        Assert.Equal("1.1.0", result.Target?.Version);
        Assert.False(result.TargetIsOlder);
        Assert.Equal(0, lookup.Calls);
    }

    [Fact]
    public async Task AModUpgradeWhoseFittingAddonIsOlder_IsNeedsChange_ToTheOlderVersion()
    {
        var listing = Listing(V(2, "1.1.0", "~1.17.0", 0), V(1, "1.0.0", "^1.4.0", 30));

        var result = await AddonImpact.AssessAsync(7, listing, "1.1.0", "1.19.0", "4.1.6", new FakeLookup().Get);

        Assert.Equal(AddonImpactVerdict.NeedsChange, result.Verdict);
        Assert.Equal("1.0.0", result.Target?.Version);
        Assert.True(result.TargetIsOlder);
    }

    [Fact]
    public async Task ATwoPartTilde_ReachesTheNextMajor_AsSpModReadsIt()
    {
        // sp-mod lists Essential Texture Pack 1.1.1 ("~1.17") as compatible with Camo And Stickers 1.17.0 to 1.19.0.
        var result = await AddonImpact.AssessAsync(7, Listing(Essential), "1.1.1", "1.19.0", "4.1.6", new FakeLookup().Get);

        Assert.Equal(AddonImpactVerdict.Unaffected, result.Verdict);
    }

    [Fact]
    public async Task AModDowngrade_TakesTheNewestVersionThatFitsTheOlderMod()
    {
        var result = await AddonImpact.AssessAsync(7, Listing(Essential), "1.1.0", "1.3.0", "4.1.6", new FakeLookup().Get);

        Assert.Equal(AddonImpactVerdict.NeedsChange, result.Verdict);
        Assert.Equal("1.0.1", result.Target?.Version);
        Assert.True(result.TargetIsOlder);
    }

    [Fact]
    public async Task NothingInTheCacheFits_LooksUpEveryVersion_BeforeSayingNothingFits()
    {
        AddonVersionSummary[] published = [V(2, "1.1.0", "^2.0.0", 0), V(1, "1.0.0", "^2.0.0", 30)];
        var lookup = new FakeLookup(published);

        var result = await AddonImpact.AssessAsync(7, Listing(published), "1.1.0", "3.0.0", "4.1.6", lookup.Get);

        Assert.Equal(AddonImpactVerdict.NoCompatibleVersion, result.Verdict);
        Assert.Equal(1, lookup.Calls);
        Assert.Equal(7, lookup.LastAddonId);
    }

    [Fact]
    public async Task AFittingVersionOlderThanTheCache_IsFoundByTheLookup()
    {
        var cached = Listing(V(3, "1.2.0", "^2.0.0", 0), V(2, "1.1.0", "^2.0.0", 30));
        var lookup = new FakeLookup(V(3, "1.2.0", "^2.0.0", 0), V(2, "1.1.0", "^2.0.0", 30), V(1, "1.0.0", "^1.0.0", 60));

        var result = await AddonImpact.AssessAsync(7, cached, "1.2.0", "1.5.0", "4.1.6", lookup.Get);

        Assert.Equal(AddonImpactVerdict.NeedsChange, result.Verdict);
        Assert.Equal("1.0.0", result.Target?.Version);
        Assert.Equal(1, lookup.Calls);
    }

    [Fact]
    public async Task AnInstalledVersionMissingFromTheCache_IsReadFromTheLookup()
    {
        var cached = Listing(V(3, "1.2.0", "^2.0.0", 0));
        var lookup = new FakeLookup(V(3, "1.2.0", "^2.0.0", 0), V(1, "0.9.0", "^1.0.0", 200));

        var result = await AddonImpact.AssessAsync(7, cached, "0.9.0", "1.5.0", "4.1.6", lookup.Get);

        Assert.Equal(AddonImpactVerdict.Unaffected, result.Verdict);
        Assert.Equal(1, lookup.Calls);
    }

    [Fact]
    public async Task AnInstalledVersionNoLongerPublished_IsUnknown()
    {
        var lookup = new FakeLookup(V(3, "1.2.0", "^2.0.0", 0));

        var result = await AddonImpact.AssessAsync(7, Listing(V(3, "1.2.0", "^2.0.0", 0)), "0.9.0", "1.5.0", "4.1.6", lookup.Get);

        Assert.Equal(AddonImpactVerdict.Unknown, result.Verdict);
        Assert.Equal(AddonImpactUnknownReason.InstalledVersionNotPublished, result.UnknownReason);
    }

    [Fact]
    public async Task AFailedLookup_IsUnknown()
    {
        var lookup = new FakeLookup { Throw = new HttpRequestException("offline") };

        var result = await AddonImpact.AssessAsync(7, Listing(V(3, "1.2.0", "^2.0.0", 0)), "0.9.0", "1.5.0", "4.1.6", lookup.Get);

        Assert.Equal(AddonImpactVerdict.Unknown, result.Verdict);
        Assert.Equal(AddonImpactUnknownReason.LookupFailed, result.UnknownReason);
    }

    [Fact]
    public async Task ACancelledAssessment_IsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var lookup = new FakeLookup { Throw = new OperationCanceledException(cts.Token) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AddonImpact.AssessAsync(7, Listing(V(3, "1.2.0", "^2.0.0", 0)), "0.9.0", "1.5.0", "4.1.6", lookup.Get, cts.Token));
    }

    [Fact]
    public async Task ADetachedAddon_IsUnknown_WithoutALookup()
    {
        var lookup = new FakeLookup();
        var listing = Listing(Essential);
        listing.IsDetached = true;

        var result = await AddonImpact.AssessAsync(7, listing, "1.1.1", "1.19.0", "4.1.6", lookup.Get);

        Assert.Equal(AddonImpactVerdict.Unknown, result.Verdict);
        Assert.Equal(AddonImpactUnknownReason.Detached, result.UnknownReason);
        Assert.Equal(0, lookup.Calls);
    }

    [Fact]
    public async Task AnAddonNoLongerListed_IsUnknown()
    {
        var result = await AddonImpact.AssessAsync(7, null, "1.1.1", "1.19.0", "4.1.6", new FakeLookup().Get);

        Assert.Equal(AddonImpactUnknownReason.NotListed, result.UnknownReason);
    }

    [Fact]
    public async Task AnUnknownInstalledVersion_IsUnknown()
    {
        var result = await AddonImpact.AssessAsync(7, Listing(Essential), null, "1.19.0", "4.1.6", new FakeLookup().Get);

        Assert.Equal(AddonImpactUnknownReason.InstalledVersionUnknown, result.UnknownReason);
    }

    [Fact]
    public async Task AnUnreadableRequirement_IsUnknown()
    {
        var result = await AddonImpact.AssessAsync(7, Listing(V(1, "1.0.0", "not a range", 0)), "1.0.0", "1.19.0", "4.1.6", new FakeLookup().Get);

        Assert.Equal(AddonImpactVerdict.Unknown, result.Verdict);
        Assert.Equal(AddonImpactUnknownReason.UnreadableRequirement, result.UnknownReason);
    }

    [Fact]
    public async Task AnSptRangeInTheModBox_Fits_AsSpModReadsIt()
    {
        // ORBIT - Fika Addon, live on sp-mod 2026-10-17: 1.1.0 needs ORBIT "~4.1.6" (ORBIT's SPT range).
        var listing = Listing(V(302, "1.1.0", "~4.1.6", 0), V(259, "1.0.0", "~2.0", 30));

        var unaffected = await AddonImpact.AssessAsync(7, listing, "1.1.0", "2.1.1", "4.1.6", new FakeLookup().Get);
        var change = await AddonImpact.AssessAsync(7, listing, "1.0.0", "3.0.0", "4.1.6", new FakeLookup().Get);

        Assert.Equal(AddonImpactVerdict.Unaffected, unaffected.Verdict);
        Assert.Equal(AddonImpactVerdict.NeedsChange, change.Verdict);
        Assert.Equal("1.1.0", change.Target?.Version);
        Assert.False(change.TargetIsOlder);
    }
}
