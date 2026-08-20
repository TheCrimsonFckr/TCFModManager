using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

public class QueryParametersTests
{
    [Fact]
    public void ModsQuery_OmitsUnsetFilters()
    {
        var query = new ModsQuery { SearchQuery = "raid time" };

        var parameters = query.ToParameters().ToList();

        Assert.Single(parameters);
        Assert.Equal(new KeyValuePair<string, string?>("query", "raid time"), parameters[0]);
    }

    [Fact]
    public void ModsQuery_RendersBoolFiltersAsLowercaseStrings()
    {
        var query = new ModsQuery { FilterFeatured = true, FilterContainsAds = false };

        var parameters = query.ToParameters().ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("true", parameters["filter[featured]"]);
        Assert.Equal("false", parameters["filter[contains_ads]"]);
    }

    [Fact]
    public void ModsQuery_IncludesPagingAndShapingParameters()
    {
        var query = new ModsQuery { Fields = "name,slug", Include = "versions,category", Sort = "-name", Page = 2, PerPage = 25 };

        var parameters = query.ToParameters().ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("name,slug", parameters["fields"]);
        Assert.Equal("versions,category", parameters["include"]);
        Assert.Equal("-name", parameters["sort"]);
        Assert.Equal("2", parameters["page"]);
        Assert.Equal("25", parameters["per_page"]);
    }

    [Fact]
    public void ModVersionsQuery_FilterFikaCompatibility_IsPassedThroughVerbatim()
    {
        // fika_compatibility on versions is a comma-separated enum string (compatible/incompatible/unknown),
        // not a bool - unlike the same-named filter on /mods.
        var query = new ModVersionsQuery { FilterFikaCompatibility = "compatible,unknown" };

        var parameters = query.ToParameters().ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("compatible,unknown", parameters["filter[fika_compatibility]"]);
    }
}
