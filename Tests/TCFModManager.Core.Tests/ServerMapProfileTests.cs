using System.Net;
using System.Text;
using TCFModManager.Core.ServerMap;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ServerMapProfileTests
{
    private static readonly ServerMapEndpoint Keyed = new("192.168.1.10", 6969, "AABB", "KEY1-KEY2");

    private const string Profile = """
        { "info": { "id": "6ac8acae28793133fcefe63e", "username": "Zero", "edition": "SPT Zero to hero" },
          "characters": {
            "pmc": {
              "Info": { "Nickname": "TheCrimsonZero", "Side": "Bear", "Level": 5, "Experience": 12000 },
              "Inventory": { "items": [ { "_id": "a", "_tpl": "5449016a4bdc2d6f028b456f", "upd": { "StackObjectsCount": 250000 } } ] },
              "Stats": { "Eft": { "TotalInGameTime": 3600, "OverallCounters": { "Items": [ { "Key": ["Sessions", "Pmc"], "Value": 4 } ] } } }
            },
            "scav": {}
          } }
        """;

    private static ServerMapClient Client(HttpStatusCode status, string body, List<string>? asked = null) =>
        ServerMapClient.TryCreate(Keyed, new Canned(status, body, asked))!;

    [Fact]
    public async Task AProfileComesBackAsTheLaunchersFigures()
    {
        var asked = new List<string>();
        using var client = Client(HttpStatusCode.OK,
            $$"""{"protocol":1,"profileId":"6ac8acae28793133fcefe63e","written":1794500000000,"unchanged":false,"busy":false,"profile":{{Profile}}}""",
            asked);

        var result = await client.ProfileAsync("6ac8acae28793133fcefe63e", null);

        Assert.True(result.Succeeded);
        Assert.Equal(ServerMapProfileState.Found, result.State);
        Assert.Equal(1794500000000, result.Written);
        Assert.Equal("TheCrimsonZero", result.Stats!.Nickname);
        Assert.Equal(250000, result.Stats.Money.Roubles);
        Assert.Equal(4, result.Stats.Pmc.Raids);
        Assert.Equal("/tcfservermap/profile?id=6ac8acae28793133fcefe63e", asked.Single());
    }

    [Fact]
    public async Task TheHeldCopysWriteTimeIsSentAndUnchangedComesBackWithoutAProfile()
    {
        var asked = new List<string>();
        using var client = Client(HttpStatusCode.OK,
            """{"protocol":1,"profileId":"6ac8acae28793133fcefe63e","written":1794500000000,"unchanged":true,"busy":false,"profile":null}""",
            asked);

        var result = await client.ProfileAsync("6ac8acae28793133fcefe63e", 1794500000000);

        Assert.Equal(ServerMapProfileState.Unchanged, result.State);
        Assert.Null(result.Stats);
        Assert.EndsWith("&since=1794500000000", asked.Single());
    }

    [Theory]
    [InlineData("""{"protocol":1,"profileId":"6ac8acae28793133fcefe63e","written":null,"unchanged":false,"busy":false,"profile":null}""", ServerMapProfileState.Missing)]
    [InlineData("""{"protocol":1,"profileId":"6ac8acae28793133fcefe63e","written":null,"unchanged":false,"busy":true,"profile":null}""", ServerMapProfileState.Busy)]
    public async Task MissingAndBusyAreAnswersNotFailures(string body, ServerMapProfileState state)
    {
        using var client = Client(HttpStatusCode.OK, body);

        var result = await client.ProfileAsync("6ac8acae28793133fcefe63e", null);

        Assert.True(result.Succeeded);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public async Task AServerMapWithoutTheRouteIsAnOlderMod()
    {
        using var client = Client(HttpStatusCode.NotFound, """{"protocol":1,"error":"No route '/profile'."}""");

        Assert.Equal(ServerMapProblem.ProfileUnsupported, (await client.ProfileAsync("6ac8acae28793133fcefe63e", null)).Problem);
    }

    [Fact]
    public async Task AHiddenProfileIsAnAnswerWithNoFigures()
    {
        using var client = Client(HttpStatusCode.OK, """{"protocol":1,"profileId":"6ac8acae28793133fcefe63e","hidden":true,"profile":null}""");

        var result = await client.ProfileAsync("6ac8acae28793133fcefe63e", null, "11111111-1111-1111-1111-111111111111");

        Assert.True(result.Succeeded);
        Assert.Equal(ServerMapProfileState.Hidden, result.State);
        Assert.Null(result.Stats);
    }

    [Fact]
    public async Task TheHostsRulesAndThisMachinesProfilesAreRead()
    {
        using var client = Client(HttpStatusCode.OK,
            """{"protocol":1,"hideOtherProfiles":true,"protectOtherProfiles":false,"owned":["6ac8acae28793133fcefe63e"]}""");

        var result = await client.ProfileRulesAsync("11111111-1111-1111-1111-111111111111");

        Assert.True(result.Succeeded);
        Assert.True(result.Rules!.HideOtherProfiles);
        Assert.False(result.Rules.ProtectOtherProfiles);
        Assert.True(result.Rules.Owns("6AC8ACAE28793133FCEFE63E"));
        Assert.False(result.Rules.Owns("6abee519842869498c34024b"));
    }

    [Fact]
    public async Task AServerMapWithoutRulesIsAnOlderMod()
    {
        using var client = Client(HttpStatusCode.NotFound, """{"protocol":1,"error":"No route '/profiles'."}""");

        Assert.Equal(ServerMapProblem.ProfileUnsupported, (await client.ProfileRulesAsync("11111111-1111-1111-1111-111111111111")).Problem);
    }

    [Theory]
    [InlineData("""{"protocol":1,"claimed":true,"reason":null}""", true, ServerMapClaimRefusal.None)]
    [InlineData("""{"protocol":1,"claimed":false,"reason":"tooOld"}""", false, ServerMapClaimRefusal.TooOld)]
    [InlineData("""{"protocol":1,"claimed":false,"reason":"ownedByAnother"}""", false, ServerMapClaimRefusal.OwnedByAnother)]
    public async Task AClaimSaysWhetherItTook(string body, bool claimed, ServerMapClaimRefusal refusal)
    {
        using var client = Client(HttpStatusCode.OK, body);

        var result = await client.ClaimAsync("11111111-1111-1111-1111-111111111111", "6ac8acae28793133fcefe63e");

        Assert.True(result.Succeeded);
        Assert.Equal(claimed, result.Claimed);
        Assert.Equal(refusal, result.Refusal);
    }

    private sealed class Canned(HttpStatusCode status, string body, List<string>? asked) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            asked?.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
