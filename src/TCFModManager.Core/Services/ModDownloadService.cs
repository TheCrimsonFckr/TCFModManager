using System.Diagnostics;

namespace TCFModManager.Core.Services;

// 
// Downloads a mod/addon version archive from its sp-mod.com download link.
// 
public sealed class ModDownloadService(HttpClient? httpClient = null) : IDisposable
{
    private readonly HttpClient _http = httpClient ?? new HttpClient();
    private readonly bool _ownsHttpClient = httpClient is null;

    //
    // How often progress is reported, regardless of how fast the bytes arrive.
    //
    // The read buffer is 80 KB, so a 5.5 GB archive is about 70,000 reads. Reporting every one of
    // them posts 70,000 callbacks to the UI thread, each raising property changes and invalidating
    // layout - and no display can show more than a few dozen of those a second, so the rest is pure
    // contention against the thread that has to render the number. On a multi-gigabyte download it
    // is enough to make the whole window feel heavy.
    //
    // 100ms is finer than the eye and roughly two updates per rendered frame.
    //
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    // Downloads <paramref name="downloadUrl"/> to <paramref name="destinationPath"/>,
    // reporting fractional progress (0.0-1.0) when a Content-Length header is available. Returns the
    // file name the server offered, for a caller that saves the archive under a name of its own.
    public async Task<DownloadResponse> DownloadAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        using var response = await _http
            .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var offeredName = OfferedFileName(response);

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fileStream = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        var clock = Stopwatch.StartNew();
        var nextReport = TimeSpan.Zero;

        while ((read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            totalRead += read;

            if (totalBytes is > 0 && clock.Elapsed >= nextReport)
            {
                nextReport = clock.Elapsed + ReportInterval;
                progress?.Report((double)totalRead / totalBytes.Value);
            }
        }

        //
        // A short read loop is not an error to HttpClient: a connection closed cleanly part way
        // through leaves a truncated file behind and returns normally. That file then extracts as
        // far as it goes - a zip throws on its missing central directory, but a tar or a solid
        // archive can stop quietly, having written some of the mod's files and none of the rest.
        //
        // So the promised length is checked here, where both numbers are in hand, rather than
        // trusting whatever landed on disk. Short only, never long: a handler configured to
        // decompress transparently would read more bytes than the header promised, and that is a
        // complete download, not a failure.
        //
        if (totalBytes is { } expected && totalRead < expected)
        {
            throw new ModInstallException(ModInstallFailure.DownloadIncomplete)
            {
                ExpectedBytes = expected,
                ReceivedBytes = totalRead,
            };
        }

        // Unconditional, so the throttle above can never swallow the last fraction and leave a bar
        // stopped short of the end.
        progress?.Report(1.0);

        return new DownloadResponse(offeredName, totalRead);
    }

    //
    // The Content-Disposition file name, preferring the RFC 5987 filename* form, which is the one
    // that carries non-ASCII names intact. Only the last path segment is kept: the header is the
    // server's to write, and a name holding a folder is not one to follow out of the folder chosen.
    //
    private static string? OfferedFileName(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(name)) return null;

        name = name.Trim().Trim('"').Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}

// What a finished download reported: the name the server offered for the file, if any, and how many
// bytes arrived.
public sealed record DownloadResponse(string? OfferedFileName, long Bytes);
