using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class NewestFittingVersionTests
{
    private static readonly DateTimeOffset Day = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ModVersion Mod(string version, string spt, int day) =>
        new() { Version = version, SptVersionConstraint = spt, PublishedAt = Day.AddDays(day) };

    private static AddonVersion Addon(string version, string? parent, int day) =>
        new() { Version = version, ModVersionConstraint = parent, PublishedAt = Day.AddDays(day) };

    [Fact]
    public void ForSpt_TakesTheNewestThatRunsOnThisSpt_NotTheNewestOverall()
    {
        // Oldest first, as the API sends them unsorted.
        var versions = new[] { Mod("1.0.0", "~4.0.0", 1), Mod("1.9.1", "~4.0.0", 5), Mod("2.0.2", "~4.1.0", 9) };

        Assert.Equal("1.9.1", NewestFittingVersion.ForSpt(versions, "4.0.13")?.Version);
        Assert.Equal("2.0.2", NewestFittingVersion.ForSpt(versions, "4.1.6")?.Version);
    }

    [Fact]
    public void ForSpt_IsNull_WhenNothingRunsOnThisSpt() =>
        Assert.Null(NewestFittingVersion.ForSpt([Mod("1.0.0", "~3.11.0", 1)], "4.1.6"));

    [Fact]
    public void ForSpt_TakesTheNewest_WhenTheSptVersionIsUnknown() =>
        Assert.Equal("2.0.0", NewestFittingVersion.ForSpt([Mod("1.0.0", "~4.0.0", 1), Mod("2.0.0", "~4.1.0", 2)], null)?.Version);

    [Fact]
    public void ForSpt_SamePublishDate_TakesTheHigherVersion() =>
        Assert.Equal("1.2.0", NewestFittingVersion.ForSpt([Mod("1.2.0-beta", "~4.1.0", 1), Mod("1.2.0", "~4.1.0", 1)], "4.1.6")?.Version);

    [Fact]
    public void ForParent_TakesTheNewestTheParentSatisfies()
    {
        var versions = new[] { Addon("1.0.0", "^1.0.0", 1), Addon("2.0.0", "^2.0.0", 5) };

        Assert.Equal("1.0.0", NewestFittingVersion.ForParent(versions, "1.4.0")?.Version);
        Assert.Equal("2.0.0", NewestFittingVersion.ForParent(versions, "2.1.0")?.Version);
        Assert.Null(NewestFittingVersion.ForParent(versions, "3.0.0"));
    }

    [Fact]
    public void ForParent_DoesntRuleOutAVersionWithNoConstraint() =>
        Assert.Equal("1.1.0", NewestFittingVersion.ForParent([Addon("1.0.0", "^1.0.0", 1), Addon("1.1.0", null, 2)], "1.4.0")?.Version);

    [Fact]
    public void ForParent_TakesTheNewest_WhenTheParentsVersionIsUnknown() =>
        Assert.Equal("2.0.0", NewestFittingVersion.ForParent([Addon("1.0.0", "^1.0.0", 1), Addon("2.0.0", "^2.0.0", 2)], null)?.Version);
}
