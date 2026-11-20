using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed class RememberedDownloadStore(string? path = null)
{
    private readonly JsonFile<List<RememberedDownload>> _file =
        new(path ?? Path.Combine(AppPaths.DataDirectory, "download-queue.json"), JsonFileOptions.Indented);

    public IReadOnlyList<RememberedDownload> Load()
    {
        try
        {
            return _file.Read() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Downloads", $"couldn't read the remembered downloads: {ex.Message}");
            return [];
        }
    }

    public void Save(IReadOnlyList<RememberedDownload> downloads)
    {
        try
        {
            _file.Write([.. downloads]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Downloads", $"couldn't keep the unfinished downloads: {ex.Message}");
        }
    }

    public void Clear() => Save([]);
}
