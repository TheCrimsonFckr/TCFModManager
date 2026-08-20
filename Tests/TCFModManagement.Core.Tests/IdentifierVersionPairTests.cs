using TCFModManagement.Core.Services;
using Xunit;

namespace TCFModManagement.Core.Tests;

public class IdentifierVersionPairTests
{
    [Theory]
    [InlineData("5:1.2.0", "5", "1.2.0")]
    [InlineData("com.example.mod:2.0.5", "com.example.mod", "2.0.5")]
    [InlineData(" 5 : 1.2.0 ", "5", "1.2.0")]
    public void TryParse_SplitsOnFirstColon(string text, string expectedIdentifier, string expectedVersion)
    {
        var parsed = IdentifierVersionPair.TryParse(text, out var pair);

        Assert.True(parsed);
        Assert.Equal(expectedIdentifier, pair.Identifier);
        Assert.Equal(expectedVersion, pair.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-colon")]
    [InlineData(":1.2.0")]
    [InlineData("5:")]
    public void TryParse_RejectsMalformedInput(string text)
    {
        var parsed = IdentifierVersionPair.TryParse(text, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void Join_ProducesCommaSeparatedPairs()
    {
        var pairs = new[]
        {
            new IdentifierVersionPair("5", "1.2.0"),
            new IdentifierVersionPair("com.example.mod", "2.0.5"),
        };

        var joined = IdentifierVersionPair.Join(pairs);

        Assert.Equal("5:1.2.0,com.example.mod:2.0.5", joined);
    }
}
