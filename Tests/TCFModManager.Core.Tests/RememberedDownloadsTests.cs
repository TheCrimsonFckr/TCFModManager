using System.Net;
using System.Net.Http.Headers;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class RememberedDownloadsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcf-remembered-" + Guid.NewGuid().ToString("N"));

    public RememberedDownloadsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheStore_KeepsEverythingAcrossARestart()
    {
        var path = Path.Combine(_root, "download-queue.json");
        var saved = new RememberedDownload
        {
            Target = new InstallTarget(12, true, "Some Addon", "com.x.addon", "thumb.png", "https://sp-mod.com/addon/12", "some-addon", "2.0.4"),
            VersionLabel = "1.4.2",
            VersionId = 991,
            InstallPath = @"G:\SPT",
            CheckDependencies = true,
            Asked = true,
            DownloadOnly = true,
            DownloadSubfolder = "My list",
            TotalBytes = 4_400_000_000,
            ArchivePath = @"G:\SPT\.tcfmm-work\queue-abc.bin",
            DependencyOf = 0,
            InstallsBefore = 0,
            UpdatesWith = [5, 6],
        };

        new RememberedDownloadStore(path).Save([saved]);
        var loaded = Assert.Single(new RememberedDownloadStore(path).Load());

        Assert.Equal(saved.Target, loaded.Target);
        Assert.Equal(saved with { UpdatesWith = loaded.UpdatesWith }, loaded);
        Assert.Equal([5, 6], loaded.UpdatesWith);
    }

    [Fact]
    public void Clear_LeavesNothingToBringBack()
    {
        var store = new RememberedDownloadStore(Path.Combine(_root, "download-queue.json"));
        store.Save([new RememberedDownload { Target = new InstallTarget(1, false, "A", null, null, null), VersionLabel = "1.0.0", InstallPath = _root }]);

        store.Clear();

        Assert.Empty(store.Load());
    }

    [Fact]
    public void TheSweep_LeavesAKeptArchiveItsPartAndValidator()
    {
        var install = Path.Combine(_root, "install");
        var work = Path.Combine(install, ModInstallService.WorkFolderName);
        Directory.CreateDirectory(work);

        var kept = Path.Combine(work, "queue-kept.bin");
        string[] keptFiles = [kept + ".part", kept + ".part.validator"];
        var stale = Path.Combine(work, "queue-stale.bin");

        foreach (var file in keptFiles.Append(stale))
        {
            File.WriteAllText(file, "x");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-3));
        }

        QueueArchives.Keep(kept);
        QueueArchives.NewPath(install);

        Assert.All(keptFiles, file => Assert.True(File.Exists(file)));
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public async Task APartFromAnEarlierRun_CarriesOnFromWhereItStopped()
    {
        var file = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray();
        var server = new RangeServer(file) { BreakAfter = 70_000 };
        using var service = new ModDownloadService(new HttpClient(server));
        var part = Path.Combine(_root, "queue-x.bin.part");

        await Assert.ThrowsAnyAsync<Exception>(() => service.DownloadAsync("http://x/mod.zip", part, resumable: true));
        Assert.Equal(70_000, new FileInfo(part).Length);

        await service.DownloadRetryingAsync("http://x/mod.zip", part, resumable: true, resumeExisting: true);

        Assert.Equal(file, File.ReadAllBytes(part));
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(70_000, server.Requests[1].Headers.Range!.Ranges.Single().From);
    }

    private sealed class RangeServer(byte[] file) : HttpMessageHandler
    {
        public int? BreakAfter { get; set; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);

            var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            var body = file.AsSpan((int)from).ToArray();
            var breakAfter = BreakAfter;
            BreakAfter = null;

            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(new Breaking(body, breakAfter)),
            };
            response.Content.Headers.ContentLength = body.Length;
            if (from > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, file.Length - 1, file.Length);
            response.Headers.ETag = EntityTagHeaderValue.Parse("\"v1\"");
            response.Headers.AcceptRanges.Add("bytes");
            return Task.FromResult(response);
        }
    }

    private sealed class Breaking(byte[] data, int? breakAfter) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (breakAfter is { } limit && Position >= limit) throw new IOException("connection reset");
            if (breakAfter is { } cap) buffer = buffer[..(int)Math.Min(buffer.Length, cap - Position)];
            return base.ReadAsync(buffer, ct);
        }
    }
}
