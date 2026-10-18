using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed class HeadlessAdviceDismissals
{
    private const int SchemaVersion = 1;

    private readonly string _filePath;

    public HeadlessAdviceDismissals() : this(Path.Combine(AppPaths.DataDirectory, "headless_advice_kept.json")) { }

    public HeadlessAdviceDismissals(string filePath) => _filePath = filePath;

    private sealed class Stored
    {
        public int SchemaVersion { get; set; }
        public List<string> Kept { get; set; } = [];
    }

    public static string Key(Guid listId, HeadlessSubject subject, HeadlessVerdict verdict) =>
        $"{listId:N}|{subject.Key}|{verdict}".ToLowerInvariant();

    public HashSet<string> Load()
    {
        if (!File.Exists(_filePath)) return [];

        try
        {
            var data = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_filePath));
            return data is null || data.SchemaVersion != SchemaVersion ? [] : [.. data.Kept];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Keep(string key)
    {
        var all = Load();
        if (all.Add(key)) Save(all);
    }

    private void Save(HashSet<string> kept)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(new Stored
            {
                SchemaVersion = SchemaVersion,
                Kept = [.. kept.Order(StringComparer.Ordinal)],
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("HeadlessAdvice", $"couldn't save kept headless advice: {ex.Message}");
        }
    }
}
