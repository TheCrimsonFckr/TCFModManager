using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ThemeRulesTests
{
    private static ThemeDefinition Named(string name) => new() { Name = name };

    [Fact]
    public void StockDark_PassesEveryCheck()
    {
        Assert.Empty(ThemeRules.Check(new ThemeDefinition { Name = "Dark", Base = ThemeBase.Dark }));
    }

    [Fact]
    public void StockLight_NeverBlocksAndItsPrimaryTextPasses()
    {
        var checks = ThemeRules.Check(new ThemeDefinition { Name = "Light", Base = ThemeBase.Light });

        Assert.All(checks, c => Assert.False(c.BlocksSave));
        Assert.DoesNotContain(checks, c => c.Problem == ThemeProblem.LowContrastPrimaryText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_IsRequired(string name)
    {
        var checks = ThemeRules.Check(Named(name));

        Assert.Contains(checks, c => c.Problem == ThemeProblem.NameMissing && c.BlocksSave);
        Assert.False(ThemeRules.CanSave(Named(name)));
    }

    [Fact]
    public void Name_HasACeiling()
    {
        Assert.Contains(ThemeRules.Check(Named(new string('x', ThemeRules.NameMaxLength + 1))), c => c.Problem == ThemeProblem.NameTooLong);
        Assert.True(ThemeRules.CanSave(Named(new string('x', ThemeRules.NameMaxLength))));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(17)]
    [InlineData(double.NaN)]
    public void CornerRadius_MustBeInRange(double radius)
    {
        var theme = Named("Square");
        theme.CornerRadius = radius;

        Assert.Contains(ThemeRules.Check(theme), c => c.Problem == ThemeProblem.CornerRadiusOutOfRange);
    }

    [Fact]
    public void FontFamily_HasACeiling()
    {
        var theme = Named("Font");
        theme.FontFamily = new string('f', ThemeRules.FontFamilyMaxLength + 1);

        Assert.Contains(ThemeRules.Check(theme), c => c.Problem == ThemeProblem.FontFamilyTooLong);
    }

    [Fact]
    public void LowContrast_IsAWarningNotABlock()
    {
        var theme = Named("Murky");
        theme.Colors = new ThemeColors
        {
            Background = ThemeColor.Parse("#303030"),
            TextPrimary = ThemeColor.Parse("#404040"),
        };

        var check = Assert.Single(ThemeRules.Check(theme), c => c.Problem == ThemeProblem.LowContrastPrimaryText);
        Assert.False(check.BlocksSave);
        Assert.NotNull(check.Ratio);
        Assert.True(ThemeRules.CanSave(theme));
    }

    [Fact]
    public void LowContrast_OnThePageIsCaughtWhenCardsAreFine()
    {
        var theme = Named("Beige page");
        theme.Colors = new ThemeColors
        {
            Background = ThemeColor.Parse("#171D25"),
            Page = ThemeColor.Parse("#D9D4C3"),
            Surface = ThemeColor.Parse("#1A3045"),
            TextPrimary = ThemeColor.Parse("#F1F2F4"),
        };

        var check = Assert.Single(ThemeRules.Check(theme), c => c.Problem == ThemeProblem.LowContrastPrimaryText);
        Assert.Equal(ThemeSurface.Page, check.Surface);
        Assert.True(check.Ratio < 1.5);
    }

    [Fact]
    public void LowContrast_ReportsTheWorstSurface()
    {
        var theme = Named("Pale menus");
        theme.Colors = new ThemeColors
        {
            Flyout = ThemeColor.Parse("#E0E0E0"),
        };

        var check = Assert.Single(ThemeRules.Check(theme), c => c.Problem == ThemeProblem.LowContrastPrimaryText);
        Assert.Equal(ThemeSurface.Menus, check.Surface);
    }

    [Fact]
    public void Normalise_TrimsAndEmptiesBlankFont()
    {
        var theme = new ThemeDefinition { Name = "  Steam  ", FontFamily = "   " };

        ThemeRules.Normalise(theme);

        Assert.Equal("Steam", theme.Name);
        Assert.Null(theme.FontFamily);
    }
}
