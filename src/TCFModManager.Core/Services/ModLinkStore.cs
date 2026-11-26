using System.Text.Json.Serialization;

namespace TCFModManager.Core.Services;

// A hand-installed mod or addon folder tied by the user to its sp-mod page.
public sealed record ModLink(
    [property: JsonPropertyName("modId")] int ModId,
    [property: JsonPropertyName("isAddon")] bool IsAddon);

// Files inside another mod's folder that are a hand-installed addon, as install-relative paths.
public sealed record AddonFileLink(
    [property: JsonPropertyName("addonId")] int AddonId,
    [property: JsonPropertyName("files")] IReadOnlyList<string> Files);

public sealed record ModLinks
{
    public static ModLinks Empty { get; } = new();

    [JsonPropertyName("folders")]
    public Dictionary<string, ModLink> Folders { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("addonFiles")]
    public List<AddonFileLink> AddonFiles { get; init; } = [];

    public bool IsEmpty => Folders.Count == 0 && AddonFiles.Count == 0;
}

//
// Data\modLinks.json: what the user tied to sp-mod by hand - a folder name to its mod or addon, and
// an addon's files inside its parent's folder. Only for what no install record names: a record is the
// authority, so a link a record covers is dropped.
//
public sealed class ModLinkStore
{
    private readonly JsonFile<ModLinks> _file;
    private readonly Lock _gate = new();

    public ModLinkStore(string? path = null)
    {
        _file = new(path ?? Path.Combine(AppPaths.DataDirectory, "modLinks.json"), JsonFileOptions.Indented, keepBackups: true);
    }

    public ModLinks Load()
    {
        lock (_gate) return Read();
    }

    //
    // The links still in force. A folder link under a folder a record names goes, and so does an
    // addon's file link once a record holds that addon - on disk too.
    //
    public ModLinks Effective(IEnumerable<string> recordedFolders, IEnumerable<int> recordedAddonIds)
    {
        lock (_gate)
        {
            var links = Read();
            if (links.IsEmpty) return links;

            var folders = recordedFolders.Where(links.Folders.ContainsKey).ToList();
            var addons = recordedAddonIds.ToHashSet();
            var files = links.AddonFiles.Where(a => addons.Contains(a.AddonId)).ToList();
            if (folders.Count == 0 && files.Count == 0) return links;

            foreach (var folder in folders) links.Folders.Remove(folder);
            links.AddonFiles.RemoveAll(files.Contains);
            _file.Write(links);
            AppLog.Info("ModLinks", $"dropped {folders.Count + files.Count} link(s) an install record covers now");
            return links;
        }
    }

    public void Link(IEnumerable<string> folders, int modId, bool isAddon)
    {
        lock (_gate)
        {
            var links = Read();
            foreach (var folder in folders.Where(f => !string.IsNullOrWhiteSpace(f))) links.Folders[folder] = new ModLink(modId, isAddon);
            _file.Write(links);
        }
    }

    // True when any of the folders had a link.
    public bool Unlink(IEnumerable<string> folders)
    {
        lock (_gate)
        {
            var links = Read();
            var removed = folders.Count(f => !string.IsNullOrWhiteSpace(f) && links.Folders.Remove(f));
            if (removed > 0) _file.Write(links);
            return removed > 0;
        }
    }

    //
    // Ties these files to the addon, in place of what it had. A file belongs to one addon at a time,
    // so it leaves any other addon's link. No files unlinks the addon.
    //
    public void LinkAddonFiles(int addonId, IEnumerable<string> files)
    {
        lock (_gate)
        {
            var links = Read();
            var wanted = files.Select(Normal).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
            var taken = wanted.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var kept = links.AddonFiles
                .Where(a => a.AddonId != addonId)
                .Select(a => a with { Files = [.. a.Files.Where(f => !taken.Contains(f))] })
                .Where(a => a.Files.Count > 0)
                .ToList();
            if (wanted.Count > 0) kept.Add(new AddonFileLink(addonId, wanted));

            links.AddonFiles.Clear();
            links.AddonFiles.AddRange(kept);
            _file.Write(links);
        }
    }

    // True when the addon had linked files.
    public bool UnlinkAddonFiles(int addonId)
    {
        lock (_gate)
        {
            var links = Read();
            if (links.AddonFiles.RemoveAll(a => a.AddonId == addonId) == 0) return false;
            _file.Write(links);
            return true;
        }
    }

    private static string Normal(string path) => DisabledModPaths.ToEnabledRelativePath(path.Replace('\\', '/').Trim('/'));

    private ModLinks Read()
    {
        var read = _file.Read();
        return new ModLinks
        {
            Folders = new(read?.Folders ?? [], StringComparer.OrdinalIgnoreCase),
            AddonFiles = [.. read?.AddonFiles ?? []],
        };
    }
}
