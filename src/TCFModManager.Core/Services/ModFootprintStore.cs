using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Persists each mod's analysed footprint per folder, so the Performance page renders from disk on a
// later launch instead of re-reading every assembly in the install. Same shape and same reasoning
// as DependencyFlagStore, with one difference: what invalidates an entry is a stamp of the folder
// itself rather than a timestamp from a catalog, because nothing here comes from sp-mod.com.
//
public sealed class ModFootprintStore
{
    // Bump when ModFootprint's stored shape changes in a way that makes an old file unsafe to
    // trust - in particular when a count changes meaning, since Level is derived from the counts
    // and an old file would silently produce a new answer.
    private const int SchemaVersion = 4;

    private readonly JsonFile<CachedFootprints> _file;

    public ModFootprintStore(string? filePath = null) =>
        _file = new JsonFile<CachedFootprints>(
            filePath ?? Path.Combine(AppPaths.DataDirectory, "mod_footprints.json"), preserveDamaged: false);

    private sealed class CachedFootprints
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, ModFootprint> Footprints { get; set; } = [];
    }

    // Cached footprints by folder key, or an empty dictionary when there's no usable file.
    public Dictionary<string, ModFootprint> Load()
    {
        try
        {
            var data = _file.Read();
            if (data is null || data.SchemaVersion != SchemaVersion) return [];
            return data.Footprints;
        }
        catch (IOException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyDictionary<string, ModFootprint> footprints)
    {
        try
        {
            var data = new CachedFootprints
            {
                SchemaVersion = SchemaVersion,
                Footprints = footprints.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            };
            _file.Write(data);
        }
        catch (IOException)
        {
            // A failed write just means the next launch analyses again.
        }
    }

    //
    // Whether a cached footprint still describes what is on disk. Kept here rather than on the
    // model so ModFootprint stays a plain record with no opinion about the filesystem.
    //
    public static bool IsCurrent(ModFootprint? cached, string currentStamp) =>
        cached is not null
        && !string.IsNullOrEmpty(cached.Stamp)
        && cached.Stamp == currentStamp;
}
