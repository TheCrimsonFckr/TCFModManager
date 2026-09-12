using System.Text.Json;
using System.Text.Json.Serialization;

namespace TCFModManager.Core.Services;

// What an update does with one mod's config files when the user has changed them.
public enum ModConfigPolicy
{
    // Start from the new version's defaults and carry the user's changed values into them.
    Merge,

    // Leave the user's file exactly as it is.
    KeepMine,

    // Take the new version's file. The user's copy is archived, as it always is.
    TakeNew,
}

//
// Per-mod config settings, in Data\mod_configs.json, keyed by the mod's folder name lowercased - the
// same key rule as ModGroupStore, with the same accepted tradeoff that renaming the folder loses the
// entry.
//
// Deliberately not on InstalledModRecord: a record is rewritten by every install, and a choice about
// how to treat somebody's config has to outlive an uninstall and reinstall.
//
public sealed class ModConfigOptionsStore(string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "mod_configs.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath => _filePath;

    public static string KeyFor(string folderName) => folderName.Trim().ToLowerInvariant();

    public Dictionary<string, ModConfigOptions> Load()
    {
        if (!File.Exists(_filePath)) return new Dictionary<string, ModConfigOptions>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, ModConfigOptions>>(
                File.ReadAllText(_filePath), Options);

            return read is null
                ? new Dictionary<string, ModConfigOptions>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ModConfigOptions>(read, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A hand-edited file that no longer parses means every mod is back on the default, which
            // is what it was before anybody set anything.
            AppLog.Warn("Configs", $"couldn't read {_filePath}: {ex.Message}");
            return new Dictionary<string, ModConfigOptions>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public ModConfigOptions For(string folderName) =>
        Load().TryGetValue(KeyFor(folderName), out var options) ? options : new ModConfigOptions();

    //
    // The policy for a mod, asked of every folder it occupies: a client+server mod is two folders with
    // one setting between them, and the answer has to be the same whichever one is asked about.
    //
    // The first folder that has an entry wins, rather than the strictest of them - two folders of one
    // mod disagreeing is not a state the UI can produce, and picking a winner silently is better than
    // an update that does something neither folder's entry asked for.
    //
    public ModConfigPolicy PolicyFor(IEnumerable<string> folderNames)
    {
        var stored = Load();

        foreach (var folder in folderNames)
            if (stored.TryGetValue(KeyFor(folder), out var options)) return options.Policy;

        return ModConfigPolicy.Merge;
    }

    // Sets one mod's policy. Writing the default back removes the entry rather than storing it, so the
    // file only ever holds choices somebody actually made.
    public void SetPolicy(string folderName, ModConfigPolicy policy)
    {
        var stored = Load();
        var key = KeyFor(folderName);

        if (policy == ModConfigPolicy.Merge) stored.Remove(key);
        else if (stored.TryGetValue(key, out var options)) options.Policy = policy;
        else stored[key] = new ModConfigOptions { Policy = policy };

        Save(stored);
    }

    public void Save(Dictionary<string, ModConfigOptions> options)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(options, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Configs", $"couldn't write {_filePath}: {ex.Message}");
        }
    }
}

// One mod's entry. Offered for hand-editing, which is why the enum is written as its name.
public sealed class ModConfigOptions
{
    public ModConfigPolicy Policy { get; set; } = ModConfigPolicy.Merge;
}
