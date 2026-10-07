using System.Text.Json;

namespace TCFModManager.Core.Services;

//
// OPEN-23 R6: amber dependency warnings the user has accepted ("I've checked, it works"). Keyed per
// dependent version and dependency version, so an accepted warning comes back as soon as either side
// changes. Only sp-mod's ranges can be accepted - a dependent's own files are what the loader checks.
//
public sealed class AcceptedWarningStore
{
    private const int SchemaVersion = 1;

    private readonly string _filePath;

    public AcceptedWarningStore() : this(Path.Combine(AppPaths.DataDirectory, "accepted_dependency_warnings.json")) { }

    public AcceptedWarningStore(string filePath) => _filePath = filePath;

    private sealed class Stored
    {
        public int SchemaVersion { get; set; }
        public List<string> Accepted { get; set; } = [];
    }

    public static string Key(int dependentModId, string? dependentVersion, int dependencyModId, string? dependencyVersion) =>
        $"{dependentModId}:{dependentVersion?.Trim()}>{dependencyModId}:{dependencyVersion?.Trim()}".ToLowerInvariant();

    public HashSet<string> Load()
    {
        if (!File.Exists(_filePath)) return [];

        try
        {
            var data = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_filePath));
            return data is null || data.SchemaVersion != SchemaVersion ? [] : [.. data.Accepted];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public bool IsAccepted(string key) => Load().Contains(key);

    public void Accept(string key)
    {
        var all = Load();
        if (!all.Add(key)) return;
        Save(all);
    }

    private void Save(HashSet<string> accepted)
    {
        try
        {
            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(new Stored
            {
                SchemaVersion = SchemaVersion,
                Accepted = [.. accepted.Order(StringComparer.Ordinal)],
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Dependencies", $"couldn't save accepted warnings: {ex.Message}");
        }
    }
}
