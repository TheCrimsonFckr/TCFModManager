using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-32 D11: addons to disable once their mod's update has installed, because no version of them
// fits the version it is going to. Held in memory - a restart before the update lands drops them.
// OPEN-37 D4: an addon installed anyway and kept is held too, so its record moves to the new version.
//
public sealed class AddonFitFollowUp
{
    public sealed record PendingDisable(int AddonId, int ModId, string ModVersion, bool Keep = false);

    private readonly List<PendingDisable> _pending = [];

    public void DisableAfterUpdate(IEnumerable<AddonImpactItem> items, bool disableOverridden = false)
    {
        foreach (var item in items.Where(i => i.Result.Verdict is AddonImpactVerdict.NoCompatibleVersion or AddonImpactVerdict.Overridden))
        {
            var keep = item.Result.Verdict == AddonImpactVerdict.Overridden && !disableOverridden;

            _pending.RemoveAll(p => p.AddonId == item.Addon.AddonId);
            _pending.Add(new PendingDisable(item.Addon.AddonId, item.ModId, item.ModVersion, keep));
        }
    }

    // The pending disables whose mod is now at the version they were waiting for, taken off the list.
    public List<PendingDisable> TakeDue(Func<int, string?> installedVersionOfMod)
    {
        var due = _pending.Where(p => ModVersionComparer.Compare(installedVersionOfMod(p.ModId), p.ModVersion) == 0).ToList();
        _pending.RemoveAll(due.Contains);
        return due;
    }
}
