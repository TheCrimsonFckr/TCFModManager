using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

public enum CodePasteFailure
{
    Network,
    Rejected,
    BadResponse,
}

public sealed record CodePasteResult
{
    public string? Link { get; init; }

    public CodePasteFailure? Failure { get; init; }

    public string? ServerMessage { get; init; }

    public bool Succeeded => Link is not null;

    internal static CodePasteResult Failed(CodePasteFailure failure, string? message) =>
        new() { Failure = failure, ServerMessage = message };
}

public sealed partial class CodePasteClient : IDisposable
{
    public const string Expiry = "3months";

    internal const int Iterations = 100_000;
    internal const int KeyBytes = 32;
    internal const int SaltBytes = 8;
    internal const int IvBytes = 12;
    internal const int TagBytes = 16;

    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Uri _baseUri;

    public CodePasteClient(HttpClient? httpClient = null, Uri? baseUri = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _baseUri = baseUri ?? new Uri(SelfMod.CodePasteUrl);
    }

    [GeneratedRegex("^[0-9A-Za-z]+$")]
    private static partial Regex PasteId();

    public async Task<CodePasteResult> UploadAsync(string text, CancellationToken ct = default)
    {
        var key = RandomNumberGenerator.GetBytes(KeyBytes);
        var body = BuildRequest(
            text,
            key,
            RandomNumberGenerator.GetBytes(SaltBytes),
            RandomNumberGenerator.GetBytes(IvBytes),
            Expiry);

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Requested-With", "JSONHttpRequest");

        int httpStatus;
        string response;
        try
        {
            using var reply = await _http.SendAsync(request, ct).ConfigureAwait(false);
            httpStatus = (int)reply.StatusCode;
            response = await reply.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return CodePasteResult.Failed(CodePasteFailure.Network, ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return CodePasteResult.Failed(CodePasteFailure.Network, ex.Message);
        }

        return ReadResponse(response, httpStatus, key);
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }

    private CodePasteResult ReadResponse(string response, int httpStatus, byte[] key)
    {
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(response);
        }
        catch (JsonException)
        {
            json = null;
        }

        if (json is not JsonObject reply
            || reply["status"] is not JsonValue statusValue
            || !statusValue.TryGetValue<int>(out var status))
        {
            return CodePasteResult.Failed(CodePasteFailure.BadResponse, $"HTTP {httpStatus}");
        }

        if (status != 0)
        {
            var message = reply["message"] is JsonValue m && m.TryGetValue<string>(out var text) ? text : null;
            return CodePasteResult.Failed(CodePasteFailure.Rejected, message);
        }

        if (reply["id"] is not JsonValue idValue
            || !idValue.TryGetValue<string>(out var id)
            || !PasteId().IsMatch(id))
        {
            return CodePasteResult.Failed(CodePasteFailure.BadResponse, $"HTTP {httpStatus}");
        }

        return new CodePasteResult
        {
            Link = $"{_baseUri.GetLeftPart(UriPartial.Path)}?{id}#{Base58Encode(key)}",
        };
    }

    internal static string BuildRequest(string text, byte[] key, byte[] salt, byte[] iv, string expire)
    {
        var adata = Adata(iv, salt);
        var associated = Encoding.UTF8.GetBytes(adata.ToJsonString(Json));

        var deflated = Deflate(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { paste = text }, Json)));
        var cipher = new byte[deflated.Length + TagBytes];
        using (var gcm = new AesGcm(DeriveKey(key, salt, Iterations), TagBytes))
        {
            gcm.Encrypt(iv, deflated, cipher.AsSpan(0, deflated.Length), cipher.AsSpan(deflated.Length), associated);
        }

        return new JsonObject
        {
            ["v"] = 2,
            ["adata"] = adata,
            ["ct"] = Convert.ToBase64String(cipher),
            ["meta"] = new JsonObject { ["expire"] = expire },
        }.ToJsonString(Json);
    }

    internal static string Decrypt(string requestBody, byte[] key)
    {
        var body = JsonNode.Parse(requestBody)!;
        var adata = body["adata"]!;
        var spec = adata[0]!;

        var iv = Convert.FromBase64String(spec[0]!.GetValue<string>());
        var salt = Convert.FromBase64String(spec[1]!.GetValue<string>());
        var iterations = spec[2]!.GetValue<int>();
        var cipher = Convert.FromBase64String(body["ct"]!.GetValue<string>());

        var deflated = new byte[cipher.Length - TagBytes];
        using (var gcm = new AesGcm(DeriveKey(key, salt, iterations), TagBytes))
        {
            gcm.Decrypt(
                iv,
                cipher.AsSpan(0, deflated.Length),
                cipher.AsSpan(deflated.Length),
                deflated,
                Encoding.UTF8.GetBytes(adata.ToJsonString(Json)));
        }

        using var inflate = new DeflateStream(new MemoryStream(deflated), CompressionMode.Decompress);
        using var reader = new StreamReader(inflate, Encoding.UTF8);
        return JsonNode.Parse(reader.ReadToEnd())!["paste"]!.GetValue<string>();
    }

    internal static JsonArray Adata(byte[] iv, byte[] salt) => new(
        new JsonArray(
            Convert.ToBase64String(iv),
            Convert.ToBase64String(salt),
            Iterations,
            KeyBytes * 8,
            TagBytes * 8,
            "aes",
            "gcm",
            "zlib"),
        "plaintext",
        0,
        0);

    internal static string AdataText(byte[] iv, byte[] salt) => Adata(iv, salt).ToJsonString(Json);

    internal static string Base58Encode(ReadOnlySpan<byte> data)
    {
        var n = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        var text = new StringBuilder();
        while (!n.IsZero)
        {
            n = BigInteger.DivRem(n, 58, out var digit);
            text.Insert(0, Base58Alphabet[(int)digit]);
        }

        foreach (var b in data)
        {
            if (b != 0) break;
            text.Insert(0, '1');
        }

        return text.ToString();
    }

    internal static byte[] Base58Decode(string text)
    {
        BigInteger n = BigInteger.Zero;
        foreach (var c in text)
        {
            var digit = Base58Alphabet.IndexOf(c);
            if (digit < 0) throw new FormatException($"'{c}' is not a base58 character.");
            n = n * 58 + digit;
        }

        var leadingZeros = 0;
        while (leadingZeros < text.Length && text[leadingZeros] == '1') leadingZeros++;

        byte[] value = n.IsZero ? [] : n.ToByteArray(isUnsigned: true, isBigEndian: true);
        var result = new byte[leadingZeros + value.Length];
        value.CopyTo(result, leadingZeros);
        return result;
    }

    private static byte[] DeriveKey(byte[] key, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(key, salt, iterations, HashAlgorithmName.SHA256, KeyBytes);

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }
}
