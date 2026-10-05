using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModVersionComparerTests
{
    [Theory]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.1.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.2.0", "1.10.0", true)] // numeric, not lexicographic, comparison
    [InlineData("v1.0.0", "v1.1.0", true)] // leading "v" tolerated on both sides
    [InlineData("1.0", "1.0.1", true)] // missing segments default to 0
    [InlineData("1.2.0-beta", "1.3.0", true)]
    [InlineData("1.2.0-beta", "1.2.0", true)] // a release is newer than its pre-release
    [InlineData("1.2.0", "1.2.0-beta", false)]
    [InlineData("1.2.0-beta.2", "1.2.0-beta.10", true)] // label parts compare as numbers
    [InlineData("1.2.0-rc2", "1.2.0-rc10", true)]
    [InlineData("1.2.0-alpha", "1.2.0-beta", true)]
    [InlineData("1.2.0", "1.2.0-hotfix", true)] // a fix after the release is newer than it
    [InlineData("1.2.0", "1.2.0-fix2", true)]
    [InlineData("1.2.0-patch1", "1.2.0-patch2", true)]
    [InlineData("1.2.0-hotfix", "1.2.0-hotfix2", true)]
    [InlineData("1.2.0-hotfix", "1.2.0", false)]
    [InlineData("1.2.0-hotfix", "1.2.1-beta", true)] // the numbers still come first
    [InlineData("1.2.0+build5", "1.2.0", false)] // build metadata ignored
    public void IsUpdateAvailable_ComparesNumerically(string installed, string latest, bool expected)
    {
        Assert.Equal(expected, ModVersionComparer.IsUpdateAvailable(installed, latest));
    }

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("1.0.0", null)]
    [InlineData(null, null)]
    [InlineData("not-a-version", "1.0.0")]
    [InlineData("1.0.0", "not-a-version")]
    public void IsUpdateAvailable_ReturnsNullWhenEitherSideIsUnknown(string? installed, string? latest)
    {
        Assert.Null(ModVersionComparer.IsUpdateAvailable(installed, latest));
    }

    [Theory]
    [InlineData("1.2.0.0", "1.2.0-hotfix", false)] // a DLL can't carry the label
    [InlineData("1.2.0.0", "1.2.1", true)]
    [InlineData("1.2.0-beta", "1.2.0", false)]
    public void IsUpdateAvailableByNumbers_IgnoresLabels(string installed, string latest, bool expected)
    {
        Assert.Equal(expected, ModVersionComparer.IsUpdateAvailableByNumbers(installed, latest));
    }

    [Theory]
    [InlineData("1.2.0.0", "1.2.0-beta", true)]
    [InlineData("1.2", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.1", false)]
    [InlineData("1.2.0", null, false)]
    public void SameNumbers_IgnoresLabelsAndTrailingZeros(string? a, string? b, bool expected)
    {
        Assert.Equal(expected, ModVersionComparer.SameNumbers(a, b));
    }
}
