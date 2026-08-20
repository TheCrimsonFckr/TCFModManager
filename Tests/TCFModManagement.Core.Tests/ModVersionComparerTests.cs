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
    [InlineData("1.2.0-beta", "1.3.0", true)] // pre-release suffix dropped before comparing
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
}
