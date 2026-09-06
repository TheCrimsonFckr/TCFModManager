using System.Net;
using System.Text;
using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ServerMapEndpointTests
{
    [Theory]
    [InlineData("192.168.1.111", 6969, "https://192.168.1.111:6969/")]
    [InlineData("spt.example.com", 6969, "https://spt.example.com:6969/")]
    [InlineData("  192.168.1.111  ", 6969, "https://192.168.1.111:6969/")]
    public void BuildsABaseUri(string host, int port, string expected)
    {
        Assert.True(new ServerMapEndpoint(host, port).TryGetBaseUri(out var uri));
        Assert.Equal(expected, uri.ToString());
    }

    //
    // UriBuilder does not bracket an IPv6 literal for you - "::1" comes out as "https://::1:6969",
    // which does not parse. An address handed out for a server reached over the internet can easily
    // be IPv6, so this is not a curiosity.
    //
    [Theory]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("fd00::1")]
    public void BracketsAnIPv6Literal(string host)
    {
        Assert.True(new ServerMapEndpoint(host, 6969).TryGetBaseUri(out var uri));
        Assert.Contains("[", uri.ToString());
        Assert.Equal(6969, uri.Port);
    }

    [Theory]
    [InlineData("", 6969)]
    [InlineData("   ", 6969)]
    [InlineData("192.168.1.111", 0)]
    [InlineData("192.168.1.111", 70000)]
    [InlineData("192.168.1.111", -1)]
    public void RefusesAnUnusableEndpoint(string host, int port) =>
        Assert.False(new ServerMapEndpoint(host, port).TryGetBaseUri(out _));

    [Fact]
    public void HasPinIsFalseUntilOneIsRecorded()
    {
        Assert.False(new ServerMapEndpoint("h", 1).HasPin);
        Assert.False(new ServerMapEndpoint("h", 1, "   ").HasPin);
        Assert.True(new ServerMapEndpoint("h", 1, "AABB").HasPin);
    }
}

//
// The pin decision, tested as the pure function it is. SPT's certificate fails the chain and
// hostname checks at any remote address - always - so the thumbprint is the entire check. Getting
// this wrong either locks everyone out or trusts anything that answers.
//
public class ServerCertificatePinTests
{
    private const string A = "6D6B779B72514954";
    private const string B = "0000000000000000";

    [Fact]
    public void NoPinYet_IsTrustOnFirstUse()
    {
        Assert.Equal(PinVerdict.FirstUse, ServerCertificatePin.Decide(null, A));
        Assert.Equal(PinVerdict.FirstUse, ServerCertificatePin.Decide("", A));
        Assert.Equal(PinVerdict.FirstUse, ServerCertificatePin.Decide("   ", A));
    }

    [Fact]
    public void SameThumbprint_Matches() => Assert.Equal(PinVerdict.Match, ServerCertificatePin.Decide(A, A));

    // Thumbprints get copied by hand and between tools; casing must not decide access.
    [Fact]
    public void CasingDoesNotDecideAccess() =>
        Assert.Equal(PinVerdict.Match, ServerCertificatePin.Decide(A.ToLowerInvariant(), A.ToUpperInvariant()));

    [Fact]
    public void DifferentThumbprint_IsAMismatch() =>
        Assert.Equal(PinVerdict.Mismatch, ServerCertificatePin.Decide(A, B));

    [Fact]
    public void NoCertificateAtAll_IsNeverTrusted()
    {
        Assert.Equal(PinVerdict.NoCertificate, ServerCertificatePin.Decide(A, null));
        Assert.Equal(PinVerdict.NoCertificate, ServerCertificatePin.Decide(null, null));
    }

    [Theory]
    [InlineData(PinVerdict.FirstUse, true)]
    [InlineData(PinVerdict.Match, true)]
    [InlineData(PinVerdict.Mismatch, false)]
    [InlineData(PinVerdict.NoCertificate, false)]
    public void OnlyFirstUseAndMatchAreAccepted(PinVerdict verdict, bool accepted) =>
        Assert.Equal(accepted, ServerCertificatePin.Accepts(verdict));
}

