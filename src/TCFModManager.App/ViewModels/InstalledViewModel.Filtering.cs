using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    private void ApplyFilter()
    {
        var query = SearchText.Trim();

        // A leading "@" switches the search text to matching against the matched catalog listing's
        // author instead of name - e.g. "@Acidphantasm".
        var authorQuery = query.StartsWith('@') ? query[1..].Trim() : null;

        var recentSince = DateTimeOffset.Now.AddDays(-RecentDays);

        var installedMods = _all.Where(c => c is { IsAddon: false, ModId: not null }).Select(c => c.ModId!.Value).ToHashSet();
        var parentsWithAddons = _all
            .Where(c => c is { IsAddon: true, ParentModId: { } parent } && installedMods.Contains(parent))
            .Select(c => c.ParentModId!.Value)
            .ToHashSet();

        var matched = _all
            .Where(m => SelectedUpdateFilter.Value switch
            {
                UpdateFilter.NeedsUpdate => m.UpdateAvailable == true,
                UpdateFilter.UpToDate => m.UpdateAvailable == false,
                UpdateFilter.NotFound => m.MatchedModName is null,
                _ => true, // All - no restriction
            })
            .Where(m => SelectedEnabledFilter.Value switch
            {
                EnabledFilter.EnabledOnly => !m.IsDisabled,
                EnabledFilter.DisabledOnly => m.IsDisabled,
                _ => true,
            })
            .Where(m => SelectedGroupFilter.AllGroups
                || (SelectedGroupFilter.GroupId is { } groupId ? m.GroupId == groupId : !m.IsGrouped))
            .Where(m => authorQuery is not null
                ? MatchesAuthor(m, authorQuery)
                : query.Length == 0 || Matches(m.DisplayTitle, query) || Matches(m.Name, query))
            .Where(m => SelectedCategory.Title is not { } category
                || string.Equals(m.CategoryTag, category, StringComparison.OrdinalIgnoreCase))
            .Where(m => !IsOn(ModAttributeFilter.FikaCompatible) || m.IsFikaCompatible)
            .Where(m => !IsOn(ModAttributeFilter.HideAds) || !m.ContainsAds)
            .Where(m => !IsOn(ModAttributeFilter.HasDependencies) || m.HasDependencies)
            .Where(m => !IsOn(ModAttributeFilter.HasAddons) || m.HasAddons)
            .Where(m => !IsOn(ModAttributeFilter.DownloadedNotConfirmed) || m.HasPendingDownload)
            .Where(m => !IsOn(ModAttributeFilter.HasConflicts) || m.HasConflicts)
            .Where(m => !IsOn(ModAttributeFilter.NotForThisSpt) || m.IsNotForThisSpt)
            .Where(m => !IsOn(ModAttributeFilter.InstalledRecently)
                || (m.InstalledAt is { } at && at >= recentSince))
            .Where(m => !IsOn(ModAttributeFilter.HasInstalledAddons)
                || (FamilyOf(m) is { } family && parentsWithAddons.Contains(family)))
            .Where(m => _family is not { } focus || FamilyOf(m) == focus.ModId);

        _filtered = SortMods(matched, SelectedSortOption.Value).ToList();

        // Every view now disagrees with _filtered, including the two that aren't on screen - they
        // catch up when switched to.
        _cardsDirty = _listDirty = _sectionsDirty = true;

        TotalPages = IsEndless ? 1 : Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
        _endlessShown = EndlessStep;

        // The button names the number it would select, so narrowing the filter has to re-label it -
        // and re-decide whether everything it now matches is already ticked.
        OnPropertyChanged(nameof(SelectAllLabel));
        OnPropertyChanged(nameof(AllSelected));

        // Expand/collapse all are disabled when the filters match nothing, and a different set of
        // mods can be all-open or all-closed when the previous one was neither.
        ExpandAllCommand.NotifyCanExecuteChanged();
        CollapseAllCommand.NotifyCanExecuteChanged();
        NotifyExpandedState();

        // Keeps the status line up to date as soon as a filter/search control changes; skipped when
        // _all is empty since ScanAsync's own "No mods found under ..." message is more useful there.
        if (_all.Count > 0) StatusMessage = DescribeCounts();
    }

    // How many mods are shown out of how many are installed, plus how many of them are disabled.
    private string DescribeCounts()
    {
        var shown = _filtered.Count == _all.Count
            ? Strings.Installed_CountFound(_all.Count)
            : Strings.Installed_CountShown(_filtered.Count, _filtered.Count, _all.Count);

        var disabled = _all.Count(m => m.IsDisabled);
        if (disabled == 0) return shown;

        return Sentences(
            shown,
            Strings.Installed_CountDisabled(disabled));
    }

    private static IEnumerable<InstalledModCardViewModel> SortMods(IEnumerable<InstalledModCardViewModel> mods, ModSortOption sort) =>
        sort switch
        {
            ModSortOption.NameAscending => mods.OrderBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.NameDescending => mods.OrderByDescending(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            // No-author mods sort last in both directions rather than clumping at whichever end an
            // empty/null string would fall on.
            ModSortOption.AuthorAscending => mods
                .OrderBy(m => m.Author is null)
                .ThenBy(m => m.Author, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.AuthorDescending => mods
                .OrderBy(m => m.Author is null)
                .ThenByDescending(m => m.Author, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            // No known install date sorts last in both directions, same as a missing author.
            ModSortOption.InstalledNewest => mods
                .OrderBy(m => m.InstalledAt is null)
                .ThenByDescending(m => m.InstalledAt)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.InstalledOldest => mods
                .OrderBy(m => m.InstalledAt is null)
                .ThenBy(m => m.InstalledAt)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            // Ungrouped mods sort last in both directions, same treatment as a missing author.
            ModSortOption.GroupAscending => mods
                .OrderBy(m => m.GroupName is null)
                .ThenBy(m => m.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.GroupDescending => mods
                .OrderBy(m => m.GroupName is null)
                .ThenByDescending(m => m.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            _ => mods,
        };

    private static bool Matches(string? haystack, string needle) =>
        haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>Matches a "@name" query against the matched catalog listing's author. An empty query matches
    /// everything, same as an empty plain-text query.</summary>
    private static bool MatchesAuthor(InstalledModCardViewModel mod, string authorQuery) =>
        authorQuery.Length == 0 || Matches(mod.Author, authorQuery);
}
