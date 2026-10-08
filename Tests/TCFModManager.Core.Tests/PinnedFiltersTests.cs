using System.Text.Json;
using TCFModManager.Core.Models;
using Xunit;

namespace TCFModManager.Core.Tests;

public class PinnedFiltersTests
{
    private static readonly string[] Known = ["SptVersion", "SearchIn", "Show", "Category", "Sort"];

    [Fact]
    public void Normalise_NothingStoredMeansNothingPinned()
    {
        Assert.Empty(PinnedFilters.Normalise(null, Known));
        Assert.Empty(PinnedFilters.Normalise([], Known));
    }

    [Fact]
    public void Normalise_FollowsPanelOrderNotStoredOrder()
    {
        var result = PinnedFilters.Normalise(["Sort", "SptVersion", "Category"], Known);

        Assert.Equal(new[] { "SptVersion", "Category", "Sort" }, result);
    }

    [Fact]
    public void Normalise_DropsNamesNoSectionHas()
    {
        var result = PinnedFilters.Normalise(["Category", "HideAiContent", "", "  "], Known);

        Assert.Equal(new[] { "Category" }, result);
    }

    [Fact]
    public void Normalise_IgnoresCaseAndRepeatsAndReturnsThePagesSpelling()
    {
        var result = PinnedFilters.Normalise(["category", " CATEGORY ", "sort"], Known);

        Assert.Equal(new[] { "Category", "Sort" }, result);
    }

    [Fact]
    public void Settings_NeverHeardOfPinsMeansNothingPinned()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{ "SptInstallPath": "C:\\SPT" }""");

        Assert.Null(settings!.BrowsePinnedFilters);
        Assert.Null(settings.InstalledPinnedFilters);
    }

    [Fact]
    public void Settings_RoundTripsPinsAsNames()
    {
        var json = JsonSerializer.Serialize(new AppSettings
        {
            BrowsePinnedFilters = ["SptVersion", "Sort"],
            InstalledPinnedFilters = ["Group"],
        });
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Contains("\"SptVersion\"", json);
        Assert.Equal(new[] { "SptVersion", "Sort" }, back.BrowsePinnedFilters);
        Assert.Equal(new[] { "Group" }, back.InstalledPinnedFilters);
    }
}
