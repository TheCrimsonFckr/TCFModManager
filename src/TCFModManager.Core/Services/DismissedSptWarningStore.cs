using System.Text.Json;

namespace TCFModManager.Core.Services;

//
// OPEN-13 follow-up (Chris, 2026-10-12): "not for this SPT" warnings the user has dismissed, because
// some mods do work on a newer SPT than they list. Keyed per mod, installed version and SPT version,
// so a dismissed warning comes back as soon as either the mod or SPT changes.
//
public sealed class DismissedSptWarningStore
{
    private const int SchemaVersion = 1;

    private readonly string _filePath;

    public DismissedSptWarningStore() : this(Path.Combine(AppPaths.DataDirectory, "dismissed_spt_warnings.json")) { }

    public DismissedSptWarningStore(string filePath) => _filePath = filePath;

    private sealed class Stored
    {
        public int SchemaVersion { get; set; }
        public List<string> Dismissed { get; set; } = [];
    }

    // modKey: the sp-mod id where the mod is matched, else its folder name.
    public static string Key(string modKey, string? installedVersion, string? sptVersion) =>
        $"{modKey.Trim()}:{installedVersion?.Trim()}@{sptVersion?.Trim()}".ToLowerInvariant();

    public HashSet<string> Load()
    {
        if (!File.Exists(_filePath)) return [];

        try
        {
            var data = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_filePath));
            return data is null || data.SchemaVersion != SchemaVersion ? [] : [.. data.Dismissed];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Dismiss(IEnumerable<string> keys)
    {
        var all = Load();
        var before = all.Count;
        all.UnionWith(keys);
        if (all.Count != before) Save(all);
    }

    public void Restore(string key)
    {
        var all = Load();
        if (all.Remove(key)) Save(all);
    }

    private void Save(HashSet<string> dismissed)
    {
        try
        {
            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(new Stored
            {
                SchemaVersion = SchemaVersion,
                Dismissed = [.. dismissed.Order(StringComparer.Ordinal)],
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Installed", $"couldn't save dismissed SPT warnings: {ex.Message}");
        }
    }
}
