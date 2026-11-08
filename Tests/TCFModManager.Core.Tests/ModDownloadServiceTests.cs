using System.Net;
using System.Net.Http.Headers;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// ModDownloadService against an in-memory server: breaking off part way, resuming with a range,
// refusing to join a file that changed, and giving up on a transfer that stalls.
//
public class ModDownloadServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcf-download-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _file = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray();

    public ModDownloadServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class Server(byte[] file) : HttpMessageHandler
    {
        public string ETag { get; set; } = "\"v1\"";

        public byte[] File { get; set; } = file;

        // The next response breaks off after this many bytes (null: sends everything).
        public int? BreakAfter { get; set; }

        // The next response sends nothing at all after its headers.
        public bool Stall { get; set; }

        // Honours Range but not If-Range (joins whatever the file is now).
        public bool IgnoreIfRange { get; set; }

        // Sends at most this many bytes of a range (servers that cap open-ended ranges).
        public int? CapRange { get; set; }

        // Answers a range with 416.
        public bool Refuse416 { get; set; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);

            var from = 0L;
            var partial = false;
            if (request.Headers.Range?.Ranges.FirstOrDefault() is { From: { } start }
                && (IgnoreIfRange || (request.Headers.TryGetValues("If-Range", out var ifRange) && ifRange.Single() == ETag)))
            {
                if (Refuse416)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable) { Content = new ByteArrayContent([]) });

                from = start;
                partial = true;
            }

            var body = File.AsSpan((int)from).ToArray();
            if (partial && CapRange is { } cap) body = body[..Math.Min(cap, body.Length)];
            Stream stream = Stall ? new StallingStream() : new BreakingStream(body, BreakAfter);
            BreakAfter = null;
            Stall = false;

            var response = new HttpResponseMessage(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
            response.Content.Headers.ContentLength = body.Length;
            if (partial) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, from + body.Length - 1, File.Length);
            response.Headers.ETag = EntityTagHeaderValue.Parse(ETag);
            response.Headers.AcceptRanges.Add("bytes");
            return Task.FromResult(response);
        }
    }

    private sealed class BreakingStream(byte[] data, int? breakAfter) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (breakAfter is { } limit && Position >= limit) throw new IOException("connection reset", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset));
            if (breakAfter is { } cap) count = (int)Math.Min(count, cap - Position);
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (breakAfter is { } limit && Position >= limit) throw new IOException("connection reset", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset));
            if (breakAfter is { } cap) buffer = buffer[..(int)Math.Min(buffer.Length, cap - Position)];
            return base.ReadAsync(buffer, ct);
        }
    }

    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
    }

    private sealed class Failing(HttpStatusCode status, string? answeredFrom = null, RetryConditionHeaderValue? retryAfter = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent([]),
                RequestMessage = answeredFrom is null ? request : new HttpRequestMessage(HttpMethod.Get, answeredFrom),
            };
            response.Headers.RetryAfter = retryAfter;
            return Task.FromResult(response);
        }
    }

    // Answers each path with its queued statuses in turn, then with the file.
    private sealed class Scripted(byte[] file, Dictionary<string, Queue<HttpStatusCode>> script) : HttpMessageHandler
    {
        public RetryConditionHeaderValue? RetryAfter { get; set; }

        public List<(string Path, DateTimeOffset At)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests) Requests.Add((path, DateTimeOffset.UtcNow));

            HttpStatusCode? status = null;
            lock (script)
                if (script.TryGetValue(path, out var queue) && queue.Count > 0) status = queue.Dequeue();

            var response = status is { } failed
                ? new HttpResponseMessage(failed) { Content = new ByteArrayContent([]) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) };
            response.RequestMessage = request;
            if (status is not null) response.Headers.RetryAfter = RetryAfter;
            return Task.FromResult(response);
        }
    }

    private sealed class Heard<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];

        public Action<T>? OnReport { get; set; }

        public void Report(T value)
        {
            lock (Items) Items.Add(value);
            OnReport?.Invoke(value);
        }
    }

    private static DownloadRetryPolicy Quick(int serverMs = 10) => new(
        [TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)],
        [TimeSpan.FromMilliseconds(serverMs), TimeSpan.FromMilliseconds(serverMs), TimeSpan.FromMilliseconds(serverMs)],
        TimeSpan.FromMilliseconds(50));

    private static Dictionary<string, Queue<HttpStatusCode>> Script(string path, params HttpStatusCode[] statuses) =>
        new() { [path] = new Queue<HttpStatusCode>(statuses) };

    private string Part => Path.Combine(_root, "mod.part");

    [Fact]
    public async Task ADownloadThatBrokeOff_CarriesOnFromWhereItStopped()
    {
        var server = new Server(_file) { BreakAfter = 70_000 };
        using var service = new ModDownloadService(new HttpClient(server));

        await Assert.ThrowsAnyAsync<Exception>(() => service.DownloadAsync("http://x/mod.zip", Part, resume: true));
        Assert.Equal(70_000, new FileInfo(Part).Length);

        await service.DownloadAsync("http://x/mod.zip", Part, resume: true);

        Assert.Equal(_file, File.ReadAllBytes(Part));
        Assert.Equal(70_000, server.Requests[^1].Headers.Range!.Ranges.Single().From);
        Assert.False(File.Exists(ModDownloadService.ValidatorPathFor(Part)));
    }

    [Fact]
    public async Task AFileThatChangedOnTheServer_IsDownloadedAgainFromTheStart()
    {
        var server = new Server(_file) { BreakAfter = 70_000 };
        using var service = new ModDownloadService(new HttpClient(server));
        await Assert.ThrowsAnyAsync<Exception>(() => service.DownloadAsync("http://x/mod.zip", Part, resume: true));

        var changed = _file.Select(b => (byte)(b ^ 0xFF)).ToArray();
        server.File = changed;
        server.ETag = "\"v2\"";

        await service.DownloadAsync("http://x/mod.zip", Part, resume: true);

        Assert.Equal(changed, File.ReadAllBytes(Part));
    }

    [Fact]
    public async Task WithoutResume_APartialFileIsReplaced()
    {
        File.WriteAllBytes(Part, [1, 2, 3]);
        File.WriteAllText(ModDownloadService.ValidatorPathFor(Part), "\"v1\"");
        var server = new Server(_file);
        using var service = new ModDownloadService(new HttpClient(server));

        await service.DownloadAsync("http://x/mod.zip", Part);

        Assert.Equal(_file, File.ReadAllBytes(Part));
        Assert.Null(server.Requests.Single().Headers.Range);
    }

    [Fact]
    public async Task ATransferThatStalls_GivesUpInsteadOfWaitingForEver()
    {
        var before = ModDownloadService.IdleTimeout;
        ModDownloadService.IdleTimeout = TimeSpan.FromMilliseconds(300);
        try
        {
            var server = new Server(_file) { Stall = true };
            using var service = new ModDownloadService(new HttpClient(server));

            var failure = await Assert.ThrowsAsync<HttpIOException>(() => service.DownloadAsync("http://x/mod.zip", Part, resume: true));
            Assert.Equal(HttpRequestError.ResponseEnded, failure.HttpRequestError);
        }
        finally
        {
            ModDownloadService.IdleTimeout = before;
        }
    }

    [Fact]
    public async Task CancellingIsStillCancelling_NotAStall()
    {
        var server = new Server(_file) { Stall = true };
        using var service = new ModDownloadService(new HttpClient(server));
        using var cancel = new CancellationTokenSource(200);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync("http://x/mod.zip", Part, ct: cancel.Token, resume: true));
    }

    private async Task<(Server Server, ModDownloadService Service)> BrokenOffAt70k()
    {
        var server = new Server(_file) { BreakAfter = 70_000 };
        var service = new ModDownloadService(new HttpClient(server));
        await Assert.ThrowsAnyAsync<Exception>(() => service.DownloadAsync("http://x/mod.zip", Part, resumable: true));
        return (server, service);
    }

    [Fact]
    public async Task OnlyPartOfTheRest_IsNotTakenForTheWholeFile_TheWholeFileIsAskedForInstead()
    {
        var (server, service) = await BrokenOffAt70k();
        server.CapRange = 10_000;

        await service.DownloadAsync("http://x/mod.zip", Part, resume: true, resumable: true);

        Assert.Equal(_file, File.ReadAllBytes(Part));
        Assert.Null(server.Requests[^1].Headers.Range);
    }

    [Fact]
    public async Task ARangeOfAFileThatChangedSize_IsNotJoined_EvenWhenIfRangeIsIgnored()
    {
        var (server, service) = await BrokenOffAt70k();
        server.IgnoreIfRange = true;
        server.ETag = "\"v2\"";
        var changed = (byte[])[.. _file, .. _file[..50_000]];
        server.File = changed;

        await service.DownloadAsync("http://x/mod.zip", Part, resume: true, resumable: true);

        Assert.Equal(changed, File.ReadAllBytes(Part));
    }

    [Fact]
    public async Task A416_StartsAgainAtOnce()
    {
        var (server, service) = await BrokenOffAt70k();
        server.Refuse416 = true;

        await service.DownloadAsync("http://x/mod.zip", Part, resume: true, resumable: true);

        Assert.Equal(_file, File.ReadAllBytes(Part));
    }

    [Fact]
    public async Task AServerError_SaysWhichHostSentIt()
    {
        using var service = new ModDownloadService(new HttpClient(new Failing(HttpStatusCode.GatewayTimeout)));

        var failure = await Assert.ThrowsAsync<DownloadHttpException>(
            () => service.DownloadAsync("https://sp-mod.com/mod/download/3261/pbt/1.0.0", Part, resumable: true));

        Assert.Equal(HttpStatusCode.GatewayTimeout, failure.Status);
        Assert.Equal(HttpStatusCode.GatewayTimeout, failure.StatusCode);
        Assert.Equal("sp-mod.com", failure.Host);
        Assert.True(failure.IsSpMod);
        Assert.True(failure.IsServerSide);
        Assert.Null(failure.RetryAfter);
        Assert.False(File.Exists(Part));
    }

    [Fact]
    public async Task AServerErrorAfterARedirect_NamesTheFileHost_NotSpMod()
    {
        var handler = new Failing(HttpStatusCode.BadGateway, "https://release-assets.githubusercontent.com/asset?sig=secret");
        using var service = new ModDownloadService(new HttpClient(handler));

        var failure = await Assert.ThrowsAsync<DownloadHttpException>(
            () => service.DownloadAsync("https://sp-mod.com/mod/download/3261/pbt/1.0.0", Part));

        Assert.Equal("release-assets.githubusercontent.com", failure.Host);
        Assert.False(failure.IsSpMod);
    }

    [Fact]
    public async Task RetryAfter_IsCarried_AsADeltaOrADate()
    {
        using var delta = new ModDownloadService(new HttpClient(
            new Failing(HttpStatusCode.ServiceUnavailable, retryAfter: new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)))));
        var fromDelta = await Assert.ThrowsAsync<DownloadHttpException>(() => delta.DownloadAsync("https://sp-mod.com/x", Part));
        Assert.Equal(TimeSpan.FromSeconds(30), fromDelta.RetryAfter);

        using var past = new ModDownloadService(new HttpClient(
            new Failing(HttpStatusCode.TooManyRequests, retryAfter: new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-5)))));
        var fromPast = await Assert.ThrowsAsync<DownloadHttpException>(() => past.DownloadAsync("https://sp-mod.com/x", Part));
        Assert.Equal(TimeSpan.Zero, fromPast.RetryAfter);
        Assert.True(fromPast.IsServerSide);
    }

    [Fact]
    public async Task ARefusal_IsNotServerSide()
    {
        using var service = new ModDownloadService(new HttpClient(new Failing(HttpStatusCode.NotFound)));

        var failure = await Assert.ThrowsAsync<DownloadHttpException>(() => service.DownloadAsync("https://files.sp-mod.com/x", Part));

        Assert.False(failure.IsServerSide);
        Assert.True(failure.IsSpMod);
    }

    [Fact]
    public async Task AServerError_IsTriedAgain_AndSaysSo()
    {
        var server = new Scripted(_file, Script("/a", HttpStatusCode.GatewayTimeout));
        using var service = new ModDownloadService(new HttpClient(server)) { RetryPolicy = Quick() };
        var heard = new Heard<DownloadRetry>();

        await service.DownloadRetryingAsync("https://sp-mod.com/a", Part, retrying: heard);

        Assert.Equal(_file, File.ReadAllBytes(Part));
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(
            [DownloadRetryState.Retrying, DownloadRetryState.Resumed],
            heard.Items.Select(r => r.State));
        Assert.Equal(DownloadRetryCause.Server, heard.Items[0].Cause);
        Assert.Equal("sp-mod.com", heard.Items[0].Host);
        Assert.Equal(1, heard.Items[0].Attempt);
    }

    [Fact]
    public async Task ServerErrors_GiveUpOnceTheServerScheduleIsUsedUp()
    {
        var server = new Scripted(_file, Script("/a", Enumerable.Repeat(HttpStatusCode.ServiceUnavailable, 10).ToArray()));
        using var service = new ModDownloadService(new HttpClient(server)) { RetryPolicy = Quick() };

        var failure = await Assert.ThrowsAsync<DownloadHttpException>(() => service.DownloadRetryingAsync("https://sp-mod.com/a", Part));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.Status);
        Assert.Equal(4, server.Requests.Count);
    }

    [Fact]
    public async Task ARefusal_IsNotTriedAgain()
    {
        var server = new Scripted(_file, Script("/a", HttpStatusCode.NotFound));
        using var service = new ModDownloadService(new HttpClient(server)) { RetryPolicy = Quick() };

        await Assert.ThrowsAsync<DownloadHttpException>(() => service.DownloadRetryingAsync("https://sp-mod.com/a", Part));

        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task RetryAfter_ReplacesTheWait_UpToTheCap()
    {
        var server = new Scripted(_file, Script("/a", HttpStatusCode.TooManyRequests))
        {
            RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)),
        };
        using var service = new ModDownloadService(new HttpClient(server)) { RetryPolicy = Quick() };
        var heard = new Heard<DownloadRetry>();

        await service.DownloadRetryingAsync("https://sp-mod.com/a", Part, retrying: heard);

        Assert.Equal(TimeSpan.FromMilliseconds(50), heard.Items[0].Wait);
    }

    [Fact]
    public async Task ABrokenConnection_IsTriedAgainOnTheConnectionSchedule_AndResumes()
    {
        var server = new Server(_file) { BreakAfter = 70_000 };
        using var service = new ModDownloadService(new HttpClient(server)) { RetryPolicy = Quick() };
        var heard = new Heard<DownloadRetry>();

        await service.DownloadRetryingAsync("http://x/mod.zip", Part, retrying: heard, resumable: true);

        Assert.Equal(_file, File.ReadAllBytes(Part));
        Assert.Equal(DownloadRetryCause.Connection, heard.Items[0].Cause);
        Assert.Equal(70_000, server.Requests[^1].Headers.Range!.Ranges.Single().From);
    }

    [Fact]
    public async Task AServerErrorOnOneDownload_HoldsTheOthersForTheSameHost()
    {
        var server = new Scripted(_file, Script("/a", HttpStatusCode.GatewayTimeout));
        using var service = new ModDownloadService(new HttpClient(server)) { RetryPolicy = Quick(serverMs: 1500) };
        var failed = new TaskCompletionSource();
        var first = new Heard<DownloadRetry>
        {
            OnReport = r => { if (r.State == DownloadRetryState.Retrying) failed.TrySetResult(); },
        };
        var second = new Heard<DownloadRetry>();

        var a = service.DownloadRetryingAsync("https://sp-mod.com/a", Path.Combine(_root, "a.part"), retrying: first);
        await failed.Task;
        var b = service.DownloadRetryingAsync("https://sp-mod.com/b", Path.Combine(_root, "b.part"), retrying: second);
        await Task.WhenAll(a, b);

        var held = Assert.Single(second.Items, r => r.State == DownloadRetryState.Held);
        Assert.Equal("sp-mod.com", held.Host);
        Assert.Equal(DownloadRetryState.Resumed, second.Items[^1].State);
        var asked = server.Requests.Single(r => r.Path == "/b").At;
        Assert.True(asked >= server.Requests.First(r => r.Path == "/a").At + TimeSpan.FromMilliseconds(1400));
    }

    [Fact]
    public async Task AnotherHost_IsNotHeld()
    {
        var server = new Scripted(_file, Script("/a", HttpStatusCode.GatewayTimeout));
        using var service = new ModDownloadService(new HttpClient(server)) { RetryPolicy = Quick(serverMs: 1500) };
        var failed = new TaskCompletionSource();
        var first = new Heard<DownloadRetry>
        {
            OnReport = r => { if (r.State == DownloadRetryState.Retrying) failed.TrySetResult(); },
        };
        var second = new Heard<DownloadRetry>();

        var a = service.DownloadRetryingAsync("https://sp-mod.com/a", Path.Combine(_root, "a.part"), retrying: first);
        await failed.Task;
        await service.DownloadRetryingAsync("https://github.com/b", Path.Combine(_root, "b.part"), retrying: second);
        await a;

        Assert.Empty(second.Items);
    }

    [Fact]
    public async Task AWeakETag_IsNotResumedAgainst()
    {
        var server = new Server(_file) { BreakAfter = 70_000, ETag = "W/\"weak\"" };
        using var service = new ModDownloadService(new HttpClient(server));

        await Assert.ThrowsAnyAsync<Exception>(() => service.DownloadAsync("http://x/mod.zip", Part, resumable: true));

        Assert.False(File.Exists(ModDownloadService.ValidatorPathFor(Part)));
    }
}
