using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The sp-mod listings the user ignored (OPEN-30) - read from settings.json once, written back on
// every change. Ignored from a Browse card's right-click menu, un-ignored there or in Options; read
// by Browse, the Author page and the new-mod check.
//
public sealed class IgnoredMods
{
    private readonly SettingsService _settings = new();
    private List<IgnoredMod> _all;

    public IgnoredMods()
    {
        _all = [.. _settings.Load().IgnoredMods];
    }

    // Raised on the UI thread after a mod was ignored or un-ignored.
    public event EventHandler? Changed;

    public IReadOnlyList<IgnoredMod> All => _all;

    public IReadOnlySet<int> Ids => _all.Select(m => m.Id).ToHashSet();

    public bool IsIgnored(int modId) => _all.Any(m => m.Id == modId);

    public void Set(int modId, string? name, bool ignore)
    {
        if (ignore == IsIgnored(modId)) return;

        if (ignore)
            _all = [.. _all, new IgnoredMod { Id = modId, Name = name, IgnoredAt = DateTimeOffset.Now }];
        else
            _all = [.. _all.Where(m => m.Id != modId)];

        var settings = _settings.Load();
        settings.IgnoredMods = [.. _all];
        _settings.Save(settings);

        AppLog.Info("Ignored", $"{(ignore ? "ignored" : "stopped ignoring")} {name} ({modId})");
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
