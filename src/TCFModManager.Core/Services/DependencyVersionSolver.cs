using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// One installed mod as the solver sees it: an sp-mod listing's worth of entries (a client half, a
// server half, patchers), identified by a key the caller chooses (the App passes its card key).
public sealed record SolverMod(string Key, string Name, int? ModId, IReadOnlyList<InstalledMod> Entries);

// Where a requirement comes from. Files decide errors; sp-mod only ever warns (OPEN-23 R1).
public enum RequirementSource
{
    Files,
    SpMod,
}

// How one requirement stands against what is installed.
public enum RequirementStanding
{
    Met,

    // The dependency is loaded but its declared version is outside the range.
    Missed,

    // Nothing loaded answers to the identifier: not installed, or only a disabled copy.
    Missing,

    // A range or version that couldn't be read - no verdict either way.
    Unknown,
}

//
// One dependent asking for one dependency. Dependent is null only for an sp-mod requirement whose
// mod isn't installed (never produced today). Range is as written; Found is the dependency's declared
// version on that side, null when Missing.
//
public sealed record DependencyRequirement(
    SolverMod Dependent,
    RequirementSource Source,
    InstalledModTarget Side,
    string Identifier,
    string? Range,
    bool IsOptional,
    string? Found,
    RequirementStanding Standing);

//
// Everything installed mods ask of one dependency. Dependency is null when nothing installed answers
// to Identifier at all.
//
// Conflict: some file requirement is missed and no published version meets every file requirement -
// changing the dependency's version alone can't fix it. FixVersion: the newest published version that
// meets every file requirement, when the installed one misses some and switching would fix them.
// InstallWide: a hard server requirement is missed or missing, which makes SPT load no server mods at
// all (OPEN-23 K1).
//
public sealed record DependencyVersionReport(
    SolverMod? Dependency,
    string Identifier,
    IReadOnlyList<DependencyRequirement> Requirements,
    bool Conflict,
    string? FixVersion,
    bool InstallWide)
{
    public bool HasFileProblem => Requirements.Any(r =>
        r.Source == RequirementSource.Files && !r.IsOptional
        && r.Standing is RequirementStanding.Missed or RequirementStanding.Missing);

    public bool HasSpModWarning => Requirements.Any(r =>
        r.Source == RequirementSource.SpMod && r.Standing == RequirementStanding.Missed);
}

// An sp-mod range for one dependent/dependency pair (from its held-back answer: blocker -> held mod).
public sealed record SpModRequirement(int DependentModId, int DependencyModId, string Constraint);

//
// OPEN-23 S1: every version requirement among the installed mods, worked out across the whole install
// at once - no batching, nothing taken from sp-mod's per-request "conflict" flag. Built from what each
// mod's own files declare (what SPT and BepInEx check), plus any ranges sp-mod gives, judged against
// each dependency's declared version and its published versions.
//
public static class DependencyVersionSolver
{
    //
    // One report per dependency at least one loaded mod asks for. <paramref name="publishedVersions"/>
    // gives a listing's published versions (newest first or in any order) for FixVersion; null or empty
    // leaves FixVersion and Conflict unanswered (Conflict false).
    //
    public static IReadOnlyList<DependencyVersionReport> Solve(
        IReadOnlyList<SolverMod> mods,
        Func<int, IEnumerable<string>>? publishedVersions = null,
        IEnumerable<SpModRequirement>? spMod = null)
    {
        var byIdentifier = new Dictionary<string, List<(SolverMod Mod, InstalledMod Entry)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        foreach (var entry in mod.Entries)
        foreach (var id in ModDependencyGraph.Identifiers(entry))
        {
            if (!byIdentifier.TryGetValue(id, out var list)) byIdentifier[id] = list = [];
            list.Add((mod, entry));
        }

        // Keyed by the dependency mod's key when installed, else by "?identifier".
        var groups = new Dictionary<string, (SolverMod? Dependency, string Identifier, List<DependencyRequirement> Reqs)>(StringComparer.OrdinalIgnoreCase);

        void Add(SolverMod? dependency, string identifier, DependencyRequirement requirement)
        {
            var key = dependency?.Key ?? "?" + identifier;
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = (dependency, identifier, []);
            g.Reqs.Add(requirement);
        }

        foreach (var mod in mods)
        foreach (var entry in mod.Entries.Where(e => !e.IsDisabled))
        foreach (var declared in entry.Dependencies)
        {
            byIdentifier.TryGetValue(declared.Identifier, out var answers);
            var sameSide = (answers ?? []).Where(a => a.Entry.Target == entry.Target && !ReferenceEquals(a.Mod, mod)).ToList();

            // A mod's halves depending on each other isn't something to report.
            if ((answers ?? []).Any(a => ReferenceEquals(a.Mod, mod)) && sameSide.Count == 0) continue;

            var dependency = sameSide.Select(a => a.Mod).FirstOrDefault();
            var loaded = sameSide.Where(a => !a.Entry.IsDisabled).Select(a => a.Entry).ToList();

            RequirementStanding standing;
            string? found = null;

            if (loaded.Count == 0)
            {
                // Client side, a GUID nothing scanned answers to is as likely SPT's or BepInEx's own
                // plugins (never scanned) as a missing mod, and BepInEx only skips that one plugin -
                // the sp-mod tree already reports missing mods. Server side SPT loads nothing (K1).
                if (entry.Target == InstalledModTarget.Client) continue;
                standing = RequirementStanding.Missing;
            }
            else if (string.IsNullOrWhiteSpace(declared.VersionRange))
            {
                standing = RequirementStanding.Met;
                found = loaded[0].Version;
            }
            else
            {
                // Any loaded copy meeting the range satisfies it; otherwise report the first.
                var verdicts = loaded.Select(e => (e.Version, Ok: ModVersionMatcher.IsSatisfiedBy(declared.VersionRange, e.Version))).ToList();
                var met = verdicts.FirstOrDefault(v => v.Ok == true);
                if (met.Ok == true) { standing = RequirementStanding.Met; found = met.Version; }
                else if (verdicts.Any(v => v.Ok == false)) { standing = RequirementStanding.Missed; found = verdicts.First(v => v.Ok == false).Version; }
                else { standing = RequirementStanding.Unknown; found = verdicts[0].Version; }
            }

            Add(dependency, declared.Identifier, new DependencyRequirement(
                mod, RequirementSource.Files, entry.Target, declared.Identifier, declared.VersionRange,
                declared.IsSoft, found, standing));
        }

        var byModId = mods.Where(m => m.ModId is not null).GroupBy(m => m.ModId!.Value).ToDictionary(g => g.Key, g => g.First());

        foreach (var req in spMod ?? [])
        {
            if (!byModId.TryGetValue(req.DependentModId, out var dependent)) continue;
            if (!byModId.TryGetValue(req.DependencyModId, out var dependency)) continue;

            // sp-mod's ranges are against the listing's version, which the declared version of either
            // half stands in for.
            var found = dependency.Entries.Where(e => !e.IsDisabled).Select(e => e.Version).FirstOrDefault(v => v is not null);
            var ok = ModVersionMatcher.IsSatisfiedBy(req.Constraint, found);

            Add(dependency, dependency.Name, new DependencyRequirement(
                dependent, RequirementSource.SpMod, InstalledModTarget.Server, dependency.Name, req.Constraint, false, found,
                found is null ? RequirementStanding.Missing
                : ok == true ? RequirementStanding.Met
                : ok == false ? RequirementStanding.Missed
                : RequirementStanding.Unknown));
        }

        var reports = new List<DependencyVersionReport>();

        foreach (var (dependency, identifier, reqs) in groups.Values)
        {
            var hardFiles = reqs.Where(r => r.Source == RequirementSource.Files && !r.IsOptional).ToList();
            var ranged = hardFiles.Where(r => !string.IsNullOrWhiteSpace(r.Range)).ToList();
            var missed = ranged.Any(r => r.Standing == RequirementStanding.Missed);

            string? fix = null;
            var conflict = false;

            if (missed && dependency?.ModId is { } id && publishedVersions is not null)
            {
                var published = publishedVersions(id).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                if (published.Count > 0)
                {
                    fix = published
                        .Where(v => ranged.All(r => ModVersionMatcher.IsSatisfiedBy(r.Range, v) == true))
                        .OrderByDescending(v => v, Comparer<string>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0))
                        .FirstOrDefault();
                    conflict = fix is null;
                }
            }

            var installWide = hardFiles.Any(r =>
                r.Side == InstalledModTarget.Server
                && r.Standing is RequirementStanding.Missed or RequirementStanding.Missing);

            reports.Add(new DependencyVersionReport(dependency, identifier, reqs, conflict, fix, installWide));
        }