public class ServerHelloTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // The exact body the spike server returned, so the parser is held to the real wire shape rather
    // than to one written to match it.
    private const string RealBody = """
        {
          "protocol": 1,
          "modVersion": "0.1.0",
          "serverName": "CORE-01",
          "requiresKey": false,
          "hasList": false,
          "capabilities": [ "hello", "echo" ],
          "payloadPath": "G:\\Single Player Tarkov 4.1\\TCFModManager\\ServerMap\\payload\\TCFMM.ServerMap.Payload.dll"
        }
        """;

    [Fact]
    public void ParsesTheHandshakeTheSpikeServerActuallySent()
    {
        var hello = JsonSerializer.Deserialize<ServerHello>(RealBody, Json)!;

        Assert.Equal(1, hello.Protocol);
        Assert.Equal("0.1.0", hello.ModVersion);
        Assert.Equal("CORE-01", hello.ServerName);
        Assert.False(hello.RequiresKey);
        Assert.False(hello.HasList);
        Assert.Null(hello.ListRevision);
        Assert.True(hello.Supports("hello"));
        Assert.True(hello.Supports("HELLO"));
        Assert.False(hello.Supports("list"));
    }

    // Unknown fields must not throw - a newer server carries more than this build knows about, and
    // payloadPath above is already one of them.
    [Fact]
    public void IgnoresFieldsItDoesNotKnow()
    {
        var hello = JsonSerializer.Deserialize<ServerHello>(
            """{ "protocol": 1, "somethingNew": { "a": 1 }, "capabilities": [] }""", Json)!;

        Assert.Equal(1, hello.Protocol);
    }

    [Fact]
    public void CapabilitiesDefaultToEmptyRatherThanNull()
    {
        var hello = JsonSerializer.Deserialize<ServerHello>("""{ "protocol": 1 }""", Json)!;

        Assert.NotNull(hello.Capabilities);
        Assert.False(hello.Supports("hello"));
    }
}

//
// The response-handling half, over a stub handler. Every branch below is something a user will
// actually hit: a plain SPT server on the address, a stub whose payload is gone, a server newer
// than this build, and a machine that is simply off.
//
public class ServerMapClientTests
{
    private static readonly ServerMapEndpoint Endpoint = new("192.168.1.111", 6969);

    private static async Task<ServerHelloProbe> Probe(HttpStatusCode status, string body)
    {
        using var client = ServerMapClient.TryCreate(Endpoint, new StubHandler(status, body))!;
        return await client.HelloAsync();
    }

    [Fact]
    public void TryCreateRefusesAnAddressItCannotDial() =>
        Assert.Null(ServerMapClient.TryCreate(new ServerMapEndpoint("", 6969)));

    [Fact]
    public void TryCreateBuildsAClientForAUsableAddress()
    {
        using var client = ServerMapClient.TryCreate(Endpoint);
        Assert.NotNull(client);
    }

    [Fact]
    public async Task AHandshakeIsReadBackWhole()
    {
        var probe = await Probe(HttpStatusCode.OK,
            """{ "protocol": 1, "serverName": "CORE-01", "hasList": true, "listRevision": 7 }""");

        Assert.True(probe.Found);
        Assert.Equal(ServerMapProblem.None, probe.Problem);
        Assert.Equal("CORE-01", probe.Hello!.ServerName);
        Assert.Equal(7, probe.Hello.ListRevision);
    }

    //
    // A plain SPT server 404s the route and a stub whose payload folder has been emptied 503s it.
    // Both mean "no server map here" and neither deserves an error the user has to interpret.
    //
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnUnhappyStatusIsNotAServerMap(HttpStatusCode status)
    {
        var probe = await Probe(status, "");

        Assert.False(probe.Found);
        Assert.Equal(ServerMapProblem.NotServerMap, probe.Problem);
        Assert.Equal((int)status, probe.StatusCode);
    }

    // Some other service on that port answering 200 - the port is a user setting and gets mistyped.
    [Theory]
    [InlineData("<html>hello</html>")]
    [InlineData("")]
    [InlineData("""{ "message": "pong" }""")]
    public async Task A200ThatIsNotAHandshakeIsNotAServerMap(string body) =>
        Assert.Equal(ServerMapProblem.NotServerMap, (await Probe(HttpStatusCode.OK, body)).Problem);

    //
    // A server newer than this build is refused rather than half-understood, and the version it
    // speaks is carried out so the App can say which side needs updating.
    //
    [Fact]
    public async Task AProtocolThisBuildDoesNotSpeakIsRefusedAndNamed()
    {
        var probe = await Probe(HttpStatusCode.OK, """{ "protocol": 99, "serverName": "CORE-01" }""");

        Assert.False(probe.Found);
        Assert.Equal(ServerMapProblem.ProtocolMismatch, probe.Problem);
        Assert.Equal(99, probe.ServerProtocol);
    }

    [Fact]
    public async Task TheEndpointIsCarriedBackOnEveryOutcome()
    {
        Assert.Equal(Endpoint, (await Probe(HttpStatusCode.OK, """{ "protocol": 1 }""")).Endpoint);
        Assert.Equal(Endpoint, (await Probe(HttpStatusCode.NotFound, "")).Endpoint);
    }

    [Fact]
    public async Task AThrowingTransportIsReportedAsUnreachableNotThrown()
    {
        using var client = ServerMapClient.TryCreate(Endpoint, new ThrowingHandler())!;

        var probe = await client.HelloAsync();

        Assert.False(probe.Found);
        Assert.Equal(ServerMapProblem.Unreachable, probe.Problem);
        Assert.NotNull(probe.Error);
    }

