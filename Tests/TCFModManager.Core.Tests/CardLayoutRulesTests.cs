using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class CardLayoutRulesTests
{
    private static readonly IReadOnlySet<string> Known = new HashSet<string> { "Note", "Version", "Author", "Group" };

    private static CardLayout Layout(string name, CardLayoutPage page = CardLayoutPage.Installed, params string[] fields) =>
        new() { Name = name, Page = page, Fields = [.. fields] };

    [Fact]
    public void CleanFields_KeepsKnownOnceInOrder()
    {
        var fields = CardLayoutRules.CleanFields(["Author", "Gone", "Note", "Author", "Version"], Known);

        Assert.Equal(["Author", "Note", "Version"], fields);
    }

    [Fact]
    public void CheckName_IsPerPageAndIgnoresCaseAndSpacing()
    {
        var layouts = new[] { Layout("My  cards"), Layout("Browse one", CardLayoutPage.Browse) };

        Assert.Equal(PresetNameProblem.Taken, CardLayoutRules.CheckName(layouts, CardLayoutPage.Installed, " my cards "));
        Assert.Equal(PresetNameProblem.None, CardLayoutRules.CheckName(layouts, CardLayoutPage.Installed, "Browse one"));
        Assert.Equal(PresetNameProblem.Empty, CardLayoutRules.CheckName(layouts, CardLayoutPage.Installed, "   "));
    }

    [Fact]
    public void CheckName_RenamingToItsOwnNameIsFine()
    {
        var layout = Layout("Mine");

        Assert.Equal(PresetNameProblem.None, CardLayoutRules.CheckName([layout], CardLayoutPage.Installed, "MINE", layout.Id));
    }

    [Fact]
    public void UniqueName_CountsUpPastTakenNames()
    {
        var layouts = new[] { Layout("Copy"), Layout("copy 2") };

        Assert.Equal("Copy 3", CardLayoutRules.UniqueName(layouts, CardLayoutPage.Installed, "Copy"));
        Assert.Equal("Copy", CardLayoutRules.UniqueName(layouts, CardLayoutPage.Browse, "Copy"));
    }

    [Fact]
    public void Normalise_DropsUnknownFieldsAndNamesABlankLayout()
    {
        var other = Layout("Cards");
        var layout = new CardLayout
        {
            Name = "  ",
            Page = CardLayoutPage.Installed,
            Fields = ["Note", "Removed", "Note"],
            ListFields = ["Group", "Removed"],
            Size = (CardSize)42,
            Picture = (CardPicture)7,
        };

        CardLayoutRules.Normalise(layout, Known, [other, layout], "Cards");

        Assert.Equal("Cards 2", layout.Name);
        Assert.Equal(["Note"], layout.Fields);
        Assert.Equal(["Group"], layout.ListFields);
        Assert.Equal(CardSize.Normal, layout.Size);
        Assert.Equal(CardPicture.Off, layout.Picture);
    }

    [Fact]
    public void Find_MatchesIdOnTheRightPageOnly()
    {
        var layout = Layout("Mine");

        Assert.Same(layout, CardLayoutRules.Find([layout], CardLayoutPage.Installed, layout.Id.ToString()));
        Assert.Null(CardLayoutRules.Find([layout], CardLayoutPage.Browse, layout.Id.ToString()));
        Assert.Null(CardLayoutRules.Find([layout], CardLayoutPage.Installed, "not a guid"));
    }

    [Fact]
    public void Copy_IsIndependentWithANewId()
    {
        var layout = Layout("Mine", CardLayoutPage.Installed, "Note");
        layout.ListFields = ["Group"];
        layout.DetailsInWindow = true;
        layout.FieldsRevision = 1;

        var copy = CardLayoutRules.Copy(layout, "Mine 2");
        copy.Fields.Add("Author");
        copy.ListFields!.Add("Version");

        Assert.NotEqual(layout.Id, copy.Id);
        Assert.True(copy.DetailsInWindow);
        Assert.Equal(1, copy.FieldsRevision);
        Assert.Equal(["Note"], layout.Fields);
        Assert.Equal(["Group"], layout.ListFields);
    }

    [Fact]
    public void Settings_LayoutSavedBeforeRevisionsReadsAsNoRevision()
    {
        var back = JsonSerializer.Deserialize<CardLayout>("{\"Name\":\"Old\",\"Fields\":[\"Note\"]}")!;

        Assert.Null(back.FieldsRevision);
    }

    [Fact]
    public void Settings_RoundTripLayoutsWithEnumNames()
    {
        var settings = new AppSettings();
        settings.CardLayouts.Add(new CardLayout { Name = "Big", Page = CardLayoutPage.Browse, Size = CardSize.Roomy, Width = CardWidth.Wide, Picture = CardPicture.Banner });

        var json = JsonSerializer.Serialize(settings);
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Contains("\"Roomy\"", json);
        Assert.Contains("\"Banner\"", json);
        var layout = Assert.Single(back.CardLayouts);
        Assert.Equal(CardLayoutPage.Browse, layout.Page);
        Assert.Equal(CardWidth.Wide, layout.Width);
    }
}
