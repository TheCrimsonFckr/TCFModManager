using System.Text.Json;
using TCFModManager.Core.Models;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ThemeColorTests
{
    [Theory]
    [InlineData("#1A9FFF", 0xFF, 0x1A, 0x9F, 0xFF)]
    [InlineData("1a9fff", 0xFF, 0x1A, 0x9F, 0xFF)]
    [InlineData("#4C2A475E", 0x4C, 0x2A, 0x47, 0x5E)]
    [InlineData("  #000000 ", 0xFF, 0, 0, 0)]
    public void Parse_ReadsSixAndEightDigitHex(string text, byte a, byte r, byte g, byte b)
    {
        Assert.Equal(new ThemeColor(a, r, g, b), ThemeColor.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    public void TryParse_RejectsAnythingElse(string? text)
    {
        Assert.False(ThemeColor.TryParse(text, out _));
    }

    [Fact]
    public void ToString_DropsTheAlphaOnlyWhenOpaque()
    {
        Assert.Equal("#1A9FFF", ThemeColor.Parse("#FF1A9FFF").ToString());
        Assert.Equal("#4C2A475E", ThemeColor.Parse("#4C2A475E").ToString());
    }

    [Fact]
    public void Json_RoundTripsAsAHexString()
    {
        var json = JsonSerializer.Serialize(new ThemeColors { Background = ThemeColor.Parse("#171D25") });

        Assert.Contains("\"Background\":\"#171D25\"", json);
        Assert.Equal(ThemeColor.Parse("#171D25"), JsonSerializer.Deserialize<ThemeColors>(json)!.Background);
    }

    [Fact]
    public void Json_RefusesAColourThatIsNotOne()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ThemeColors>("""{ "Background": "blue" }"""));
    }
}