        return reports
            .OrderByDescending(r => r.InstallWide)
            .ThenByDescending(r => r.Conflict)
            .ThenByDescending(r => r.HasFileProblem)
            .ThenBy(r => r.Dependency?.Name ?? r.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    //
    // OPEN-23 S4: the file requirements installing <paramref name="newVersion"/> of a mod would break -
    // met (or not yet asked of, for a new install) today, missed with the new version. The mod is found
    // by sp-mod id among the installed mods, or by <paramref name="guid"/> when it isn't installed yet.
    // The new version is the sp-mod version string, which stands in for the declared version its files
    // will carry. Optional dependencies never count.
    //
    public static IReadOnlyList<DependencyRequirement> WouldBreak(
        IReadOnlyList<SolverMod> mods, int modId, string? guid, string newVersion)
    {
        var target = mods.FirstOrDefault(m => m.ModId == modId);

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sides = new HashSet<InstalledModTarget>();
        if (target is not null)
        {
            foreach (var entry in target.Entries)
            {
                foreach (var id in ModDependencyGraph.Identifiers(entry)) identifiers.Add(id);
                sides.Add(entry.Target);
            }
        }
        else if (!string.IsNullOrWhiteSpace(guid))
        {
            identifiers.Add(guid);
            sides.Add(InstalledModTarget.Client);
            sides.Add(InstalledModTarget.Server);
        }

        var broken = new List<DependencyRequirement>();
        if (identifiers.Count == 0) return broken;

        foreach (var mod in mods)
        {
            if (ReferenceEquals(mod, target)) continue;

            foreach (var entry in mod.Entries.Where(e => !e.IsDisabled && sides.Contains(e.Target)))
            foreach (var declared in entry.Dependencies)
            {
                if (declared.IsSoft || string.IsNullOrWhiteSpace(declared.VersionRange)) continue;
                if (!identifiers.Contains(declared.Identifier)) continue;
                if (ModVersionMatcher.IsSatisfiedBy(declared.VersionRange, newVersion) != false) continue;

                var current = target?.Entries
                    .Where(e => !e.IsDisabled && e.Target == entry.Target)
                    .Select(e => e.Version)
                    .FirstOrDefault(v => v is not null);

                // Already broken today - not something this install does.
                if (current is not null && ModVersionMatcher.IsSatisfiedBy(declared.VersionRange, current) == false) continue;

                broken.Add(new DependencyRequirement(
                    mod, RequirementSource.Files, entry.Target, declared.Identifier, declared.VersionRange,
                    false, newVersion, RequirementStanding.Missed));
            }
        }

        return broken;
    }
}
