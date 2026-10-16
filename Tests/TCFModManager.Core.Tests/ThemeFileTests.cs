using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ThemeFileTests
{
    private static ThemeDefinition Steam() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = "Steam",
        Base = ThemeBase.Dark,
        Accent = ThemeColor.Parse("#1A9FFF"),
        Colors = new ThemeColors
        {
            Background = ThemeColor.Parse("#171D25"),
            Page = ThemeColor.Parse("#FF0F1924"),
            Surface = ThemeColor.Parse("#FF1A3045"),
            FootprintCpu = ThemeColor.Parse("#383B82F6"),
        },
        CornerRadius = 2,
        FontFamily = "Bahnschrift",
        Overrides = new() { ["AcrylicBackgroundFillColorDefault"] = ThemeColor.Parse("#182433") },
    };

    [Fact]
    public void WriteThenRead_KeepsEveryField()
    {
        var original = Steam();

        var read = ThemeFile.Read(ThemeFile.Write(original));

        Assert.True(read.Succeeded);
        var theme = read.Theme!;
        Assert.Equal(original.Id, theme.Id);
        Assert.Equal("Steam", theme.Name);
        Assert.Equal(ThemeBase.Dark, theme.Base);
        Assert.Equal(original.Accent, theme.Accent);
        Assert.Equal(original.Colors, theme.Colors);
        Assert.Equal(2, theme.CornerRadius);
        Assert.Equal("Bahnschrift", theme.FontFamily);
        Assert.Equal(ThemeColor.Parse("#182433"), theme.Overrides["AcrylicBackgroundFillColorDefault"]);
    }

    [Fact]
    public void WriteThenRead_KeepsAccentShadesSetByHand()
    {
        var original = Steam();
        original.AccentShades = new ThemeAccentShades(ThemeColor.Parse("#1A9FFF"), ThemeColor.Parse("#1A9FFF"), ThemeColor.Parse("#66C0F4"));

        Assert.Equal(original.AccentShades, ThemeFile.Read(ThemeFile.Write(original)).Theme!.AccentShades);
    }

    [Fact]
    public void Write_UsesNamesNotNumbersForTheBase()
    {
        var json = ThemeFile.Write(Steam());

        Assert.Contains("\"Base\": \"Dark\"", json);
        Assert.Contains("\"SchemaVersion\": 1", json);
    }

    [Fact]
    public void Read_RefusesANewerFile()
    {
        var read = ThemeFile.Read("""{ "SchemaVersion": 99, "Theme": { "Name": "Future" } }""");

        Assert.Equal(ThemeFileProblem.NewerVersion, read.Problem);
    }

    [Theory]
    [InlineData("not json", ThemeFileProblem.Unreadable)]
    [InlineData("null", ThemeFileProblem.Unreadable)]
    [InlineData("""{ "SchemaVersion": 1 }""", ThemeFileProblem.NotATheme)]
    [InlineData("""{ "SchemaVersion": 1, "Theme": { "Colors": { "Background": "blue" } } }""", ThemeFileProblem.Unreadable)]
    public void Read_SaysWhyItFailed(string json, ThemeFileProblem problem)
    {
        var read = ThemeFile.Read(json);

        Assert.False(read.Succeeded);
        Assert.Equal(problem, read.Problem);
    }

    [Fact]
    public void Read_ToleratesMissingOptionalParts()
    {
        var read = ThemeFile.Read("""{ "SchemaVersion": 1, "Theme": { "Name": " Bare ", "Base": "Light" } }""");

        Assert.True(read.Succeeded);
        Assert.Equal("Bare", read.Theme!.Name);
        Assert.Equal(ThemeBase.Light, read.Theme.Base);
        Assert.NotNull(read.Theme.Colors);
        Assert.Empty(read.Theme.Overrides);
    }
}
