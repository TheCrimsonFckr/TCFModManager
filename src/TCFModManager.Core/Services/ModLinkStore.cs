using System.Text.Json.Serialization;

namespace TCFModManager.Core.Services;

// A hand-installed mod or addon folder tied by the user to its sp-mod page.
public sealed record ModLink(
    [property: JsonPropertyName("modId")] int ModId,
    [property: JsonPropertyName("isAddon")] bool IsAddon);

//
// Data\mod-links.json: folder name -> the sp-mod listing it was linked to. Only for folders no
// install record names - a record is the authority, so a link under a recorded folder is dropped.
//
public sealed class ModLinkStore
{
    private readonly JsonFile<Dictionary<string, ModLink>> _file;
    private readonly Lock _gate = new();

    public ModLinkStore(string? path = null)
    {
        _file = new(path ?? Path.Combine(AppPaths.DataDirectory, "mod-links.json"), JsonFileOptions.Indented, keepBackups: true);
    }

    public IReadOnlyDictionary<string, ModLink> Load()
    {
        lock (_gate) return Read();
    }

    // The links still in force: those under a folder a record names are dropped, on disk too.
    public IReadOnlyDictionary<string, ModLink> Effective(IEnumerable<string> recordedFolders)
    {
        lock (_gate)
        {
            var links = Read();
            if (links.Count == 0) return links;

            var taken = recordedFolders.Where(links.ContainsKey).ToList();
            if (taken.Count == 0) return links;

            foreach (var folder in taken) links.Remove(folder);
            _file.Write(links);
            AppLog.Info("ModLinks", $"dropped the link of {string.Join(", ", taken)}: an install record names it now");
            return links;
        }
    }

    public void Link(IEnumerable<string> folders, int modId, bool isAddon)
    {
        lock (_gate)
        {
            var links = Read();
            foreach (var folder in folders.Where(f => !string.IsNullOrWhiteSpace(f))) links[folder] = new ModLink(modId, isAddon);
            _file.Write(links);
        }
    }

    // True when any of the folders had a link.
    public bool Unlink(IEnumerable<string> folders)
    {
        lock (_gate)
        {
            var links = Read();
            var removed = folders.Count(f => !string.IsNullOrWhiteSpace(f) && links.Remove(f));
            if (removed > 0) _file.Write(links);
            return removed > 0;
        }
    }

    private Dictionary<string, ModLink> Read() =>
        new(_file.Read() ?? [], StringComparer.OrdinalIgnoreCase);
}
