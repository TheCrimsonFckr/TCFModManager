using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// OPEN-44 S2: the "Where it goes" line - R1-R6 and R9 from ModListPlacement on each row.
public partial class ModListsViewModel
{
    private static readonly PlacementDependencies PlacementDeps = new();

    private ModListInstall? _placementInstall;

    private Dictionary<ModListCandidate, PlacementHalves?> _candidateHalves = new(ReferenceEqualityComparer.Instance);

    private Dictionary<ModListEntry, IReadOnlyList<PlacementFinding>> _placement = new(ReferenceEqualityComparer.Instance);

    private CancellationTokenSource? _placementDepsCancel;

    private void UsePlacementInstall(ModListInstall install)
    {
        if (ReferenceEquals(install, _placementInstall)) return;

        _placementInstall = install;
        _candidateHalves = new(ReferenceEqualityComparer.Instance);
    }

    private void UpdatePlacement()
    {
        var before = _placement;
        RecomputePlacement();

        var changed = false;

        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i].Entry;
            var was = before.GetValueOrDefault(entry) ?? [];
            var now = _placement.GetValueOrDefault(entry) ?? [];
            if (was.SequenceEqual(now)) continue;

            Entries[i] = Row(entry);
            changed = true;
        }

        if (changed) Notify();

        LoadPlacementDependencies();
    }

    private void RecomputePlacement()
    {
        _placement = new(ReferenceEqualityComparer.Instance);
        if (Selected is not { } list) return;

        foreach (var group in Findings(list.Id).GroupBy(f => f.Entry, ReferenceEqualityComparer.Instance))
            _placement[(ModListEntry)group.Key!] = [.. group.OrderByDescending(f => f.Severity)];
    }

    private List<PlacementFinding> Findings(Guid listId)
    {
        var entries = Entries.Select(e => e.Entry).ToList();
        if (entries.Count == 0) return [];

        var spt = AppServices.SptEnvironment.InstalledVersion;

        var dependencies = new List<PlacementDependency>();
        var needsCommonLib = new HashSet<ModListEntry>(ReferenceEqualityComparer.Instance);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(spt) || PlacementDeps.Known(entry, spt) is not { } ids) continue;

            foreach (var id in ids)
            {
                dependencies.Add(new PlacementDependency(entry, id));
                if (id == ModListPlacement.WttCommonLibModId) needsCommonLib.Add(entry);
            }
        }

        var halves = new Dictionary<ModListEntry, PlacementHalves>(ReferenceEqualityComparer.Instance);
        var links = new Dictionary<ModListEntry, AddonParentLink>(ReferenceEqualityComparer.Instance);

        if (_placementInstall is { } install)
        {
            var match = new ModListMatch(install.Candidates);

            foreach (var addon in entries.Where(e => e is { IsAddon: true, ModId: not null }))
            {
                var found = match.IndexOf(addon);
                if (found < 0 || AppServices.Addons.ById(addon.ModId!.Value)?.ModId is not { } parentId) continue;

                var guids = new List<string>();
                foreach (var parent in entries.Where(e => e is { IsAddon: false } && e.ModId == parentId))
                {
                    if (parent.Guid is { } guid) guids.Add(guid);
                    if (match.IndexOf(parent) is var at and >= 0)
                        guids.AddRange(install.Candidates[at].Entries.SelectMany(m => m.AllGuids));
                }

                links[addon] = ModListPlacement.LinkOf(install.Candidates[found], guids);
            }

            foreach (var entry in entries)
            {
                var found = match.IndexOf(entry);
                if (found < 0) continue;

                var candidate = install.Candidates[found];
                if (!_candidateHalves.TryGetValue(candidate, out var own))
                    _candidateHalves[candidate] = own = ModListPlacement.HalvesOf(candidate);

                if (own is null) continue;
                halves[entry] = needsCommonLib.Contains(entry) && own.HasServer ? own with { ShipsItems = true } : own;
            }
        }

        var advice = new Dictionary<ModListEntry, HeadlessAdvice>(ReferenceEqualityComparer.Instance);
        foreach (var entry in entries)
        {
            if (SubjectOf(entry) is { } subject && AppServices.HeadlessAdvice.Known(subject) is { Verdict: not HeadlessVerdict.None } own)
                advice[entry] = own;
        }

        var onList = entries.Where(e => !e.IsAddon && e.ModId is not null).Select(e => e.ModId!.Value).ToHashSet();
        var addonAdvice = new Dictionary<int, HeadlessAdvice>();
        foreach (var addon in AppServices.Addons.AllAddons)
        {
            if (addon.ModId is not { } parent || !onList.Contains(parent)) continue;
            if (AppServices.HeadlessAdvice.Known(HeadlessSubject.ForAddon(addon.Id)) is { Verdict: not HeadlessVerdict.None } own)
                addonAdvice[addon.Id] = own;
        }

        return [.. ModListPlacement.Check(new PlacementFacts
            {
                Entries = entries,
                Halves = halves,
                Addons = AppServices.Addons.AllAddons,
                Dependencies = dependencies,
                Advice = advice,
                AddonAdvice = addonAdvice,
                AddonLinks = links,
            })
            .Where(f => !_keptKeys.Contains(HeadlessAdviceDismissals.Key(listId, f.Entry, f.Rule)))];
    }

    // Q16: asked of sp-mod in the background for a list this install can edit; R3 lines follow the answer.
    private void LoadPlacementDependencies()
    {
        if (Selected is not { IsEditable: true } list) return;

        var spt = AppServices.SptEnvironment.InstalledVersion;
        if (string.IsNullOrWhiteSpace(spt)) return;

        var unasked = PlacementDeps.Unasked(Entries.Select(e => e.Entry), spt);
        if (unasked.Count == 0) return;

        _placementDepsCancel?.Cancel();
        var cancel = _placementDepsCancel = new CancellationTokenSource();

        _ = LoadPlacementDependenciesAsync(list.Id, unasked, spt, cancel);
    }

    private async Task LoadPlacementDependenciesAsync(Guid listId, List<ModListEntry> entries, string spt, CancellationTokenSource cancel)
    {
        await Task.Yield();
        if (cancel.IsCancellationRequested) return;

        if (!await PlacementDeps.ResolveAsync(entries, spt, cancel.Token)) return;
        if (cancel.IsCancellationRequested || Selected?.Id != listId) return;

        UpdatePlacement();
    }

    private ModListEntryRowViewModel WithPlacement(ModListEntryRowViewModel row)
    {
        if (!_placement.TryGetValue(row.Entry, out var findings) || findings.Count == 0) return row;

        var top = findings[0];

        return row with
        {
            Placement = top,
            PlacementLine = PlacementText(top),
            PlacementToolTip = findings.Count > 1
                ? Text(Strings.ModLists_PlaceMoreFormat, string.Join(Environment.NewLine, findings.Skip(1).Select(PlacementText)))
                : null,
            PlacementActionLabel = top switch
            {
                { Rule: PlacementRule.AddonLoadsAlone } => null,
                { Rule: PlacementRule.SyncAddonAvailable, AddonScope: { } scope } =>
                    Text(Strings.ModLists_PlaceAddFormat, ModListScopes.Label(scope)),
                { SuggestedScope: { } scope } => Text(Strings.ModLists_PlaceSetScopeFormat, ModListScopes.Label(scope)),
                _ => null,
            },
            PlacementAltLabel = top.AlternativeScope is { } alt
                ? Text(Strings.ModLists_PlaceNarrowAddonFormat, ModListScopes.Label(alt))
                : null,
        };
    }

    private string PlacementText(PlacementFinding finding) => finding.Rule switch
    {
        PlacementRule.SyncAddonAvailable => Text(Strings.ModLists_PlaceSyncAddonFormat, finding.Addon?.Name ?? string.Empty),
        PlacementRule.FikaAddonAvailable => Text(Strings.ModLists_PlaceFikaAddonFormat, finding.Addon?.Name ?? string.Empty),
        PlacementRule.AddonWiderThanParent => Text(
            finding.ParentLink == AddonParentLink.Hard ? Strings.ModLists_PlaceAddonWiderFormat : Strings.ModLists_PlaceAddonWiderMaybeFormat,
            NameOf(finding.Other), Machines(Added(finding))),
        PlacementRule.AddonLoadsAlone => Text(Strings.ModLists_PlaceAddonAloneFormat, NameOf(finding.Other), Machines(Added(finding))),
        PlacementRule.DependencyUncovered => Text(Strings.ModLists_PlaceDependencyFormat, NameOf(finding.Other), Machines(Added(finding))),
        PlacementRule.LibraryCanLeaveHeadless => Strings.ModLists_PlaceLibrary,
        PlacementRule.NothingToLoad => finding.SuggestedScope == ModListEntryScope.Server
            ? Text(Strings.ModLists_PlaceServerOnlyFormat, Machines(finding.Entry.EffectiveScope & ~ModListEntryScope.Server))
            : Strings.ModLists_PlaceNothingToLoad,
        PlacementRule.BotsCarryItems => Strings.ModLists_PlaceBotsItems,
        PlacementRule.AdviceNamesEntry => Text(Strings.ModLists_PlaceAdviceNamesFormat, NameOf(finding.Other), finding.Quote ?? string.Empty),
        PlacementRule.OnlyTheSync => Text(Strings.ModLists_PlaceOnlySyncFormat, NameOf(finding.Other)),
        _ => string.Empty,
    };

    // The machines a suggested scope adds to the entry's own.
    private static ModListEntryScope Added(PlacementFinding finding) =>
        (finding.SuggestedScope ?? finding.Entry.EffectiveScope) & ~finding.Entry.EffectiveScope;

    private static string Machines(ModListEntryScope scope)
    {
        var names = new List<string>();
        if (scope.HasFlag(ModListEntryScope.Server)) names.Add(Strings.ModLists_PlaceMachineServer);
        if (scope.HasFlag(ModListEntryScope.Client)) names.Add(Strings.ModLists_PlaceMachinePlayers);
        if (scope.HasFlag(ModListEntryScope.Headless)) names.Add(Strings.ModLists_PlaceMachineHeadless);
        return TextLists.Join(names);
    }

    private string NameOf(ModListEntry? entry) => entry is null
        ? string.Empty
        : entry.ModId is { } id && _titles.TryGetValue((id, entry.IsAddon), out var title) ? title : entry.Name;

    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void TakePlacement(ModListEntryRowViewModel? row)
    {
        if (Selected is not { } list || row?.Placement is not { } finding) return;

        if (finding is { Rule: PlacementRule.SyncAddonAvailable, Addon: { } addon, AddonScope: { } scope })
        {
            if (Entries.Any(e => e.Entry is { IsAddon: true } added && added.ModId == addon.Id)) return;

            Entries.Add(Row(ModListEntries.WithScope(
                ModListEntries.ForCatalogMod(addon.Id, addon.Name ?? addon.Id.ToString(), isAddon: true), scope)));
            UnsavedCount++;
            Notify();
            StatusMessage = Strings.ModLists_Added(1, 1, list.Name);
            UpdatePlacement();
        }
        else if (finding.SuggestedScope is { } suggested)
        {
            SetScope(row, suggested);
        }
    }

    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void TakePlacementAlternative(ModListEntryRowViewModel? row)
    {
        if (row?.Placement is not { AlternativeScope: { } scope, Other: { } other }) return;
        if (Entries.FirstOrDefault(e => ReferenceEquals(e.Entry, other)) is not { } target) return;

        SetScope(target, scope);
    }

    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void KeepPlacement(ModListEntryRowViewModel? row)
    {
        if (Selected is not { } list || row?.Placement is not { } finding) return;

        var key = HeadlessAdviceDismissals.Key(list.Id, finding.Entry, finding.Rule);
        _kept.Keep(key);
        _keptKeys.Add(key);

        UpdatePlacement();
        StatusMessage = Text(Strings.ModLists_PlaceKeptFormat, row.Name);
    }
}
