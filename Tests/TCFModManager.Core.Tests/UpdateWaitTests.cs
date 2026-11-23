using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-64: wait before offering an update.
public class UpdateWaitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static ModVersion V(string version, int? daysAgo) =>
        new() { Version = version, PublishedAt = daysAgo is { } d ? Now.AddDays(-d) : null };

    private static string? Pick(int days, string installed, string upTo, params ModVersion[] versions) =>
        UpdateWait.NewestOldEnough(versions, v => v.Version, v => v.PublishedAt, installed, upTo, days, Now)?.Version;

    [Fact]
    public void OffersTheNewestVersionOldEnough()
    {
        Assert.Equal("1.2.1", Pick(3, "1.2.0", "1.2.2", V("1.2.2", 0), V("1.2.1", 5), V("1.2.0", 30)));
    }

    [Fact]
    public void OffersNothingWhenOnlyTheNewestIsOut()
    {
        Assert.Null(Pick(3, "1.2.0", "1.2.1", V("1.2.1", 1), V("1.2.0", 30)));
    }

    [Fact]
    public void AVersionWithNoPublishDateIsOldEnough()
    {
        Assert.Equal("1.2.1", Pick(7, "1.2.0", "1.2.1", V("1.2.1", null)));
    }

    [Fact]
    public void NeverGoesPastTheUpdateItWasAskedAbout()
    {
        Assert.Equal("1.2.1", Pick(1, "1.2.0", "1.2.1", V("1.3.0", 10), V("1.2.1", 10)));
    }

    [Fact]
    public void WaitingAndTheDateItEnds()
    {
        Assert.True(UpdateWait.IsWaiting(Now.AddDays(-1), 3, Now));
        Assert.False(UpdateWait.IsWaiting(Now.AddDays(-4), 3, Now));
        Assert.False(UpdateWait.IsWaiting(null, 3, Now));
        Assert.False(UpdateWait.IsWaiting(Now, 0, Now));
        Assert.Equal(Now.AddDays(2), UpdateWait.OfferedFrom(Now.AddDays(-1), 3));
    }
}
