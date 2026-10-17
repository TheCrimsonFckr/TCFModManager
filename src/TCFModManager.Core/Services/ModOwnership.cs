using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Who owns the files in a client mod folder, for splitting a folder several mods share (OPEN-28):
// the app-managed install records, by the files they placed, and the sp-mod listings, by plugin GUID.
//
public sealed class ModOwnership
{
    public static ModOwnership None { get; } = new([]);

    private readonly Dictionary<string, List<InstalledModRecord>> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<InstalledModRecord>> _byFolder = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _listingsByGuid = new(StringComparer.OrdinalIgnoreCase);

    public ModOwnership(
        IEnumerable<InstalledModRecord> records,
        IEnumerable<KeyValuePair<string, int>>? listingsByGuid = null,
        bool includeAddons = false)
    {
        foreach (var record in records.Where(r => (includeAddons || !r.IsAddon) && r.IsAppManaged).OrderBy(r => r.InstalledAt))
        {
            foreach (var file in record.Files)
            {
                var path = DisabledModPaths.ToEnabledRelativePath(file);
                Add(_byFile, path, record);

                if (FolderKeyOf(path) is { } folder) Add(_byFolder, folder, record);
            }
        }

        foreach (var (guid, modId) in listingsByGuid ?? [])
        {
            if (!string.IsNullOrWhiteSpace(guid)) _listingsByGuid.TryAdd(guid.Trim(), modId);
        }
    }

    public bool IsEmpty => _byFile.Count == 0 && _listingsByGuid.Count == 0;

    // The earliest app-managed record of this install that placed the file, by its path relative to
    // the install root in either container state.
    public InstalledModRecord? RecordFor(string installPath, string relativePath) =>
        _byFile.TryGetValue(DisabledModPaths.ToEnabledRelativePath(relativePath), out var records)
            ? records.FirstOrDefault(r => BelongsTo(r, installPath))
            : null;

    // Every app-managed record of this install that placed something in the mod folder, by the
    // folder's path relative to the install root ("BepInEx/plugins/Realism").
    public IReadOnlyList<InstalledModRecord> RecordsIn(string installPath, string relativeFolder) =>
        _byFolder.TryGetValue(DisabledModPaths.ToEnabledRelativePath(relativeFolder), out var records)
            ? [.. records.Where(r => BelongsTo(r, installPath)).DistinctBy(r => (r.ModId, r.IsAddon))]
            : [];

    public int? ListingFor(string? guid) =>
        !string.IsNullOrWhiteSpace(guid) && _listingsByGuid.TryGetValue(guid.Trim(), out var id) ? id : null;

    private static bool BelongsTo(InstalledModRecord record, string installPath) =>
        record.InstallPath is not { } stamped
        || string.Equals(stamped, InstallStamp.Of(installPath), StringComparison.OrdinalIgnoreCase);

    private static string? FolderKeyOf(string relativePath)
    {
        if (!DisabledModPaths.TryFindRelativeContainer(relativePath, out var container, out _)) return null;

        var rest = relativePath[container.Length..].TrimStart('/');
        var slash = rest.IndexOf('/');
        return slash <= 0 ? null : container + "/" + rest[..slash];
    }

    private static void Add(Dictionary<string, List<InstalledModRecord>> index, string key, InstalledModRecord record)
    {
        if (!index.TryGetValue(key, out var list)) index[key] = list = [];
        if (!list.Contains(record)) list.Add(record);
    }
}
