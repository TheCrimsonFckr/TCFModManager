using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Where one installed mod stands on an SPT release the user is thinking of moving to (OPEN-12 F12).
public enum SptUpgradeStanding
{
    // The version installed now is published for that release.
    Ready,

    // A newer version is published for it; the installed one is not, or can't be told.
    UpdateNeeded,

    // Nothing published for it yet.
    NotYet,

    // Not matched to an sp-mod listing (installed by hand), or its versions say nothing readable
    // about SPT - it has to be checked by hand.
    Unknown,
}

// One installed mod in an upgrade report. Version is the one for the target release: the installed
// one when Ready, the one to update to when UpdateNeeded, else null.
public sealed record SptUpgradeRow(string Name, int? ModId, string? Installed, SptUpgradeStanding Standing, string? Version);

// What the report is built from, for each installed mod.
public sealed record SptUpgradeInput(string Name, int? ModId, string? Installed);

//
// OPEN-12 F12: "if I moved to SPT x.y, which of my mods would come with me?" - answered from the
// catalog each mod is already matched to, before anything is changed. From SSPTMM's SptUpgradeReport.
//
// One change from the fork: the catalog keeps only each mod's latest few versions, so an older
// installed version is often not among them. When a newer version is published for the target, that
// is still an update needed - the fork called it Unknown, which left most of an older install
// unanswered. Only when nothing newer fits is it Unknown.
//
public static class SptUpgradeReport
{
    public static List<SptUpgradeRow> Build(IEnumerable<SptUpgradeInput> installed, IReadOnlyList<Mod> catalog, string targetSpt)
    {
        var byId = catalog.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        var rows = new List<SptUpgradeRow>();

        foreach (var mod in installed)
        {
            if (mod.ModId is not { } id || !byId.TryGetValue(id, out var listing) || listing.Versions is not { Count: > 0 } versions)
            {
                rows.Add(new SptUpgradeRow(mod.Name, mod.ModId, mod.Installed, SptUpgradeStanding.Unknown, null));
                continue;
            }

            // Each version's answer for the target; null when its constraint can't be read.
            var fits = versions
                .Where(v => !string.IsNullOrWhiteSpace(v.Version))
                .Select(v => (Version: v.Version!, Fits: SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, targetSpt)))
                .ToList();

            // The installed version among the published ones: exactly, or - for a version read off a
            // file, which carries no label - by its numbers.
            var mine = fits.FirstOrDefault(v => ModVersionComparer.Compare(v.Version, mod.Installed) == 0);
            if (mine.Version is null) mine = fits.FirstOrDefault(v => ModVersionComparer.SameNumbers(v.Version, mod.Installed));

            if (mine.Version is not null && mine.Fits == true)
            {
                rows.Add(new SptUpgradeRow(mod.Name, id, mod.Installed, SptUpgradeStanding.Ready, mine.Version));
                continue;
            }

            // Only a newer version is an update: an older one published for the target is not.
            var newest = fits
                .Where(v => v.Fits == true && IsNewer(v.Version, mod.Installed))
                .OrderByDescending(v => v.Version, Comparer<string>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0))
                .Select(v => v.Version)
                .FirstOrDefault();

            if (newest is not null)
            {
                rows.Add(new SptUpgradeRow(mod.Name, id, mod.Installed, SptUpgradeStanding.UpdateNeeded, newest));
                continue;
            }

            // Not among the versions known here, or what it needs can't be read: nothing can be said.
            var standing = mine.Version is null || mine.Fits is null ? SptUpgradeStanding.Unknown : SptUpgradeStanding.NotYet;
            rows.Add(new SptUpgradeRow(mod.Name, id, mod.Installed, standing, null));
        }

        // What stands in the way first.
        return [.. rows.OrderBy(r => Array.IndexOf(Order, r.Standing)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool IsNewer(string published, string? installed) =>
        (ModVersionComparer.IsUpdateAvailable(installed, published)
            ?? ModVersionComparer.IsUpdateAvailableByNumbers(installed, published)) == true;

    private static readonly SptUpgradeStanding[] Order =
        [SptUpgradeStanding.NotYet, SptUpgradeStanding.UpdateNeeded, SptUpgradeStanding.Unknown, SptUpgradeStanding.Ready];
}
