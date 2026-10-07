using System.Globalization;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-23: the App's side of DependencyVersionSolver, shared by Dependencies and Conflicts, the
// Installed cards (S4), the Play page (R4) and the download queue's update check. Core works out the
// requirements; every sentence about them is here.
//
public static class DependencyVersions
{
    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);

    public static AcceptedWarningStore Accepted { get; } = new();

    // A solve and the cards it was made from - SolverMod.Key is the card's index.
    public sealed record Result(IReadOnlyList<InstalledModCardViewModel> Cards, IReadOnlyList<DependencyVersionReport> Reports)
    {
        public InstalledModCardViewModel CardOf(SolverMod mod) => Cards[int.Parse(mod.Key, CultureInfo.InvariantCulture)];
    }

    public static List<SolverMod> ToSolverMods(IReadOnlyList<InstalledModCardViewModel> cards) =>
        cards
            .Select((c, i) => new SolverMod(i.ToString(CultureInfo.InvariantCulture), c.DisplayTitle,
                c is { IsAddon: false, ModId: not null } ? c.ModId : null, c.Entries))
            .ToList();

    public static IEnumerable<string> Published(int modId) =>
        (AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == modId)?.Versions ?? []).Select(v => v.Version ?? "");

    // Whether the user accepted sp-mod's range for this pair at these versions (R6).
    public static bool IsAccepted(int dependentModId, string? dependentVersion, int dependencyModId, string? dependencyVersion) =>
        Accepted.IsAccepted(AcceptedWarningStore.Key(dependentModId, dependentVersion, dependencyModId, dependencyVersion));

    public static Result Solve(IReadOnlyList<InstalledModCardViewModel> cards, string? sptVersion)
    {
        var byModId = cards
            .Where(c => c is { IsAddon: false, ModId: not null })
            .GroupBy(c => c.ModId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var accepted = Accepted.Load();

        // sp-mod's ranges, from its held-back answer, minus the pairs the user accepted at these versions.
        var spMod = AppServices.HeldBack.All(sptVersion)
            .SelectMany(h => h.Blockers
                .Where(b => !string.IsNullOrWhiteSpace(b.Constraint))
                .Select(b => new SpModRequirement(b.ModId, h.ModId, b.Constraint!)))
            .Where(r => !(byModId.TryGetValue(r.DependentModId, out var dependent)
                          && byModId.TryGetValue(r.DependencyModId, out var dependency)
                          && accepted.Contains(AcceptedWarningStore.Key(
                              r.DependentModId, dependent.InstalledVersion, r.DependencyModId, dependency.InstalledVersion))))
            .ToList();

        return new Result(cards, DependencyVersionSolver.Solve(ToSolverMods(cards), Published, spMod));
    }

    // R4: "SPT will load no server mods: Eco needs WTT - CommonLib. ..." or null when nothing does.
    public static string? InstallWideText(Result result, bool forPlayPage = false)
    {
        var pieces = result.Reports
            .Where(r => r.InstallWide)
            .SelectMany(r => r.Requirements
                .Where(q => q.Source == RequirementSource.Files && !q.IsOptional && q.Side == InstalledModTarget.Server
                            && q.Standing is RequirementStanding.Missed or RequirementStanding.Missing)
                .Select(q => Text(Strings.Dependencies_InstallWidePieceFormat, q.Dependent.Name, r.Dependency?.Name ?? r.Identifier)))
            .Distinct()
            .ToList();

        return pieces.Count == 0
            ? null
            : Text(forPlayPage ? Strings.Play_InstallWideFormat : Strings.Dependencies_InstallWideFormat, TextLists.Join(pieces));
    }

    //
    // S4: each card whose own files ask for something the install doesn't meet gets a "won't load"
    // tooltip, one line per dependency. Cards with nothing missed are cleared.
    //
    public static void Apply(Result result)
    {
        var lines = new Dictionary<InstalledModCardViewModel, List<string>>();

        foreach (var report in result.Reports)
        foreach (var req in report.Requirements)
        {
            if (req.Source != RequirementSource.Files || req.IsOptional) continue;
            if (req.Standing is not (RequirementStanding.Missed or RequirementStanding.Missing)) continue;

            var card = result.CardOf(req.Dependent);
            var name = report.Dependency?.Name ?? report.Identifier;
            var line = req.Standing == RequirementStanding.Missing
                ? Text(Strings.Installed_VersionMissingFormat, name)
                : Text(Strings.Installed_VersionMissedFormat, name, Needs(req, report), req.Found);

            if (!lines.TryGetValue(card, out var list)) lines[card] = list = [];
            if (!list.Contains(line)) list.Add(line);
        }

        foreach (var card in result.Cards)
        {
            card.VersionSummary = lines.TryGetValue(card, out var list)
                ? Text(Strings.Installed_StatusVersionFormat, string.Join("\n", list))
                : null;
        }
    }

    // "needs up to 3.0.3" / "needs 3.0.4 or later" / "needs 3.0.3".
    public static string Needs(DependencyRequirement req, DependencyVersionReport report)
    {
        if (req.Range is not { Length: > 0 } range) return Strings.Dependencies_VersionNeedsAny;

        var versions = report.Dependency?.ModId is { } id
            ? AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id)?.Versions
            : null;

        return RangeWording.Describe(range, req.Found, versions) switch
        {
            (var v, RangeWordingKind.UpTo) => Text(Strings.Dependencies_VersionNeedsUpToFormat, v),
            (var v, RangeWordingKind.AtLeast) => Text(Strings.Dependencies_VersionNeedsAtLeastFormat, v),
            var (v, _) => Text(Strings.Dependencies_VersionNeedsFormat, v),
        };
    }
}
