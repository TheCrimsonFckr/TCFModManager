using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// A dependency ticked to go with a list's apply (OPEN-22): the entry it would be, and what needs it.
public sealed record ModListDependencyPick(ModListEntry Entry, IReadOnlyList<string> NeededBy);

// What applying the list would have to do for one dependency it doesn't name.
public enum ModListDependencyNeed
{
    // Not installed here.
    Install,

    // Installed, but disabled.
    Enable,

    // Installed and enabled, but not on the list, so an Exclusive list would set it aside.
    KeepEnabled,
}

// One dependency offered on the preview.
public sealed record ModListDependencyOffer(SpModDependency Dependency, ModListDependencyNeed Need, ModListCandidate? Installed)
{
    public ModListDependencyPick Pick => new(Dependency.ToEntry(), Dependency.NeededBy);
}

//
// OPEN-22: which of the mods a list's entries need, but the list doesn't name, applying it would
// leave this install without. Asked of sp-mod by the caller (SpModListResolver); this only compares
// the answer with what is installed and with the plan the list makes on its own.
//
public static class ModListDependencies
{
    //
    // Offers: a dependency not installed (Install), installed but disabled (Enable), or installed and
    // enabled but about to be set aside by the list's Exclusive sweep (KeepEnabled). One installed
    // and left alone needs nothing - at whatever version (Q3: the Dependencies page judges that).
    // A dependency with no version for this SPT can't be offered; NoVersion lists those.
    //
    public static (IReadOnlyList<ModListDependencyOffer> Offers, IReadOnlyList<SpModDependency> NoVersion) Offer(
        SpModDependencies dependencies, IReadOnlyList<ModListCandidate> installed, ModListPlan planWithout)
    {
        var match = new ModListMatch(installed);
        var sweeping = new HashSet<ModListCandidate>(
            planWithout.Disable.Select(a => a.Installed).OfType<ModListCandidate>(), ReferenceEqualityComparer.Instance);

        var offers = new List<ModListDependencyOffer>();
        var noVersion = new List<SpModDependency>();

        foreach (var dependency in dependencies.Missing)
        {
            var index = match.IndexOf(dependency.ToEntry());
            var candidate = index >= 0 ? installed[index] : null;

            if (candidate is null)
            {
                if (dependency.HasVersion) offers.Add(new(dependency, ModListDependencyNeed.Install, null));
                else noVersion.Add(dependency);
            }
            else if (candidate.IsDisabled)
            {
                offers.Add(new(dependency, ModListDependencyNeed.Enable, candidate));
            }
            else if (sweeping.Contains(candidate))
            {
                offers.Add(new(dependency, ModListDependencyNeed.KeepEnabled, candidate));
            }
        }

        return (offers, noVersion);
    }

    //
    // OPEN-31 F1: the parent mod of each addon on the list that the list doesn't name and that isn't
    // already reported missing - sp-mod's addon dependencies never include the parent. Each is offered
    // at the newest version for sptVersion that the listed addon versions accept, or failing that the
    // newest for sptVersion; with none for sptVersion it comes back with no version, like any other
    // dependency that can't be added. Whether it is installed is for Offer to decide.
    //
    public static IReadOnlyList<SpModDependency> MissingParents(
        IReadOnlyList<ModListEntry> entries,
        IReadOnlyDictionary<int, Addon> addons,
        IReadOnlyDictionary<int, Mod> catalog,
        string? sptVersion,
        IReadOnlySet<int> alreadyMissing)
    {
        var onList = entries.Where(e => e is { IsAddon: false, ModId: not null }).Select(e => e.ModId!.Value).ToHashSet();
        var wanted = new Dictionary<int, (List<string> NeededBy, List<string> Constraints)>();

        foreach (var entry in entries.Where(e => e is { IsAddon: true, ModId: not null }))
        {
            if (!addons.TryGetValue(entry.ModId!.Value, out var addon) || addon.ModId is not { } parent) continue;
            if (onList.Contains(parent) || alreadyMissing.Contains(parent)) continue;

            if (!wanted.TryGetValue(parent, out var need)) wanted[parent] = need = ([], []);
            need.NeededBy.Add(entry.Name);

            var listed = (addon.Versions ?? []).FirstOrDefault(v =>
                (entry.VersionId is { } id && v.Id == id)
                || (!string.IsNullOrWhiteSpace(entry.Version) && string.Equals(v.Version, entry.Version, StringComparison.OrdinalIgnoreCase)));
            if (listed?.ModVersionConstraint is { Length: > 0 } constraint) need.Constraints.Add(constraint);
        }

        return
        [
            .. wanted.Select(w =>
            {
                var mod = catalog.GetValueOrDefault(w.Key);
                var forSpt = (mod?.Versions ?? [])
                    .Where(v => string.IsNullOrWhiteSpace(sptVersion) || SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, sptVersion) == true)
                    .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
                    .ThenByDescending(v => v.Version, Comparer<string?>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0))
                    .ToList();

                var pick = forSpt.FirstOrDefault(v => w.Value.Constraints.All(c => AddonFit.Check(c, v.Version, sptVersion) != false))
                    ?? forSpt.FirstOrDefault();

                return new SpModDependency(
                    w.Key,
                    string.IsNullOrWhiteSpace(mod?.Name) ? $"mod {w.Key}" : mod!.Name!,
                    pick?.Version,
                    pick?.Id,
                    [.. w.Value.NeededBy.Distinct(StringComparer.OrdinalIgnoreCase)],
                    IsParent: true,
                    Conflict: false);
            })
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    //
    // The entries to ask sp-mod about: everything on the list that applies to this machine and can be
    // asked about (a mod id and a version).
    //
    public static IReadOnlyList<ModListEntry> Askable(ModList list, ModListEntryScope machine) =>
        list.EntriesApplyingTo(machine).Where(e => e.ModId is not null && !string.IsNullOrWhiteSpace(e.Version)).ToList();

    //
    // What to store when the user asks for the ticked dependencies to be added to the list (Q1): the
    // list's entries plus those not already on it, sorted the way the page sorts.
    //
    public static IReadOnlyList<ModListEntry> WithAdded(ModList list, IEnumerable<ModListDependencyPick> picks)
    {
        var onList = new HashSet<(int, bool)>(list.Entries.Where(e => e.ModId is not null).Select(e => (e.ModId!.Value, e.IsAddon)));
        var added = picks.Select(p => p.Entry).Where(e => e.ModId is { } id && onList.Add((id, e.IsAddon)));
        return ModListEntries.Sorted(list.Entries.Concat(added));
    }
}
