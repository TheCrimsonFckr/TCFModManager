namespace TCFModManager.Core.Services;

//
// OPEN-23 R6: amber dependency warnings the user has accepted ("I've checked, it works"). Keyed per
// dependent version and dependency version, so an accepted warning comes back as soon as either side
// changes. Only sp-mod's ranges can be accepted - a dependent's own files are what the loader checks.
//
public sealed class AcceptedWarningStore
{
    private const int SchemaVersion = 1;

    private readonly JsonFile<Stored> _file;

    public AcceptedWarningStore() : this(Path.Combine(AppPaths.DataDirectory, "accepted_dependency_warnings.json")) { }

    public AcceptedWarningStore(string filePath) => _file = new JsonFile<Stored>(filePath);

    private sealed class Stored
    {
        public int SchemaVersion { get; set; }
        public List<string> Accepted { get; set; } = [];
    }

    public static string Key(int dependentModId, string? dependentVersion, int dependencyModId, string? dependencyVersion) =>
        $"{dependentModId}:{dependentVersion?.Trim()}>{dependencyModId}:{dependencyVersion?.Trim()}".ToLowerInvariant();

    public HashSet<string> Load()
    {
        try
        {
            var data = _file.Read();
            return data is null || data.SchemaVersion != SchemaVersion ? [] : [.. data.Accepted];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
            _file.Write(new Stored
            {
                SchemaVersion = SchemaVersion,
                Accepted = [.. accepted.Order(StringComparer.Ordinal)],
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Dependencies", $"couldn't save accepted warnings: {ex.Message}");
        }
    }
}
