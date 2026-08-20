namespace TCFModManagement.Core.Services;

// 
// Downloads a mod/addon version archive from its sp-mod.com download link.
// 
public sealed class ModDownloadService(HttpClient? httpClient = null) : IDisposable
{
    private readonly HttpClient _http = httpClient ?? new HttpClient();
    private readonly bool _ownsHttpClient = httpClient is null;

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
        while ((read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            totalRead += read;
            if (totalBytes is > 0) progress?.Report((double)totalRead / totalBytes.Value);
        }

        progress?.Report(1.0);
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
