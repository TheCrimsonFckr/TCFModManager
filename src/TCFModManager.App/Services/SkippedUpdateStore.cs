using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

public sealed class SkippedUpdateStore
{
    private readonly SettingsService _settings = new();
    private IReadOnlyDictionary<string, string> _all;

    public SkippedUpdateStore()
    {
        _all = new Dictionary<string, string>(_settings.Load().SkippedUpdates, StringComparer.OrdinalIgnoreCase);
    }

    public event EventHandler? Changed;

    public string? For(bool isAddon, int? id) =>
        id is { } value && _all.TryGetValue(SkippedUpdates.Key(isAddon, value), out var version) ? version : null;

    public void Skip(bool isAddon, int id, string version, string? name) => Set(isAddon, id, version, name);

    public void StopSkipping(bool isAddon, int id, string? name) => Set(isAddon, id, null, name);

    private void Set(bool isAddon, int id, string? version, string? name)
    {
        var key = SkippedUpdates.Key(isAddon, id);
        var next = new Dictionary<string, string>(_all, StringComparer.OrdinalIgnoreCase);
        if (version is null) { if (!next.Remove(key)) return; }
        else next[key] = version;
        _all = next;

        var settings = _settings.Load();
        settings.SkippedUpdates = new Dictionary<string, string>(next, StringComparer.OrdinalIgnoreCase);
        _settings.Save(settings);

        AppLog.Info("Updates", version is null ? $"stopped skipping updates of {name} ({key})" : $"skipping {name} ({key}) {version}");
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
