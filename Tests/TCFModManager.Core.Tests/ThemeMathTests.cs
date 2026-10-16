using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ThemeMathTests
{
    [Fact]
    public void AccentShades_OnDark_MixTowardsWhiteBy15_30_45()
    {
        var shades = ThemeMath.AccentShades(ThemeColor.Parse("#B4A46B"), ThemeBase.Dark);

        Assert.Equal(ThemeColor.Parse("#BFB281"), shades.Primary);
        Assert.Equal(ThemeColor.Parse("#CABF97"), shades.Secondary);
        Assert.Equal(ThemeColor.Parse("#D6CDAE"), shades.Tertiary);
    }

    [Fact]
    public void AccentShades_OnLight_MixTowardsBlack()
    {
        var shades = ThemeMath.AccentShades(ThemeColor.Parse("#FFFFFF"), ThemeBase.Light);

        Assert.Equal(ThemeColor.Parse("#D9D9D9"), shades.Primary);
        Assert.Equal(ThemeColor.Parse("#B2B2B2"), shades.Secondary);
        Assert.Equal(ThemeColor.Parse("#8C8C8C"), shades.Tertiary);
    }

    [Fact]
    public void AccentShadesFor_PrefersShadesTheThemeSetsItself()
    {
        var own = new ThemeAccentShades(ThemeColor.Parse("#1A9FFF"), ThemeColor.Parse("#1A9FFF"), ThemeColor.Parse("#66C0F4"));
        var theme = new ThemeDefinition { Accent = ThemeColor.Parse("#1A9FFF"), AccentShades = own };

        Assert.Same(own, ThemeMath.AccentShadesFor(theme));
    }

    [Fact]
    public void CompositeOver_LandsWithinOneStepOfWhatTheSpikeMeasuredOnScreen()
    {
        var page = ThemeMath.CompositeOver(ThemeColor.Parse("#4C3A3A3A"), ThemeColor.Parse("#202020"));
        var card = ThemeMath.CompositeOver(ThemeColor.Parse("#0DFFFFFF"), page);

        Assert.InRange(page.R, 0x27, 0x28);
        Assert.InRange(card.R, 0x32, 0x33);
        Assert.True(page.R == page.G && page.G == page.B);
    }

    [Fact]
    public void ContrastRatio_BlackOnWhiteIs21()
    {
        Assert.Equal(21.0, ThemeMath.ContrastRatio(ThemeMath.Black, ThemeMath.White), 2);
    }

    [Fact]
    public void ContrastRatio_LightTertiaryTextFailsBodyText()
    {
        var ratio = ThemeMath.ContrastRatio(ThemeColor.Parse("#72000000"), ThemeColor.Parse("#FEFEFE"));

        Assert.InRange(ratio, 3.0, 4.5);
    }

    [Theory]
    [InlineData("#1A9FFF", "#000000")]
    [InlineData("#FFD400", "#000000")]
    [InlineData("#0F1924", "#FFFFFF")]
    public void PreferredTextOn_PicksWhicheverReadsBetter(string fill, string expected)
    {
        Assert.Equal(ThemeColor.Parse(expected), ThemeMath.PreferredTextOn(ThemeColor.Parse(fill)));
    }

    [Fact]
    public void Resolve_FillsGapsFromTheBase()
    {
        var theme = new ThemeDefinition
        {
            Base = ThemeBase.Dark,
            Colors = new ThemeColors { Background = ThemeColor.Parse("#000000") },
        };

        var resolved = ThemeMath.Resolve(theme);

        Assert.Equal(ThemeColor.Parse("#000000"), resolved.Background);
        Assert.Equal(ThemeColor.Parse("#FFFFFF"), resolved.TextPrimary);
        Assert.Equal(ThemeColor.Parse("#4C3A3A3A"), resolved.Page);
    }
}
