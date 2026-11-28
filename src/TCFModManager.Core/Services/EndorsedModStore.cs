namespace TCFModManager.Core.Services;

//
// OPEN-76 R15: which sp-mod mods the signed-in user has endorsed, as last seen on each mod's page
// in the sp-mod actions pop-up. sp-mod's API has only counts, so a mod endorsed on the website is
// not known here until its pop-up loads.
//
public sealed class EndorsedModStore
{
    private const int SchemaVersion = 1;

    private readonly JsonFile<Stored> _file;

    private HashSet<int>? _ids;

    public EndorsedModStore() : this(Path.Combine(AppPaths.DataDirectory, "endorsedMods.json")) { }

    public EndorsedModStore(string filePath) => _file = new JsonFile<Stored>(filePath);

    // The mod whose state changed, or null when every mod's did.
    public event Action<int?>? Changed;

    private sealed class Stored
    {
        public int SchemaVersion { get; set; }
        public List<int> Endorsed { get; set; } = [];
    }

    public bool Contains(int modId) => Ids().Contains(modId);

    public void Set(int modId, bool endorsed)
    {
        var ids = Ids();
        if (endorsed ? !ids.Add(modId) : !ids.Remove(modId)) return;

        Save(ids);
        Changed?.Invoke(modId);
    }

    public void Clear()
    {
        var ids = Ids();
        if (ids.Count == 0) return;

        ids.Clear();
        Save(ids);
        Changed?.Invoke(null);
    }

    private HashSet<int> Ids()
    {
        if (_ids is not null) return _ids;

        try
        {
            var data = _file.Read();
            _ids = data is null || data.SchemaVersion != SchemaVersion ? [] : [.. data.Endorsed];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ids = [];
        }

        return _ids;
    }

    private void Save(HashSet<int> ids)
    {
        try
        {
            _file.Write(new Stored { SchemaVersion = SchemaVersion, Endorsed = [.. ids.Order()] });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("SpModActions", $"couldn't save endorsed mods: {ex.Message}");
        }
    }
}
