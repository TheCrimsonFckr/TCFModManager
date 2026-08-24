using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.3.0", 1, 3, 0, null)]
    [InlineData("v1.3.0", 1, 3, 0, null)] // leading "v" tolerated
    [InlineData("1.3.0-beta", 1, 3, 0, "beta")]
    [InlineData("1.3.0-beta.2", 1, 3, 0, "beta.2")]
    [InlineData("1.3", 1, 3, 0, null)] // missing segments default to 0
    [InlineData("2", 2, 0, 0, null)]
    [InlineData("1.3.0+abc123", 1, 3, 0, null)] // build metadata dropped
    [InlineData("1.3.0-beta+abc123", 1, 3, 0, "beta")]
    public void TryParse_ReadsEachPart(string raw, int major, int minor, int patch, string? preRelease)
    {
        Assert.True(SemanticVersion.TryParse(raw, out var parsed));
        Assert.Equal(new SemanticVersion(major, minor, patch, preRelease), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("beta")]
    public void TryParse_RejectsAnythingWithoutALeadingNumber(string? raw)
    {
        Assert.False(SemanticVersion.TryParse(raw, out var parsed));
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("1.3.0", "1.3.1", -1)]
    [InlineData("1.3.0", "1.4.0", -1)]
    [InlineData("1.3.0", "2.0.0", -1)]
    [InlineData("1.3.0", "1.3.0", 0)]
    [InlineData("1.2.0", "1.10.0", -1)] // numeric, not lexicographic
    // A release outranks a pre-release of the same numbers - the case ModVersionComparer can't see,
    // since it throws the suffix away.
    [InlineData("1.3.0-beta", "1.3.0", -1)]
    [InlineData("1.3.0", "1.3.0-beta", 1)]
    [InlineData("1.3.0-beta", "1.3.0-beta.2", -1)] // more identifiers rank higher
    [InlineData("1.3.0-beta.2", "1.3.0-beta.10", -1)] // numeric identifiers compare numerically
    [InlineData("1.3.0-alpha", "1.3.0-beta", -1)]
    [InlineData("1.3.0-1", "1.3.0-alpha", -1)] // numeric identifiers rank below alphanumeric ones
    public void CompareTo_FollowsSemVerPrecedence(string left, string right, int expectedSign)
    {
        Assert.True(SemanticVersion.TryParse(left, out var a));
        Assert.True(SemanticVersion.TryParse(right, out var b));

        Assert.Equal(expectedSign, Math.Sign(a!.Value.CompareTo(b!.Value)));
        Assert.Equal(-expectedSign, Math.Sign(b.Value.CompareTo(a.Value)));
    }

    [Theory]
    [InlineData("1.3.0", "1.3.1", VersionChangeKind.Patch)]
    [InlineData("1.3.0", "1.4.0", VersionChangeKind.Minor)]
    [InlineData("1.3.0", "2.0.0", VersionChangeKind.Major)]
    // A jump that moves more than one segment is named by the most significant one that moved.
    [InlineData("1.3.0", "2.4.1", VersionChangeKind.Major)]
    [InlineData("1.3.0", "1.4.1", VersionChangeKind.Minor)]
    // Same numbers, only the suffix moved: nothing new is promised, so it's fix-level.
    [InlineData("1.3.0-beta", "1.3.0", VersionChangeKind.Patch)]
    [InlineData("1.3.0-beta", "1.3.0-beta.2", VersionChangeKind.Patch)]
    // Real releases of this app, as published.
    [InlineData("1.2.1-beta", "1.3.0-beta", VersionChangeKind.Minor)]
    [InlineData("1.2.0-beta", "1.2.1-beta", VersionChangeKind.Patch)]
    public void Classify_NamesTheMostSignificantSegmentThatMoved(
        string installed, string candidate, VersionChangeKind expected)
    {
        Assert.Equal(expected, SemanticVersion.Classify(installed, candidate));
    }

    [Theory]
    [InlineData("1.3.0", "1.3.0")]
    [InlineData("1.3.0", "1.2.9")]
    [InlineData("1.3.0", "1.3.0-beta")] // a beta of what's already installed is not an update
    [InlineData("2.0.0", "1.9.9")]
    public void Classify_ReportsNoneForAnythingNotNewer(string installed, string candidate)
    {
        Assert.Equal(VersionChangeKind.None, SemanticVersion.Classify(installed, candidate));
    }

    [Theory]
    [InlineData(null, "1.3.0")]
    [InlineData("1.3.0", null)]
    [InlineData("not-a-version", "1.3.0")]
    [InlineData("1.3.0", "coming-soon")]
    public void Classify_ReturnsNullRatherThanGuessingAtAnUnparsableVersion(string? installed, string? candidate)
    {
        Assert.Null(SemanticVersion.Classify(installed, candidate));
    }
}
