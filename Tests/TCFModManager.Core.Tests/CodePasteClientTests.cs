using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class CodePasteClientTests
{
    private const string Text = "TCF Mod Manager 1.28.0-beta\nGrüße, Привет, ✓\nline with ``` in it\n";

    private static readonly byte[] PlusIv = [.. Enumerable.Repeat(new byte[] { 0xFB, 0xEF, 0xBE }, 4).SelectMany(b => b)];
    private static readonly byte[] SlashSalt = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    [Fact]
    public void A_request_decrypts_back_to_the_text()
    {
        var key = RandomNumberGenerator.GetBytes(CodePasteClient.KeyBytes);
        var body = CodePasteClient.BuildRequest(Text, key, RandomNumberGenerator.GetBytes(8), RandomNumberGenerator.GetBytes(12), "5min");

        Assert.Equal(Text, CodePasteClient.Decrypt(body, key));
    }

    [Fact]
    public void A_request_does_not_decrypt_with_another_key()
    {
        var body = CodePasteClient.BuildRequest(
            Text, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(8), RandomNumberGenerator.GetBytes(12), "5min");

        Assert.ThrowsAny<CryptographicException>(() => CodePasteClient.Decrypt(body, RandomNumberGenerator.GetBytes(32)));
    }

    [Fact]
    public void The_adata_is_in_privatebin_order_with_plus_and_slash_unescaped()
    {
        Assert.Equal("++++++++++++++++", Convert.ToBase64String(PlusIv));

        var adata = CodePasteClient.AdataText(PlusIv, SlashSalt);

        Assert.Equal(
            "[[\"++++++++++++++++\",\"//////////8=\",100000,256,128,\"aes\",\"gcm\",\"zlib\"],\"plaintext\",0,0]",
            adata);
    }

    [Fact]
    public void The_request_carries_the_adata_exactly_as_it_was_authenticated()
    {
        var body = CodePasteClient.BuildRequest(Text, RandomNumberGenerator.GetBytes(32), SlashSalt, PlusIv, "5min");

        Assert.Contains(CodePasteClient.AdataText(PlusIv, SlashSalt), body);
        Assert.DoesNotContain("\\u002B", body, StringComparison.OrdinalIgnoreCase);

        var json = JsonNode.Parse(body)!;
        Assert.Equal(2, json["v"]!.GetValue<int>());
        Assert.Equal("5min", json["meta"]!["expire"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("48656c6c6f20576f726c6421", "2NEpo7TZRRrLZSi2U")]
    [InlineData("0000287fb4cd", "11233QC4")]
    [InlineData("00", "1")]
    public void Base58_matches_known_vectors(string hex, string expected)
    {
        var bytes = Convert.FromHexString(hex);

        Assert.Equal(expected, CodePasteClient.Base58Encode(bytes));
        Assert.Equal(bytes, CodePasteClient.Base58Decode(expected));
    }

    [Fact]
    public void A_key_with_leading_zeros_survives_base58()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key.AsSpan(2));

        Assert.Equal(key, CodePasteClient.Base58Decode(CodePasteClient.Base58Encode(key)));
    }

    [Fact]
    public async Task A_successful_upload_returns_a_link_whose_key_opens_the_paste()
    {
        var handler = new CapturingHandler(() => Json(
            HttpStatusCode.OK,
            """{"status":0,"id":"3bf549eb9bbe04a9","url":"/?3bf549eb9bbe04a9","deletetoken":"d3l3t3t0k3n"}"""));
        using var client = new CodePasteClient(new HttpClient(handler));

        var result = await client.UploadAsync(Text);

        Assert.True(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.Matches(@"^https://codepaste\.sp-mod\.com/\?3bf549eb9bbe04a9#[1-9A-HJ-NP-Za-km-z]+$", result.Link);
        Assert.DoesNotContain("d3l3t3t0k3n", result.Link);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("JSONHttpRequest", handler.Request.Headers.GetValues("X-Requested-With").Single());
        Assert.Equal(CodePasteClient.Expiry, JsonNode.Parse(handler.Body!)!["meta"]!["expire"]!.GetValue<string>());

        var key = CodePasteClient.Base58Decode(result.Link![(result.Link!.IndexOf('#') + 1)..]);
        Assert.Equal(CodePasteClient.KeyBytes, key.Length);
        Assert.Equal(Text, CodePasteClient.Decrypt(handler.Body!, key));
    }

    [Fact]
    public async Task An_error_status_is_rejected_with_the_server_message()
    {
        var handler = new CapturingHandler(() => Json(HttpStatusCode.OK, """{"status":1,"message":"Invalid data."}"""));
        using var client = new CodePasteClient(new HttpClient(handler));

        var result = await client.UploadAsync(Text);

        Assert.False(result.Succeeded);
        Assert.Equal(CodePasteFailure.Rejected, result.Failure);
        Assert.Equal("Invalid data.", result.ServerMessage);
    }

    [Theory]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("")]
    [InlineData("""{"id":"3bf549eb9bbe04a9"}""")]
    [InlineData("""{"status":0,"id":"../evil"}""")]
    [InlineData("""{"status":0}""")]
    public async Task A_response_that_is_not_a_privatebin_answer_is_a_bad_response(string body)
    {
        var handler = new CapturingHandler(() => Json(HttpStatusCode.BadGateway, body));
        using var client = new CodePasteClient(new HttpClient(handler));

        var result = await client.UploadAsync(Text);

        Assert.Equal(CodePasteFailure.BadResponse, result.Failure);
        Assert.Null(result.Link);
    }

    [Fact]
    public async Task A_network_failure_is_reported_not_thrown()
    {
        var handler = new CapturingHandler(() => throw new HttpRequestException("No such host is known."));
        using var client = new CodePasteClient(new HttpClient(handler));

        var result = await client.UploadAsync(Text);

        Assert.Equal(CodePasteFailure.Network, result.Failure);
        Assert.Equal("No such host is known.", result.ServerMessage);
    }

    [Fact]
    public async Task Cancelling_still_throws()
    {
        var handler = new CapturingHandler(() => Json(HttpStatusCode.OK, "{}"));
        using var client = new CodePasteClient(new HttpClient(handler));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.UploadAsync(Text, cancel.Token));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class CapturingHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond();
        }
    }
}
