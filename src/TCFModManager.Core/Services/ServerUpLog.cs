using System.Text.Json;

namespace TCFModManager.Core.Services;

//
// OPEN-54 N27: Data\serverLastUp.json - when each install's server was last seen up, so a search
// can try the mods changed since then first.
//
public sealed class ServerUpLog
{
    private readonly string _filePath;

    public ServerUpLog() : this(Path.Combine(AppPaths.DataDirectory, "serverLastUp.json"))
    {
    }

    public ServerUpLog(string filePath) => _filePath = filePath;

    public DateTimeOffset? Last(string installPath) =>
        Read().TryGetValue(installPath, out var at) ? at : null;

    public void Note(string installPath, DateTimeOffset at)
    {
        try
        {
            var all = Read();
            all[installPath] = at;
            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(all));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Search", $"serverLastUp.json couldn't be written: {ex.Message}");
        }
    }

    private Dictionary<string, DateTimeOffset> Read()
    {
        try
        {
            if (File.Exists(_filePath)
                && JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(_filePath)) is { } read)
                return new Dictionary<string, DateTimeOffset>(read, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Debug("Search", $"serverLastUp.json couldn't be read: {ex.Message}");
        }

        return new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
    }
}
