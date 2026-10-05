using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Where a row sits in the import window, in the order the window shows them.
public enum SpModReviewSection
{
    // An addon on the list whose parent mod isn't. An addon does nothing without it (R4).
    MissingParents,

    Mods,

    Addons,

    // A mod something on the list needs that the list doesn't name.
    Dependencies,

    // A page item with no version for the target SPT after a retarget. Kept, unticked.
    NoVersion,
}

// How a row compares with the list already stored, on a refresh. None on a first import.
public enum SpModReviewChange
{
    None,
    New,
    VersionChanged,
}

//
// One line of the import window.
//
// Entry is what goes into the list if the row stays ticked. FromVersion is the page's version when
// a retarget changed it. NeededBy names what requires a dependency or a missing parent; ParentName
// names the mod an addon belongs to.
//
public sealed class SpModReviewRow
{
    public required string Ref { get; init; }

    public required SpModReviewSection Section { get; init; }

    public required ModListEntry Entry { get; init; }

    public required bool OnPage { get; init; }

    public bool Ticked { get; set; }

    public string? FromVersion { get; init; }

    public string? ParentName { get; init; }

    public IReadOnlyList<string> NeededBy { get; init; } = [];

    // sp-mod reports this dependency as wanted at incompatible versions by different mods.
    public bool Conflict { get; init; }

    // sp-mod's own "Not compatible" badge on the page: no version for the list's target.
    public bool NotCompatible { get; init; }

    // An addon whose parent isn't on the list, so a retarget had no parent version to fit it to.
    public bool ParentUnknown { get; init; }

    public SpModReviewChange Change { get; init; }

    // The stored version, when Change is VersionChanged.
    public string? StoredVersion { get; init; }

    // What sp-mod's card showed for it - thumbnail, author, badges. Null for a row that isn't on the
    // page; the window fills those from the app's catalog.
    public SpModCard? Card { get; init; }
}

// A row's dependencies split by whether the list, as ticked, provides them.
public sealed record SpModDependencyCoverage(IReadOnlyList<string> Covered, IReadOnlyList<string> Missing)
{
    public static SpModDependencyCoverage None { get; } = new([], []);

    public bool Any => Covered.Count + Missing.Count > 0;

    public bool Satisfied => Missing.Count == 0;
}

//
// Everything the import window shows, and the list its ticks make.
//
// Built from the page, the retarget and dependency results (either may be missing - not asked, or
// failed), and the stored list on a refresh. The window only flips Ticked; ToModList and Merge do
// the rest, so what is stored is decided here and tested here.
//
public sealed class SpModListReview
{
    public required SpModListPage Page { get; init; }

    public ModList? Stored { get; init; }

    public required IReadOnlyList<SpModReviewRow> Rows { get; init; }

    // Dependencies sp-mod knows of but has no version of for the target SPT. Shown, never added.
    public IReadOnlyList<SpModDependency> Unaddable { get; init; } = [];

    // Entries the stored list has that this read no longer offers, on a refresh.
    public IReadOnlyList<ModListEntry> Removed { get; init; } = [];

    // The retarget asked for, when one ran.
    public SpModRetarget? Retarget { get; init; }

    public SpModDependencies? Dependencies { get; init; }

    public bool Retargeted => Retarget is { Succeeded: true };

    public bool IsRefresh => Stored is not null;

    public IEnumerable<SpModReviewRow> In(SpModReviewSection section) => Rows.Where(r => r.Section == section);

    public IEnumerable<SpModReviewRow> TickedRows => Rows.Where(r => r.Ticked);

    //
    // A mod the page could only show at a version for another SPT: sp-mod has nothing of it for the
    // list's target, so it drew the closest version with a "Not compatible" badge. A retarget
    // replaces those versions with ones for this install (or moves them to NoVersion), so once one
    // has run nothing is left in this state.
    //
    public bool IsBuiltForOtherSpt(SpModReviewRow row) =>
        row.Section == SpModReviewSection.Mods && row.NotCompatible && !Retargeted;

