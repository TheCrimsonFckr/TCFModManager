using System.Text.RegularExpressions;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// OPEN-44 §3: where each list entry should go, as pure rules over facts the operator's install holds.
public enum PlacementRule
{
    // R1
    SyncAddonAvailable,

    // R1, information only
    FikaAddonAvailable,

    // R2
    AddonWiderThanParent,

    // R3
    DependencyUncovered,

    // R3, the reverse
    LibraryCanLeaveHeadless,

    // R4
    NothingToLoad,

    // R5
    BotsCarryItems,

    // R6
    AdviceNamesEntry,

    // R9
    OnlyTheSync,
}

public enum PlacementSeverity
{
    Info,
    Hint,
    Warning,
}

// Absent from PlacementFacts.Halves means unknown.
public sealed record PlacementHalves(bool HasClient, bool HasServer, bool ShipsItems = false);

public sealed record PlacementDependency(ModListEntry Dependent, int DependencyModId);

public sealed record PlacementFinding
{
    public required PlacementRule Rule { get; init; }

    public required PlacementSeverity Severity { get; init; }

    public required ModListEntry Entry { get; init; }

    // Null: information only.
    public ModListEntryScope? SuggestedScope { get; init; }

    // R2, non-sync addon: narrow the addon instead.
    public ModListEntryScope? AlternativeScope { get; init; }

    // R2/R9 the addon, R3 the dependent, R6 the entry whose advice names this one.
    public ModListEntry? Other { get; init; }

    // R1
    public Addon? Addon { get; init; }

    public ModListEntryScope? AddonScope { get; init; }

    // R6
    public string? Quote { get; init; }
}

public sealed record PlacementFacts
{
    public required IReadOnlyList<ModListEntry> Entries { get; init; }

    public IReadOnlyDictionary<ModListEntry, PlacementHalves> Halves { get; init; } =
        new Dictionary<ModListEntry, PlacementHalves>();

    public IReadOnlyList<Addon> Addons { get; init; } = [];

    public IReadOnlyList<PlacementDependency> Dependencies { get; init; } = [];

    public IReadOnlyDictionary<ModListEntry, HeadlessAdvice> Advice { get; init; } =
        new Dictionary<ModListEntry, HeadlessAdvice>();

    // R1, by sp-mod addon id, for addons on no list.
    public IReadOnlyDictionary<int, HeadlessAdvice> AddonAdvice { get; init; } = new Dictionary<int, HeadlessAdvice>();
}

public static partial class ModListPlacement
{
    private const ModListEntryScope Loaders = ModListEntryScope.Client | ModListEntryScope.Headless;

    // Q2 (c): a server half that depends on WTT-CommonLib ships items.
    public const int WttCommonLibModId = 2310;

    // Null when the scan found no half for it.
    public static PlacementHalves? HalvesOf(ModListCandidate candidate, bool dependsOnCommonLib = false)
    {
        if (candidate.Entries.Count == 0) return null;

        var servers = candidate.Entries.Where(e => e.Target == InstalledModTarget.Server).ToList();

        return new PlacementHalves(
            candidate.Entries.Any(e => e.Target == InstalledModTarget.Client),
            servers.Count > 0,
            servers.Count > 0 && (dependsOnCommonLib || servers.Any(s => ShipsItemFolders(s.FolderPath))));
    }

