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
    // reporting fractional progress (0.0-1.0) when a Content-Length header is available.
    public async Task DownloadAsync(
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

        // Unconditional, so the throttle above can never swallow the last fraction and leave a bar
        // stopped short of the end.
        progress?.Report(1.0);
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
