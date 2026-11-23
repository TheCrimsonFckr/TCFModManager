using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

public sealed class UpdateWaitStore
{
    private readonly SettingsService _settings = new();
    private int _days;
    private HashSet<string> _exempt;

    public UpdateWaitStore()
    {
        var settings = _settings.Load();
        _days = Math.Max(0, settings.UpdateWaitDays);
        _exempt = new HashSet<string>(settings.NoUpdateWait, StringComparer.OrdinalIgnoreCase);
    }

    public event EventHandler? Changed;

    public int Days => _days;

    public bool IsExempt(bool isAddon, int? id) =>
        id is { } value && _exempt.Contains(SkippedUpdates.Key(isAddon, value));

    public int DaysFor(bool isAddon, int? id) => IsExempt(isAddon, id) ? 0 : _days;

    public void SetDays(int days)
    {
        days = Math.Max(0, days);
        if (days == _days) return;

        _days = days;
        var settings = _settings.Load();
        settings.UpdateWaitDays = days;
        _settings.Save(settings);

        AppLog.Info("Updates", $"wait before offering an update: {days} days");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetExempt(bool isAddon, int id, bool exempt, string? name)
    {
        var key = SkippedUpdates.Key(isAddon, id);
        var next = new HashSet<string>(_exempt, StringComparer.OrdinalIgnoreCase);
        if (!(exempt ? next.Add(key) : next.Remove(key))) return;
        _exempt = next;

        var settings = _settings.Load();
        settings.NoUpdateWait = [.. next.Order(StringComparer.OrdinalIgnoreCase)];
        _settings.Save(settings);

        AppLog.Info("Updates", exempt ? $"no wait for {name} ({key})" : $"waiting again for {name} ({key})");
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
