using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class AddonSearchTests
{
    private const int CamoId = 2658;

    private static readonly Addon Scribbles = new()
    {
        Id = 72, Name = "CAG Scribbles!", Slug = "cag-scribbles", Teaser = "Hand-drawn markings for the high speed PMC!",
        ModId = CamoId, Owner = new Owner { Name = "CAG" },
    };

    private static readonly Addon Essential = new()
    {
        Id = 40, Name = "Essential Texture Pack", Slug = "essential-texture-pack", Teaser = "Basic collection of textures",
        ModId = CamoId, Owner = new Owner { Name = "7Bpencil" }, AdditionalAuthors = [new Owner { Name = "Helper" }],
    };

    private static readonly Addon Orphan = new()
    {
        Id = 9, Name = "Old Scribble Pack", Slug = "old-scribble-pack", ModId = 1234, IsDetached = true,
    };

    private static readonly Addon[] All = [Scribbles, Essential, Orphan];

    [Theory]
    [InlineData("scribbles")]
    [InlineData("SCRIBBLES")]
    [InlineData("cag-scrib")]
    [InlineData("hand-drawn")]
    public void NameSlugAndTeaser_Match(string query) =>
        Assert.Contains(Scribbles, AddonSearch.Match(All, query, titleOnly: false));

    [Fact]
    public void TitleOnly_LeavesTheTeaserOut()
    {
        Assert.Empty(AddonSearch.Match(All, "hand-drawn", titleOnly: true));
        Assert.Equal([Scribbles], AddonSearch.Match(All, "cag scribbles", titleOnly: true));
    }

    [Fact]
    public void AnAuthorSearch_MatchesTheOwnerAndCoAuthors()
    {
        Assert.Equal([Essential], AddonSearch.Match(All, "@7bpencil", titleOnly: false));
        Assert.Equal([Essential], AddonSearch.Match(All, "@ helper", titleOnly: false));
        Assert.Empty(AddonSearch.Match(All, "@", titleOnly: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoSearchText_MatchesNothing(string? query) =>
        Assert.Empty(AddonSearch.Match(All, query, titleOnly: false));

    [Fact]
    public void ADetachedAddon_Matches_ButBringsNoParent()
    {
        var matches = AddonSearch.Match(All, "scribble", titleOnly: false);

        Assert.Equal([Scribbles, Orphan], matches);
        Assert.Equal([CamoId], AddonSearch.ParentIds(matches));
    }
}
