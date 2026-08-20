using System.Net;
using TCFModManagement.Core.SpModApi;
using Xunit;

namespace TCFModManagement.Core.Tests;

// 
// These tests use JSON fixtures captured from live calls against https://sp-mod.com on 2026-08-13,
// rather than the API doc's generated examples - a couple of fields (License.short_name,
// Category.name/color_class) only exist in the docs' fake data and are never actually returned,
// so asserting against real responses is what catches that.
// 
public class SpModApiClientTests
{
    private static SpModApiClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler));

    [Fact]
    public async Task GetModsAsync_BuildsExpectedQueryString()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"success":true,"data":[],"links":{},"meta":{}}""");
        using var client = CreateClient(handler);

        await client.GetModsAsync(new ModsQuery { SearchQuery = "raid time", FilterFeatured = true, PerPage = 25 });

        Assert.NotNull(handler.LastRequestUri);
        var query = handler.LastRequestUri!.Query;
        Assert.Contains("query=raid%20time", query);
        Assert.Contains("filter%5Bfeatured%5D=true", query);
        Assert.Contains("per_page=25", query);
    }

    [Fact]
    public async Task GetModsAsync_DeserializesRealListResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModsListFixture);
        using var client = CreateClient(handler);

        var result = await client.GetModsAsync();

        Assert.True(result.Success);
        Assert.Single(result.Data);
        var mod = result.Data[0];
        Assert.Equal(31, mod.Id);
        Assert.Equal("Scav Cat Trader Mod", mod.Name);
        Assert.Equal("scav-cat-trader-mod", mod.Slug);
        Assert.Equal(13548, mod.Downloads);
        Assert.Equal("DonutxLord", mod.Owner?.Name);
        Assert.Equal(1810, result.Meta?.LastPage);
    }

    [Fact]
    public async Task GetModAsync_DeserializesCategoryUsingTitleNotName()
    {
        // Live /mod/{id}?include=category returns {id, hub_id, title, slug, description} - not the
        // {id, name, slug, color_class} shape shown in some of the doc's generated examples.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModDetailsFixture);
        using var client = CreateClient(handler);

        var mod = await client.GetModAsync("31", include: "versions,license,category");

        Assert.Equal("Traders", mod.Category?.Title);
        Assert.Equal("traders", mod.Category?.Slug);
    }

    [Fact]
    public async Task GetModAsync_DeserializesLicenseUsingNameAndLinkNotShortName()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModDetailsFixture);
        using var client = CreateClient(handler);

        var mod = await client.GetModAsync("31", include: "versions,license,category");

        Assert.Equal("MIT License", mod.License?.Name);
        Assert.Equal("https://choosealicense.com/licenses/mit/", mod.License?.Link);
    }

    [Fact]
    public async Task GetModAsync_DeserializesEmbeddedVersionSummaries()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModDetailsFixture);
        using var client = CreateClient(handler);

        var mod = await client.GetModAsync("31", include: "versions,license,category");

        Assert.NotNull(mod.Versions);
        Assert.Equal(2, mod.Versions!.Count);
        Assert.Equal("1.0.8", mod.Versions[0].Version);
    }

    [Fact]
    public async Task GetModUpdatesAsync_CategorizesRealResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModUpdatesFixture);
        using var client = CreateClient(handler);

        var result = await client.GetModUpdatesAsync("31:1.0.0", "4.0.10");

        Assert.Equal("4.0.10", result.SptVersion);
        Assert.Single(result.Updates);
        Assert.Empty(result.BlockedUpdates);
        Assert.Empty(result.UpToDate);
        Assert.Empty(result.IncompatibleWithSpt);

        var update = result.Updates[0];
        Assert.Equal(31, update.CurrentVersion?.ModId);
        Assert.Equal("1.0.0", update.CurrentVersion?.Version);
        Assert.Equal("1.0.8", update.RecommendedVersion?.Version);
        Assert.Equal("newer_version_available", update.UpdateReason);
    }

    [Fact]
    public async Task GetModDependenciesAsync_KeyedByExactQueriedPair()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModDependenciesFixture);
        using var client = CreateClient(handler);

        var result = await client.GetModDependenciesAsync("31:1.0.8", "4.0.10");

        Assert.True(result.ContainsKey("31:1.0.8"));
        Assert.Empty(result["31:1.0.8"]);
    }

    [Fact]
    public async Task RateLimitedResponse_ThrowsWithRetryAfter()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.TooManyRequests,
            """{"success":false,"code":"RATE_LIMITED","message":"Too many requests. Retry after the number of seconds in the Retry-After header."}""",
            retryAfterSeconds: "30");
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SpModApiRateLimitedException>(() => client.GetModsAsync());

        Assert.Equal("RATE_LIMITED", exception.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task NotFoundResponse_ThrowsWithCodeAndMessage()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.NotFound,
            """{"success":false,"code":"NOT_FOUND","message":"Resource not found."}""");
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SpModApiException>(() => client.GetModAsync("999999"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NOT_FOUND", exception.Code);
        Assert.Equal("Resource not found.", exception.Message);
    }

    // ---- Fixtures, captured from live https://sp-mod.com responses on 2026-08-13 -----------------

    private const string ModsListFixture = """
        {"success":true,"data":[{"id":31,"name":"Scav Cat Trader Mod","slug":"scav-cat-trader-mod","downloads":13548,"category_id":16,"owner":{"id":355,"name":"DonutxLord","profile_photo_url":"https://files.sp-mod.com/profile-photos/HbLCZMn9dHWo8M5j0uG452RjevMNWl64GC4TY9yM.png","cover_photo_url":"https://files.sp-mod.com/cover-photos/QhEdXITt9r8KicVKeRqp2wxE1KpttShD4LkKPm3X.png"},"additional_authors":[]}],"links":{"first":"https://sp-mod.com/api/v0/mods?page=1","last":"https://sp-mod.com/api/v0/mods?page=1810","prev":null,"next":"https://sp-mod.com/api/v0/mods?page=2"},"meta":{"current_page":1,"from":1,"last_page":1810,"path":"https://sp-mod.com/api/v0/mods","per_page":1,"to":1,"total":1810}}
        """;

    private const string ModDetailsFixture = """
        {"success":true,"data":{"id":31,"hub_id":76,"guid":"com.donut.scavcat","name":"Scav Cat Trader Mod","slug":"scav-cat-trader-mod","teaser":"Scav Cat sells cases.","thumbnail":"https://files.sp-mod.com/mods/76.jpg","downloads":13548,"favourites_count":9,"description":"<p>Cheap cases.</p>","detail_url":"https://sp-mod.com/mod/31/scav-cat-trader-mod","fika_compatibility":false,"featured":false,"contains_ads":false,"contains_ai_content":false,"custom_ai_disclosure":"","shows_profile_binding_notice":true,"cheat_notice":false,"category_id":16,"published_at":"2021-01-01T07:22:00.000000Z","created_at":"2021-01-01T07:22:08.000000Z","updated_at":"2026-07-28T07:26:05.000000Z","owner":{"id":355,"name":"DonutxLord","profile_photo_url":"https://files.sp-mod.com/profile-photos/x.png","cover_photo_url":"https://files.sp-mod.com/cover-photos/y.png"},"additional_authors":[],"versions":[{"id":12710,"hub_id":null,"version":"1.0.8","spt_version_constraint":" 4.0.10 ","downloads":4711,"published_at":"2025-12-29T20:52:00.000000Z"},{"id":11467,"hub_id":130,"version":"1.0.0","spt_version_constraint":"","downloads":306,"published_at":"2021-01-01T07:22:08.000000Z"}],"license":{"id":11,"hub_id":14,"name":"MIT License","link":"https://choosealicense.com/licenses/mit/","created_at":"2025-09-26T15:44:12.000000Z","updated_at":"2025-09-26T15:44:12.000000Z"},"category":{"id":16,"hub_id":29,"title":"Traders","slug":"traders","description":""}}}
        """;

    private const string ModUpdatesFixture = """
        {"success":true,"data":{"spt_version":"4.0.10","updates":[{"current_version":{"id":11467,"mod_id":31,"guid":"com.donut.scavcat","name":"Scav Cat Trader Mod","slug":"scav-cat-trader-mod","version":"1.0.0"},"recommended_version":{"id":12710,"version":"1.0.8","link":"https://sp-mod.com/mod/download/31/scav-cat-trader-mod/1.0.8","content_length":43815,"fika_compatibility":"unknown","spt_versions":["4.0.10"]},"update_reason":"newer_version_available"}],"blocked_updates":[],"up_to_date":[],"incompatible_with_spt":[]}}
        """;

    private const string ModDependenciesFixture = """
        {"success":true,"data":{"31:1.0.8":[]}}
        """;
}