    //
    // Nothing is listening on port 1, so this is the one test that goes through the real handler -
    // the pinning callback, UseProxy = false and all. It must come back as Unreachable rather than
    // throwing, and must not be mistaken for a rejected certificate.
    //
    [Fact]
    public async Task ARealSocketThatGoesNowhereIsReportedNotThrown()
    {
        using var client = ServerMapClient.TryCreate(
            new ServerMapEndpoint("127.0.0.1", 1), TimeSpan.FromSeconds(5))!;

        var probe = await client.HelloAsync();

        Assert.False(probe.Found);
        Assert.Equal(ServerMapProblem.Unreachable, probe.Problem);
        Assert.NotNull(probe.Error);
    }

    // Asks for the handshake route and nothing else, so a stub server's CanHandle prefix test is
    // being given what it was written against.
    [Fact]
    public async Task AsksForTheHandshakeRoute()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "protocol": 1 }""");
        using var client = ServerMapClient.TryCreate(Endpoint, handler)!;

        await client.HelloAsync();

        Assert.Equal("https://192.168.1.111:6969/tcfservermap/hello", handler.LastRequestUri?.ToString());
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no route to host");
    }
}

public class ServerMapSettingsTests
{
    //
    // Off by default, like the Mod footprint page. The map needs an SPT server running the Server
    // Map mod at the other end, which most installs will not have - so nobody gets a page that is
    // empty for them without asking for it.
    //
    [Fact]
    public void AFreshSettingsFileHasTheServerMapOffAndUnconfiguredOnTheDefaultPort()
    {
        var settings = new AppSettings();

        Assert.NotNull(settings.ServerMap);
        Assert.False(settings.ServerMap.ShowPage);
        Assert.False(settings.ServerMap.IsConfigured);
        Assert.Equal(ServerMapEndpoint.DefaultPort, settings.ServerMap.Port);
        Assert.Null(settings.ServerMap.PinnedThumbprint);
    }

    [Fact]
    public void ConfiguredSettingsBecomeAnEndpointThatCanBeDialled()
    {
        var settings = new ServerMapSettings { Host = "192.168.1.111", PinnedThumbprint = "AABB" };

        var endpoint = settings.ToEndpoint();

        Assert.True(settings.IsConfigured);
        Assert.True(endpoint.HasPin);
        Assert.True(endpoint.TryGetBaseUri(out var uri));
        Assert.Equal("https://192.168.1.111:6969/", uri.ToString());
    }

    // Round-trips through the same serializer settings.json uses, so an upgrading install keeps the
    // pin it already recorded rather than silently re-arming trust-on-first-use.
    [Fact]
    public void SurvivesARoundTripThroughJson()
    {
        var settings = new AppSettings
        {
            ServerMap = new ServerMapSettings
            {
                ShowPage = true,
                Host = "spt.example.com",
                Port = 7000,
                PinnedThumbprint = "AABB",
            },
        };

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;

        Assert.True(restored.ServerMap.ShowPage);
        Assert.Equal("spt.example.com", restored.ServerMap.Host);
        Assert.Equal(7000, restored.ServerMap.Port);
        Assert.Equal("AABB", restored.ServerMap.PinnedThumbprint);
    }

    //
    // An install upgrading from a build with no ServerMap key must not land with a null here - and
    // neither must one whose hand-edited settings.json says so outright, which is a thing that
    // happens to a file the Options page invites people to open.
    //
    [Theory]
    [InlineData("""{ "SptInstallPath": "C:\\SPT" }""")]
    [InlineData("""{ "ServerMap": null }""")]
    public void SettingsWithNoUsableServerMapSectionStillLoad(string json)
    {
        var restored = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.NotNull(restored.ServerMap);
        Assert.False(restored.ServerMap.ShowPage);
        Assert.False(restored.ServerMap.IsConfigured);
        Assert.Equal(ServerMapEndpoint.DefaultPort, restored.ServerMap.Port);
    }

    //
    // Turning the page off must not throw away the address or the recorded certificate: switching it
    // back on has to land where it was, not re-arm trust-on-first-use as though the server had never
    // been seen.
    //
    [Fact]
    public void SwitchingThePageOffKeepsTheAddressAndThePin()
    {
        var settings = new ServerMapSettings
        {
            ShowPage = true,
            Host = "127.0.0.1",
            PinnedThumbprint = "AABB",
        };

        settings.ShowPage = false;

        Assert.True(settings.IsConfigured);
        Assert.Equal("AABB", settings.ToEndpoint().PinnedThumbprint);
    }

    // The address Fika defaults to, and the one this was first tested against.
    [Fact]
    public void LoopbackIsADialableAddress()
    {
        var endpoint = new ServerMapSettings { Host = "127.0.0.1" }.ToEndpoint();

        Assert.True(endpoint.TryGetBaseUri(out var uri));
        Assert.Equal("https://127.0.0.1:6969/", uri.ToString());
    }
}