    // Q2 (b) as checked 2026-11-15: a folder named CustomItems, or ending in Items, directly under db\ or Resources\.
    public static bool ShipsItemFolders(string serverFolder)
    {
        try
        {
            foreach (var under in Directory.Exists(serverFolder) ? Directory.EnumerateDirectories(serverFolder) : [])
            {
                var name = Path.GetFileName(under);
                if (!name.Equals("db", StringComparison.OrdinalIgnoreCase) && !name.Equals("Resources", StringComparison.OrdinalIgnoreCase)) continue;

                if (Directory.EnumerateDirectories(under).Any(d => Path.GetFileName(d).EndsWith("Items", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("ModLists", $"couldn't look for item folders in {serverFolder}: {ex.Message}");
        }

        return false;
    }

    [GeneratedRegex(@"\bsync\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Sync();

    [GeneratedRegex(@"\b(fika|headless)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Fika();

    public static bool LooksLikeSync(Addon addon) => Sync().IsMatch(Words(addon));

    public static bool MentionsFika(Addon addon) => Fika().IsMatch(Words(addon));

    private static string Words(Addon addon) => (addon.Name ?? "") + " " + (addon.Teaser ?? "");

    public static IReadOnlyList<PlacementFinding> Check(PlacementFacts facts)
    {
        var findings = new List<PlacementFinding>();
        var entries = facts.Entries;

        var mods = entries.Where(e => !e.IsAddon && e.ModId is not null).ToDictionary(e => e.ModId!.Value);
        var addonsOnList = entries.Where(e => e.IsAddon && e.ModId is not null).ToDictionary(e => e.ModId!.Value);
        var cached = facts.Addons.Where(a => a.ModId is not null).ToList();

        OnlyTheSync(facts, mods, addonsOnList, cached, findings);
        SyncAddons(facts, mods, addonsOnList, cached, findings);
        AddonsWiderThanParent(facts, mods, addonsOnList, cached, findings);
        NothingToLoad(facts, entries, findings);
        BotsCarryItems(facts, entries, findings);
        Dependencies(facts, mods, findings);
        AdviceNamesEntries(facts, entries, findings);

        return findings;
    }

    // R1 - sync addon on sp-mod, on no list.
    private static void SyncAddons(
        PlacementFacts facts,
        Dictionary<int, ModListEntry> mods,
        Dictionary<int, ModListEntry> addonsOnList,
        List<Addon> cached,
        List<PlacementFinding> findings)
    {
        foreach (var addon in cached)
        {
            if (addonsOnList.ContainsKey(addon.Id)) continue;
            if (!mods.TryGetValue(addon.ModId!.Value, out var parent)) continue;

            var sync = LooksLikeSync(addon)
                || (facts.AddonAdvice.TryGetValue(addon.Id, out var advice) && advice.Verdict == HeadlessVerdict.Needs);
            if (!sync && !MentionsFika(addon)) continue;

            var scope = parent.EffectiveScope & Loaders;
            if (sync && scope == 0) continue;

            findings.Add(new PlacementFinding
            {
                Rule = sync ? PlacementRule.SyncAddonAvailable : PlacementRule.FikaAddonAvailable,
                Severity = sync ? PlacementSeverity.Hint : PlacementSeverity.Info,
                Entry = parent,
                Addon = addon,
                AddonScope = sync ? scope : null,
            });
        }
    }

    // R2 - addon wider than its parent.
    private static void AddonsWiderThanParent(
        PlacementFacts facts,
        Dictionary<int, ModListEntry> mods,
        Dictionary<int, ModListEntry> addonsOnList,
        List<Addon> cached,
        List<PlacementFinding> findings)
    {
        foreach (var addon in cached)
        {
            if (!addonsOnList.TryGetValue(addon.Id, out var entry)) continue;
            if (!mods.TryGetValue(addon.ModId!.Value, out var parent)) continue;
            if (facts.Advice.TryGetValue(entry, out var advice) && advice.OnlyThis) continue;

            var reach = entry.EffectiveScope & Loaders;
            var missing = reach & ~parent.EffectiveScope;
            if (missing == 0) continue;

            var sync = LooksLikeSync(addon);

            findings.Add(new PlacementFinding
            {
                Rule = PlacementRule.AddonWiderThanParent,
                Severity = PlacementSeverity.Warning,
                Entry = parent,
                Other = entry,
                SuggestedScope = parent.EffectiveScope | missing,
                AlternativeScope = sync ? null : entry.EffectiveScope & ~missing,
            });
        }
    }

    // R3 - dependency coverage by half, and the reverse hint.
    private static void Dependencies(PlacementFacts facts, Dictionary<int, ModListEntry> mods, List<PlacementFinding> findings)
    {
        var dependentsOf = new Dictionary<ModListEntry, List<ModListEntry>>();

        foreach (var dependency in facts.Dependencies)
        {
            if (!mods.TryGetValue(dependency.DependencyModId, out var needed)) continue;
            if (ReferenceEquals(needed, dependency.Dependent)) continue;

            if (!dependentsOf.TryGetValue(needed, out var dependents)) dependentsOf[needed] = dependents = [];
            dependents.Add(dependency.Dependent);

            if (facts.Halves.TryGetValue(needed, out var halves) && !halves.HasClient) continue;

            var dependentLoads = facts.Halves.TryGetValue(dependency.Dependent, out var own) && !own.HasClient
                ? 0
                : dependency.Dependent.EffectiveScope & Loaders;

            var missing = dependentLoads & ~needed.EffectiveScope;
            if (missing == 0) continue;

            findings.Add(new PlacementFinding
            {
                Rule = PlacementRule.DependencyUncovered,
                Severity = PlacementSeverity.Warning,
                Entry = needed,
                Other = dependency.Dependent,
                SuggestedScope = needed.EffectiveScope | missing,
            });
        }

        foreach (var (library, dependents) in dependentsOf)
        {
            var scope = library.EffectiveScope;
            if (!scope.HasFlag(ModListEntryScope.Headless) || scope == ModListEntryScope.Headless) continue;
            if (findings.Any(f => f.Rule == PlacementRule.OnlyTheSync && ReferenceEquals(f.Entry, library))) continue;
            if (facts.Halves.TryGetValue(library, out var halves) && !halves.HasClient) continue;
            if (dependents.Any(d => d.EffectiveScope.HasFlag(ModListEntryScope.Headless))) continue;
            if (dependents.Any(d => findings.Any(f => f.Rule == PlacementRule.BotsCarryItems && ReferenceEquals(f.Entry, d)))) continue;

            findings.Add(new PlacementFinding
            {
                Rule = PlacementRule.LibraryCanLeaveHeadless,
                Severity = PlacementSeverity.Hint,
                Entry = library,
                Other = dependents[0],
                SuggestedScope = scope & ~ModListEntryScope.Headless,
            });
        }
    }

    // R4 - nothing to load there.
    private static void NothingToLoad(PlacementFacts facts, IReadOnlyList<ModListEntry> entries, List<PlacementFinding> findings)
    {
        foreach (var entry in entries)
        {
            if (entry.IsAddon || !facts.Halves.TryGetValue(entry, out var halves)) continue;

            var scope = entry.EffectiveScope;
            ModListEntryScope? fix = (halves.HasClient, halves.HasServer) switch
            {
                (false, true) when !scope.HasFlag(ModListEntryScope.Server) => ModListEntryScope.Server,
                (true, false) when scope == ModListEntryScope.Server => Loaders,
                _ => null,
            };

            if (fix is null) continue;

            findings.Add(new PlacementFinding
            {
                Rule = PlacementRule.NothingToLoad,
                Severity = PlacementSeverity.Hint,
                Entry = entry,
                SuggestedScope = fix,
            });
        }
    }

    // R5 - items bots can carry, headless taken off.
    private static void BotsCarryItems(PlacementFacts facts, IReadOnlyList<ModListEntry> entries, List<PlacementFinding> findings)
    {
        foreach (var entry in entries)
        {
            if (entry.IsAddon || !facts.Halves.TryGetValue(entry, out var halves)) continue;
            if (!halves.HasClient || !halves.HasServer || !halves.ShipsItems) continue;

            var scope = entry.EffectiveScope;
            if (scope.HasFlag(ModListEntryScope.Headless)) continue;

            findings.Add(new PlacementFinding
            {
                Rule = PlacementRule.BotsCarryItems,
                Severity = PlacementSeverity.Warning,
                Entry = entry,
                SuggestedScope = scope | ModListEntryScope.Headless,
            });
        }
    }

    // R6 - advice that names another entry.
    private static void AdviceNamesEntries(PlacementFacts facts, IReadOnlyList<ModListEntry> entries, List<PlacementFinding> findings)
    {
        foreach (var (source, advice) in facts.Advice)
        {
            if (advice.Quotes.Count == 0) continue;

            foreach (var other in entries)
            {
                if (ReferenceEquals(other, source) || other.Name.Trim().Length < 4) continue;

                var quote = advice.Quotes.FirstOrDefault(q => Names(q, other));
                if (quote is null) continue;

                findings.Add(new PlacementFinding
                {
                    Rule = PlacementRule.AdviceNamesEntry,
                    Severity = PlacementSeverity.Info,
                    Entry = other,
                    Other = source,
                    Quote = quote,
                });
            }
        }
    }

    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "mod", "mods", "spt", "fika", "sync", "addon", "add-on", "client", "server", "headless",
        "pack", "fix", "fixes", "patch", "support", "compatibility", "module", "continued", "reloaded",
    };

    // The whole name, a folder name, or two or more consecutive meaningful words of the name ("common lib").
    private static bool Names(string quote, ModListEntry entry)
    {
        var name = entry.Name.Trim();
        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('-', '\'', '"', '(', ')', '[', ']', ':', ',', '.'))
            .Where(w => w.Length >= 3)
            .ToArray();

        if (words.All(Generic.Contains)) return false;
        if (quote.Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
        if (entry.Folders.Any(f => f.Length >= 6 && quote.Contains(f, StringComparison.OrdinalIgnoreCase))) return true;

        for (var i = 0; i + 1 < words.Length; i++)
        {
            if (Generic.Contains(words[i]) || Generic.Contains(words[i + 1])) continue;
            if (quote.Contains(words[i] + " " + words[i + 1], StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    // R9 - "only the sync" advice keeps the parent and its dependencies off the headless.
    private static void OnlyTheSync(
        PlacementFacts facts,
        Dictionary<int, ModListEntry> mods,
        Dictionary<int, ModListEntry> addonsOnList,
        List<Addon> cached,
        List<PlacementFinding> findings)
    {
        foreach (var addon in cached)
        {
            if (!addonsOnList.TryGetValue(addon.Id, out var entry)) continue;
            if (!facts.Advice.TryGetValue(entry, out var advice) || !advice.OnlyThis) continue;
            if (!mods.TryGetValue(addon.ModId!.Value, out var parent)) continue;

            var family = new List<ModListEntry> { parent };
            family.AddRange(facts.Dependencies
                .Where(d => ReferenceEquals(d.Dependent, parent) && mods.TryGetValue(d.DependencyModId, out _))
                .Select(d => mods[d.DependencyModId]));

            foreach (var member in family.Distinct())
            {
                var scope = member.EffectiveScope;
                var off = scope.HasFlag(ModListEntryScope.Headless) && scope != ModListEntryScope.Headless;

                findings.Add(new PlacementFinding
                {
                    Rule = PlacementRule.OnlyTheSync,
                    Severity = off ? PlacementSeverity.Warning : PlacementSeverity.Info,
                    Entry = member,
                    Other = entry,
                    SuggestedScope = off ? scope & ~ModListEntryScope.Headless : null,
                });
            }
        }
    }
}