    //
    // Which of the dependencies sp-mod lists on a row's card the list will actually provide, by the
    // ticks as they stand. A dependency is covered when a ticked row carries its name. One the
    // review has no row for at all - already on the list under a name sp-mod shows differently, or
    // simply not asked about - goes by what sp-mod's card said. Covered first, then missing, each in
    // sp-mod's order.
    //
    public SpModDependencyCoverage Coverage(SpModReviewRow row)
    {
        var dependencies = row.Card?.Dependencies ?? [];
        if (dependencies.Count == 0) return SpModDependencyCoverage.None;

        var ticked = Rows.Where(r => r.Ticked).Select(r => r.Entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewed = Rows.Select(r => r.Entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var covered = new List<string>();
        var missing = new List<string>();

        foreach (var dependency in dependencies)
        {
            var ok = ticked.Contains(dependency.Name) || (!reviewed.Contains(dependency.Name) && dependency.OnList);
            (ok ? covered : missing).Add(dependency.Name);
        }

        return new SpModDependencyCoverage(covered, missing);
    }

    //
    // The rows for a page.
    //
    // previous is what was decided last time - the stored list's choices on a refresh, or the
    // window's current ticks when it rebuilds after the retarget switch is flipped - so an item
    // unticked once stays unticked. Without it, the defaults apply: everything ticked bar items
    // with no version for the target.
    //
    public static SpModListReview Build(
        SpModListPage page,
        ModList? stored = null,
        SpModRetarget? retarget = null,
        SpModDependencies? dependencies = null,
        SpModListChoices? previous = null)
    {
        previous ??= stored is null ? SpModListChoices.None : SpModListChoices.From(stored);

        var excluded = new HashSet<string>(previous.Excluded, StringComparer.OrdinalIgnoreCase);
        var storedByRef = (stored?.Entries ?? [])
            .Where(e => SpModListSource.RefFor(e) is not null)
            .GroupBy(e => SpModListSource.RefFor(e)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var retargeted = retarget is { Succeeded: true }
            ? retarget.Rows
                .Where(r => SpModListSource.RefFor(r.From) is not null)
                .GroupBy(r => SpModListSource.RefFor(r.From)!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : [];

        var rows = new List<SpModReviewRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var namesById = page.Mods.ToDictionary(m => m.Id, m => m.Name);

        (SpModReviewChange Change, string? StoredVersion) Compare(string key, ModListEntry entry)
        {
            if (stored is null) return (SpModReviewChange.None, null);

            // Left out last time on purpose, so not something this read has brought.
            if (!storedByRef.TryGetValue(key, out var was))
                return (excluded.Contains(key) ? SpModReviewChange.None : SpModReviewChange.New, null);

            return string.Equals(was.Version, entry.Version, StringComparison.OrdinalIgnoreCase)
                ? (SpModReviewChange.None, null)
                : (SpModReviewChange.VersionChanged, was.Version);
        }

        void Add(SpModReviewRow row)
        {
            if (!seen.Add(row.Ref)) return;
            rows.Add(row);
        }

        // ---- page items ----

        foreach (var item in page.Items)
        {
            var isAddon = item.Kind == SpModListItemKind.Addon;
            var key = SpModListSource.RefFor(isAddon, item.Id);

            var fromPage = new ModListEntry { Name = item.Name, ModId = item.Id, IsAddon = isAddon, Version = item.Version };

            var section = isAddon ? SpModReviewSection.Addons : SpModReviewSection.Mods;
            var entry = fromPage;
            string? fromVersion = null;
            var parentUnknown = false;

            if (retargeted.TryGetValue(key, out var r))
            {
                switch (r.Outcome)
                {
                    case SpModRetargetOutcome.Changed:
                        entry = r.To;
                        fromVersion = item.Version;
                        break;
                    case SpModRetargetOutcome.NoVersion:
                        section = SpModReviewSection.NoVersion;
                        break;
                    case SpModRetargetOutcome.ParentUnknown:
                        parentUnknown = true;
                        break;
                }
            }

            var (change, storedVersion) = Compare(key, entry);

            Add(new SpModReviewRow
            {
                Ref = key,
                Section = section,
                Entry = entry,
                OnPage = true,
                Ticked = !excluded.Contains(key) && section != SpModReviewSection.NoVersion,
                FromVersion = fromVersion,
                ParentName = isAddon && item.ParentModId is { } p
                    ? namesById.GetValueOrDefault(p) ?? item.ParentName
                    : null,
                NotCompatible = item.NotCompatible,
                ParentUnknown = parentUnknown,
                Change = change,
                StoredVersion = storedVersion,
                Card = item.Card,
            });
        }

        var deps = dependencies is { Succeeded: true } ? dependencies.Missing : [];
        var depsById = deps.GroupBy(d => d.ModId).ToDictionary(g => g.Key, g => g.First());

        // ---- missing parents (R4) ----
        //
        // From the page first: a detached card names the parent and its version even when the
        // dependency check failed. The dependency answer, when there is one, has the version for
        // the target SPT and wins.
        //
        var addonNamesByParent = page.Addons
            .Where(a => !a.ParentOnList && a.ParentModId is not null)
            .GroupBy(a => a.ParentModId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(a => a.Name)]);

        var parentCards = page.Addons
            .Where(a => !a.ParentOnList && a.ParentModId is not null && a.ParentCard is not null)
            .GroupBy(a => a.ParentModId!.Value)
            .ToDictionary(g => g.Key, g => g.First().ParentCard);

        foreach (var addon in page.Addons.Where(a => !a.ParentOnList && a.ParentModId is not null))
        {
            var id = addon.ParentModId!.Value;
            var key = SpModListSource.RefFor(false, id);

            var entry = depsById.TryGetValue(id, out var dep) && dep.HasVersion
                ? dep.ToEntry()
                : new ModListEntry { Name = addon.ParentName ?? $"mod {id}", ModId = id, Version = addon.ParentVersion };

            var (change, storedVersion) = Compare(key, entry);

            Add(new SpModReviewRow
            {
                Ref = key,
                Section = SpModReviewSection.MissingParents,
                Entry = entry,
                OnPage = false,
                Ticked = !excluded.Contains(key),
                NeededBy = addonNamesByParent[id],
                Change = change,
                StoredVersion = storedVersion,
                Card = parentCards.GetValueOrDefault(id),
            });
        }

        // ---- dependencies ----

        foreach (var dep in deps.Where(d => d.HasVersion))
        {
            var key = SpModListSource.RefFor(false, dep.ModId);
            var entry = dep.ToEntry();
            var (change, storedVersion) = Compare(key, entry);

            Add(new SpModReviewRow
            {
                Ref = key,
                Section = dep.IsParent ? SpModReviewSection.MissingParents : SpModReviewSection.Dependencies,
                Entry = entry,
                OnPage = false,
                Ticked = !excluded.Contains(key),
                NeededBy = dep.NeededBy,
                Conflict = dep.Conflict,
                Change = change,
                StoredVersion = storedVersion,
            });
        }

        //
        // ---- added last time, and not offered by this read ----
        //
        // A dependency added on an earlier day stays on the list when this read didn't check
        // dependencies (or the check failed) - dropping it would be the refresh removing something
        // the user chose.
        //
        foreach (var entry in previous.Added)
        {
            if (SpModListSource.RefFor(entry) is not { } key || seen.Contains(key)) continue;

            Add(new SpModReviewRow
            {
                Ref = key,
                Section = SpModReviewSection.Dependencies,
                Entry = entry,
                OnPage = false,
                Ticked = !excluded.Contains(key),
            });
        }

        var removed = stored is null
            ? []
            : stored.Entries.Where(e => SpModListSource.RefFor(e) is not { } key || !seen.Contains(key)).ToList();

        return new SpModListReview
        {
            Page = page,
            Stored = stored,
            Rows = [.. rows.OrderBy(r => r.Section)],
            Unaddable = [.. deps.Where(d => !d.HasVersion)],
            Removed = removed,
            Retarget = retarget,
            Dependencies = dependencies,
        };
    }

    // The window's ticks as choices - stored on the list, and handed back to Build on a rebuild.
    public SpModListChoices ToChoices() => new(
        [.. Rows.Where(r => !r.Ticked).Select(r => r.Ref)],
        [.. Rows.Where(r => r.Ticked && !r.OnPage).Select(r => r.Entry)],
        Retargeted,
        Dependencies is { Succeeded: true },
        Retargeted ? Retarget!.TargetSptVersion : null,
        Rows.Where(r => r.OnPage && r.FromVersion is not null)
            .ToDictionary(r => r.Ref, r => r.Entry, StringComparer.OrdinalIgnoreCase));

    //
    // The list to store, folded into the stored one. The revision moves only when something
    // changed (SpModListImport.Merge).
    //
    public SpModListUpdate ToUpdate(string fallbackName, DateTimeOffset now) =>
        SpModListImport.Merge(Stored, Page.ToModList(fallbackName, now, ToChoices()));
}
