using System.Net;
using System.Text;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class SpModListImportTests
{
    private static readonly Uri AddonsList = new("https://sp-mod.com/list/126308/addons");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpModLists", name));

    private static SpModListPage Read(string fixture, string url)
    {
        var read = SpModListImport.Parse(Fixture(fixture), new Uri(url));
        Assert.True(read.Succeeded, read.Failure?.ToString());
        return read.Page!;
    }

    // A list page cut down to the parts the parser reads, in the shape sp-mod renders them.
    private static string Page(string counts, params string[] groups) => $"""
        <html><head><title>Test list - Mod List - The Forge</title></head><body>
        <span class="badge-version violet">SPT 4.1.6</span>
        <p class="mt-1 text-sm text-gray-400">by <a href="https://sp-mod.com/user/1/someone">someone</a>
        <span>·</span> <time datetime="2026-10-01T12:00:00+00:00">Thursday</time> {counts}</p>
        <div class="grid">{string.Concat(groups)}</div>
        </body></html>
        """;

    private static string Group(string key, string? mod, params string[] addons) => $"""
        <div wire:key="list-group-{key}"><div>{mod ?? "<div class=\"truncate\">Removed item</div>"}</div>
        <ul>{string.Concat(addons)}</ul></div>
        """;

    private static string Mod(int id, string name, string? version = "1.0.0", string extra = "") => $"""
        <a href="https://sp-mod.com/mod/{id}/x" aria-hidden="true"><img src="a.png"></a>
        <div><a href="https://sp-mod.com/mod/{id}/x" class="truncate">{name}</a>{(version is null ? "" : $"<span>{version}</span>")}</div>
        <span class="badge-version gray"><span class="sr-only">SPT version&nbsp;</span>SPT 4.1.6</span>{extra}
        """;

    private static string Addon(int key, int id, string name, string version = "2.0.0") => $"""
        <li><div wire:key="list-addon-{key}"><a href="https://sp-mod.com/addon/{id}/y" class="truncate">{name}</a><span>{version}</span></div></li>
        """;

    private static string OptedOutAddon(int key) =>
        $"<li wire:key=\"list-addon-{key}\"><div class=\"truncate\">Removed item</div></li>";

    // ---- addresses ----

    [Theory]
    [InlineData("https://sp-mod.com/list/126308/addons", "https://sp-mod.com/list/126308/addons")]
    [InlineData("  https://sp-mod.com/list/126308/addons/  ", "https://sp-mod.com/list/126308/addons")]
    [InlineData("http://www.sp-mod.com/list/126308", "https://sp-mod.com/list/126308")]
    [InlineData("sp-mod.com/list/126308/addons", "https://sp-mod.com/list/126308/addons")]
    [InlineData("https://SP-MOD.com/list/126308/addons?share=abc123", "https://sp-mod.com/list/126308/addons?share=abc123")]
    public void ListAddressesAreAcceptedAndCanonicalised(string text, string expected)
    {
        Assert.True(SpModListImport.TryParseListUrl(text, out var id, out var url));
        Assert.Equal(126308, id);
        Assert.Equal(expected, url.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://sp-mod.com/lists")]
    [InlineData("https://sp-mod.com/mod/2706/orbit-20")]
    [InlineData("https://sp-mod.com/list/0/zero")]
    [InlineData("https://sp-mod.com/list/abc/slug")]
    [InlineData("https://sp-mod.com/list/126308/addons/extra")]
    [InlineData("https://evil.example/list/126308/addons")]
    [InlineData("https://sp-mod.com.evil.example/list/126308")]
    [InlineData("ftp://sp-mod.com/list/126308")]
    public void AnythingElseIsRefused(string? text)
    {
        Assert.False(SpModListImport.TryParseListUrl(text, out _, out _));
    }

    [Fact]
    public void TheListIdIsStableAndDistinctPerSpModList()
    {
        var first = SpModListImport.ListIdFor(126308);

        Assert.Equal(first, SpModListImport.ListIdFor(126308));
        Assert.NotEqual(first, SpModListImport.ListIdFor(126309));
        Assert.Equal('5', first.ToString()[14]);
        Assert.Contains(first.ToString()[19], "89ab");
    }

    // ---- real pages ----

    [Fact]
    public void ModsAndAddonsAreBothRead()
    {
        var page = Read("list-126308.html", AddonsList.ToString());

        Assert.Equal(SpModListReadPass.Structural, page.Pass);
        Assert.Empty(page.Notices);

        Assert.Equal("Addons", page.Name);
        Assert.Equal("Kryptic-S01", page.Author);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 59, 23, TimeSpan.Zero), page.UpdatedAt);
        Assert.Equal(5, page.ExpectedMods);
        Assert.Equal(6, page.ExpectedAddons);

        Assert.Equal([2658, 2706, 2788, 2511, 3090], page.Mods.Select(m => m.Id));
        Assert.Equal([134, 135, 124, 138, 34, 145], page.Addons.Select(a => a.Id));

        var orbit = Assert.Single(page.Mods, m => m.Id == 2706);
        Assert.Equal("ORBIT 2.0", orbit.Name);
        Assert.Equal("2.1.1", orbit.Version);

        var liveLike = Assert.Single(page.Addons, a => a.Id == 135);
        Assert.Equal("Live-Like ORBIT – Full Config + Routes", liveLike.Name);
        Assert.Equal("1.3.0", liveLike.Version);
        Assert.Equal(2706, liveLike.ParentModId);
        Assert.True(liveLike.ParentOnList);
    }

    [Fact]
    public void AListWithNoTargetSptHasNone()
    {
        // The per-mod "SPT 4.1.6" badges on this page are not the list's target.
        Assert.Null(Read("list-126308.html", AddonsList.ToString()).SptVersion);
    }

    [Fact]
    public void ABigListIsReadInFullWithItsTargetSpt()
    {
        var page = Read("list-127707.html", "https://sp-mod.com/list/127707/spt-4013-part-1");

        Assert.Equal(SpModListReadPass.Structural, page.Pass);
        Assert.Empty(page.Notices);
        Assert.Equal("SPT-4.0.13 Part 1", page.Name);
        Assert.Equal("4.0.13", page.SptVersion);
        Assert.Equal(223, page.Mods.Count());
        Assert.Equal(27, page.Addons.Count());

        // Resolved against 4.0.13 by sp-mod, not the newest (4.5.1, for 4.1).
        Assert.Equal("4.4.3", Assert.Single(page.Mods, m => m.Id == 791).Version);
        Assert.All(page.Items, i => Assert.NotNull(i.Version));
    }

    [Fact]
    public void AListWithNoAddonsReadsZeroAddons()
    {
        var page = Read("list-121380.html", "https://sp-mod.com/list/121380/overhaul-mods");

        Assert.Equal("Overhaul mods", page.Name);
        Assert.Equal(4, page.Mods.Count());
        Assert.Empty(page.Addons);
        Assert.Equal(0, page.ExpectedAddons);
        Assert.Empty(page.Notices);
    }

    [Fact]
    public void AnUnavailableModIsNotedAndStillMatchesTheHeader()
    {
        // The header says 108; one of them is "This mod is no longer available".
        var page = Read("list-127534.html", "https://sp-mod.com/list/127534/my-spt-run-mods");

        Assert.Equal(SpModListReadPass.Structural, page.Pass);
        Assert.Equal(108, page.ExpectedMods);
        Assert.Equal(107, page.Mods.Count());
        Assert.Equal("4.1.6", page.SptVersion);

        var notice = Assert.Single(page.Notices);
        Assert.Equal(SpModListNoticeKind.Unavailable, notice.Kind);
        Assert.Equal(SpModListItemKind.Mod, notice.ItemKind);
    }

    [Fact]
    public void WithoutTheCardsTheFallbackStillFindsEverything()
    {
        var page = Read("list-126308-no-cards.html", AddonsList.ToString());

        Assert.Equal(SpModListReadPass.Fallback, page.Pass);
        Assert.Contains(page.Notices, n => n.Kind == SpModListNoticeKind.LayoutChanged);
        Assert.DoesNotContain(page.Notices, n => n.Kind == SpModListNoticeKind.CountMismatch);

        Assert.Equal([2658, 2706, 2788, 2511, 3090], page.Mods.Select(m => m.Id));
        Assert.Equal(6, page.Addons.Count());
        Assert.Equal("2.1.1", Assert.Single(page.Mods, m => m.Id == 2706).Version);
        Assert.All(page.Addons, a => Assert.Null(a.ParentModId));
    }

    // ---- what each card shows ----

    [Fact]
    public void AModCardCarriesWhatSpModShowsBesideIt()
    {
        var orbit = Read("list-126308.html", AddonsList.ToString()).Mods.Single(m => m.Id == 2706).Card!;

        Assert.Equal("https://sp-mod.com/mod/2706/orbit-20", orbit.Url);
        Assert.StartsWith("https://files.sp-mod.com/mods/", orbit.Thumbnail);
        Assert.Equal("Chazut", orbit.Author);
        Assert.Equal(51156, orbit.Downloads);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 8, 18, 36, TimeSpan.Zero), orbit.UpdatedAt);
        Assert.Equal("4.1.6", orbit.SptVersion);
        Assert.False(orbit.IsDependency);
        Assert.Equal(
            [("SAIN - Solarint's AI Modifications - Full AI Combat System Replacement", false), ("Waypoints - Expanded Navmesh", false), ("BigBrain", false)],
            orbit.Dependencies.Select(d => (d.Name, d.OnList)));
    }

    [Fact]
    public void DependencyBadgesAndListsAreRead()
    {
        var page = Read("list-127707.html", "https://sp-mod.com/list/127707/spt-4013-part-1");

        Assert.Equal(22, page.Mods.Count(m => m.Card!.IsDependency));
        Assert.True(page.Mods.Single(m => m.Id == 902).Card!.IsDependency);

        var sain = page.Mods.Single(m => m.Id == 791).Card!;
        Assert.Equal(1314222, sain.Downloads);
        Assert.Equal("4.0.13", sain.SptVersion);
        Assert.Equal([("Waypoints - Expanded Navmesh", true), ("BigBrain", true)], sain.Dependencies.Select(d => (d.Name, d.OnList)));
    }

    [Fact]
    public void AnAddonCardHasItsThumbnailAndAuthorOnly()
    {
        var addon = Read("list-126308.html", AddonsList.ToString()).Addons.Single(a => a.Id == 135).Card!;

        Assert.StartsWith("https://files.sp-mod.com/addons/", addon.Thumbnail);
        Assert.Equal("TomiNyxer", addon.Author);
        Assert.Null(addon.Downloads);
        Assert.Null(addon.SptVersion);
        Assert.Empty(addon.Dependencies);
    }

    [Fact]
    public void ADetachedParentsCardTravelsWithItsAddon()
    {
        var html = Page("1 addon", Group("detached-77", Mod(2706, "ORBIT 2.0"), Addon(77, 135, "Live-Like ORBIT")));

        var addon = Assert.Single(SpModListImport.Parse(html, AddonsList).Page!.Addons);

        Assert.Equal("https://sp-mod.com/mod/2706/x", addon.ParentCard!.Url);
        Assert.Equal("4.1.6", addon.ParentCard.SptVersion);
    }

    [Fact]
    public void APageForAnyModOrAddonCanBeLinkedWithoutItsSlug()
    {
        Assert.Equal("https://sp-mod.com/mod/791/-", SpModListImport.PageFor(false, 791));
        Assert.Equal("https://sp-mod.com/addon/135/-", SpModListImport.PageFor(true, 135));
    }

    // ---- shapes the real pages above don't happen to contain ----

    [Fact]
    public void ADetachedCardsModIsNotAnEntryButIsItsAddonsParent()
    {
        var html = Page("1 addon", Group("detached-77", Mod(2706, "ORBIT 2.0"), Addon(77, 135, "Live-Like ORBIT")));

        var page = SpModListImport.Parse(html, AddonsList).Page!;

        Assert.Empty(page.Mods);
        var addon = Assert.Single(page.Addons);
        Assert.Equal(2706, addon.ParentModId);
        Assert.False(addon.ParentOnList);
        Assert.Empty(page.Notices);
    }

    [Fact]
    public void OptedOutEntriesAreNotedAndSkipped()
    {
        var html = Page("1 mod · 1 addon",
            Group("10", null),
            Group("11", Mod(2706, "ORBIT 2.0"), OptedOutAddon(12), Addon(13, 135, "Live-Like ORBIT")));

        var page = SpModListImport.Parse(html, AddonsList).Page!;

        Assert.Equal([2706], page.Mods.Select(m => m.Id));
        Assert.Equal([135], page.Addons.Select(a => a.Id));
        Assert.Equal(2, page.Notices.Count(n => n.Kind == SpModListNoticeKind.OptedOut));
        Assert.DoesNotContain(page.Notices, n => n.Kind == SpModListNoticeKind.CountMismatch);
    }

    [Fact]
    public void UnavailableEntriesAreCountedByTheHeaderAndAllowedFor()
    {
        const string goneMod = "<div wire:key=\"list-group-20\"><div class=\"text-sm italic\">This mod is no longer available.</div></div>";
        const string goneAddon = "<li wire:key=\"list-addon-22\"><div class=\"text-sm italic\">This addon is no longer available.</div></li>";

        var html = Page("2 mods · 1 addon", goneMod, Group("21", Mod(2706, "ORBIT 2.0"), goneAddon));

        var page = SpModListImport.Parse(html, AddonsList).Page!;

        Assert.Equal(SpModListReadPass.Structural, page.Pass);
        Assert.Equal([2706], page.Mods.Select(m => m.Id));
        Assert.Empty(page.Addons);
        Assert.Equal(2, page.Notices.Count(n => n.Kind == SpModListNoticeKind.Unavailable));
        Assert.DoesNotContain(page.Notices, n => n.Kind == SpModListNoticeKind.CountMismatch);
    }

    [Fact]
    public void TheCloudflareBeaconOnANormalPageIsNotAChallenge()
    {
        var html = Page("1 mod", Group("1", Mod(1, "One")))
            .Replace("</body>", "<script src=\"/cdn-cgi/challenge-platform/scripts/jsd/main.js\"></script></body>");

        Assert.True(SpModListImport.Parse(html, AddonsList).Succeeded);
    }

    [Fact]
    public void AShortfallTriesTheFallbackAndReportsTheGap()
    {
        var html = Page("3 mods", Group("1", Mod(1, "One")), Group("2", Mod(2, "Two")));

        var page = SpModListImport.Parse(html, AddonsList).Page!;

        Assert.Equal(SpModListReadPass.Fallback, page.Pass);
        var gap = Assert.Single(page.Notices, n => n.Kind == SpModListNoticeKind.CountMismatch);
        Assert.Equal(SpModListItemKind.Mod, gap.ItemKind);
        Assert.Equal(3, gap.Expected);
        Assert.Equal(2, gap.Read);
    }

    [Fact]
    public void AnEntryWithNoVersionIsKeptAndNoted()
    {
        var page = SpModListImport.Parse(Page("1 mod", Group("1", Mod(1, "One", version: null))), AddonsList).Page!;

        Assert.Null(Assert.Single(page.Mods).Version);
        var notice = Assert.Single(page.Notices);
        Assert.Equal(SpModListNoticeKind.MissingVersion, notice.Kind);
        Assert.Equal("One", notice.Name);
    }

    [Fact]
    public void TheNotCompatibleBadgeIsCarried()
    {
        const string badge = "<div data-flux-badge>Not compatible</div>";
        var html = Page("2 mods", Group("1", Mod(1, "Old", extra: badge)), Group("2", Mod(2, "Fine")));

        var page = SpModListImport.Parse(html, AddonsList).Page!;

        Assert.True(Assert.Single(page.Mods, m => m.Id == 1).NotCompatible);
        Assert.False(Assert.Single(page.Mods, m => m.Id == 2).NotCompatible);
    }

    [Fact]
    public void AnEmptyListIsAnEmptyListNotAFailure()
    {
        var read = SpModListImport.Parse(Page(""), AddonsList);

        Assert.True(read.Succeeded);
        Assert.Empty(read.Page!.Items);
        Assert.Empty(read.Page.Notices);
        Assert.Equal("4.1.6", read.Page.SptVersion);
    }

    [Fact]
    public void AChallengePageIsBlocked()
    {
        const string html = "<html><head><title>Just a moment...</title></head><body><script>window._cf_chl_opt={};</script></body></html>";

        Assert.Equal(SpModListFailure.Blocked, SpModListImport.Parse(html, AddonsList).Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><head><title>Mods - The Forge</title></head><body><a href=\"/mod/1/x\">One</a></body></html>")]
    public void APageThatIsNotAListIsRefused(string html)
    {
        Assert.Equal(SpModListFailure.NotAListPage, SpModListImport.Parse(html, AddonsList).Failure);
    }

    // ---- into a stored list ----

    [Fact]
    public void ThePageBecomesAnAdditiveImportedList()
    {
        var page = Read("list-126308.html", "https://sp-mod.com/list/126308/addons?share=secret");
        var now = new DateTimeOffset(2026, 10, 4, 18, 0, 0, TimeSpan.Zero);

        var list = page.ToModList("fallback", now);

        Assert.Equal(SpModListImport.ListIdFor(126308), list.Id);
        Assert.Equal("Addons", list.Name);
        Assert.Equal(ModListOrigin.Imported, list.Origin);
        Assert.Equal(ModListPolicy.Additive, list.Policy);
        Assert.Equal("Kryptic-S01", list.Source);
        Assert.Null(list.SptVersion);
        Assert.Null(list.Description);
        Assert.Equal(126308, list.SpModSource!.ListId);
        Assert.Equal("https://sp-mod.com/list/126308/addons?share=secret", list.SpModSource.Url);
        Assert.Equal(now, list.SpModSource.ReadAt);
        Assert.Equal(page.UpdatedAt, list.SpModSource.PageUpdatedAt);
        Assert.Empty(list.SpModSource.Excluded);
        Assert.Empty(list.SpModSource.Added);
        Assert.Equal(now, list.CreatedAt);
        Assert.Equal(page.UpdatedAt, list.UpdatedAt);
        Assert.Equal(11, list.Entries.Count);

        var addon = Assert.Single(list.Entries, e => e.ModId == 135);
        Assert.True(addon.IsAddon);
        Assert.Equal("1.3.0", addon.Version);
        Assert.Null(addon.VersionId);

        Assert.False(Assert.Single(list.Entries, e => e.ModId == 2706).IsAddon);
    }

    [Fact]
    public void ANamelessPageTakesTheFallbackName()
    {
        var html = Page("1 mod", Group("1", Mod(1, "One"))).Replace("Test list - Mod List - The Forge", "");

        var list = SpModListImport.Parse(html, AddonsList).Page!.ToModList("sp-mod list 126308", DateTimeOffset.UnixEpoch);

        Assert.Equal("sp-mod list 126308", list.Name);
    }

    [Fact]
    public void TheStoredListSurvivesTheShareFile()
    {
        var list = Read("list-126308.html", AddonsList.ToString()).ToModList("x", DateTimeOffset.UnixEpoch);

        var back = ModListFile.Read(ModListFile.Write(list)).List!;

        Assert.Equal(list.Id, back.Id);
        Assert.Equal(list.Entries.Select(e => (e.ModId, e.IsAddon, e.Version)), back.Entries.Select(e => (e.ModId, e.IsAddon, e.Version)));
        Assert.Equal(ModListFile.AddonSchemaVersion, ModListFile.SchemaVersionFor(list));
    }

    // ---- the request ----

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Html(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    [Fact]
    public async Task AReadSendsOneRequestAndParsesTheAnswer()
    {
        var handler = new Handler(_ => Html(HttpStatusCode.OK, Fixture("list-126308.html")));
        using var http = new HttpClient(handler);

        var read = await SpModListImport.ReadAsync(" sp-mod.com/list/126308/addons?share=abc ", http, "TCFModManager/test");

        Assert.True(read.Succeeded);
        Assert.Equal(11, read.Page!.Items.Count);
        Assert.Equal("https://sp-mod.com/list/126308/addons?share=abc", handler.Last!.RequestUri!.ToString());
        Assert.Equal("TCFModManager/test", handler.Last.Headers.UserAgent.ToString());
        Assert.Equal("https://sp-mod.com/", handler.Last.Headers.Referrer!.ToString());
    }

    [Fact]
    public async Task ABadAddressIsRefusedWithoutARequest()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("no request expected"));
        using var http = new HttpClient(handler);

        var read = await SpModListImport.ReadAsync("https://sp-mod.com/mod/2706/orbit", http, "ua");

        Assert.Equal(SpModListFailure.NotAListUrl, read.Failure);
        Assert.Null(handler.Last);
    }

    [Fact]
    public async Task AMissingListIsAnHttpStatus()
    {
        using var http = new HttpClient(new Handler(_ => Html(HttpStatusCode.NotFound, "<html>404</html>")));

        var read = await SpModListImport.ReadAsync(AddonsList.ToString(), http, "ua");

        Assert.Equal(SpModListFailure.HttpStatus, read.Failure);
        Assert.Equal(HttpStatusCode.NotFound, read.Status);
    }

    [Fact]
    public async Task ANotFoundPageWithTheBeaconIsStillNotFound()
    {
        const string body = "<html><head><title>Page Not Found - The Forge</title></head><body><script src=\"/cdn-cgi/challenge-platform/x.js\"></script></body></html>";
        using var http = new HttpClient(new Handler(_ => Html(HttpStatusCode.NotFound, body)));

        var read = await SpModListImport.ReadAsync(AddonsList.ToString(), http, "ua");

        Assert.Equal(SpModListFailure.HttpStatus, read.Failure);
    }

    [Fact]
    public async Task AChallengeStatusIsBlocked()
    {
        using var http = new HttpClient(new Handler(_ =>
            Html(HttpStatusCode.Forbidden, "<html><head><title>Just a moment...</title></head></html>")));

        var read = await SpModListImport.ReadAsync(AddonsList.ToString(), http, "ua");

        Assert.Equal(SpModListFailure.Blocked, read.Failure);
    }

    [Fact]
    public async Task NoConnectionIsANetworkFailure()
    {
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("offline")));

        var read = await SpModListImport.ReadAsync(AddonsList.ToString(), http, "ua");

        Assert.Equal(SpModListFailure.Network, read.Failure);
        Assert.Equal("offline", read.Detail);
    }
}
