using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed class HeadlessAdviceDismissals
{
    private const int SchemaVersion = 1;

    private readonly JsonFile<Stored> _file;

    public HeadlessAdviceDismissals() : this(Path.Combine(AppPaths.DataDirectory, "headless_advice_kept.json")) { }

    public HeadlessAdviceDismissals(string filePath) => _file = new JsonFile<Stored>(filePath);

    private sealed class Stored
    {
        public int SchemaVersion { get; set; }
        public List<string> Kept { get; set; } = [];
    }

    public static string Key(Guid listId, HeadlessSubject subject, HeadlessVerdict verdict) =>
        $"{listId:N}|{subject.Key}|{verdict}".ToLowerInvariant();

    public HashSet<string> Load()
    {
        try
        {
            var data = _file.Read();
            return data is null || data.SchemaVersion != SchemaVersion ? [] : [.. data.Kept];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
            _file.Write(new Stored
            {
                SchemaVersion = SchemaVersion,
                Kept = [.. kept.Order(StringComparer.Ordinal)],
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("HeadlessAdvice", $"couldn't save kept headless advice: {ex.Message}");
        }
    }
}
