using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Whether a preset is the page as it is right now (OPEN-25 §2.4) - which one the panel ticks and a
// pinned Filter presets dropdown names. Nothing about the active preset is stored; the page captures
// itself the way saving a preset does and asks this.
//
// The comparison is on meaning, not on the raw records: names ignore case, ticked attributes, SPT
// lines and pinned sections are sets, an empty category is all categories, a custom range compares
// by day, and a preset that leaves search or pins alone (null - a hand-edited file) matches whatever
// the page has there. Whether the presets section itself is pinned never counts (R6).
//
public static class FilterPresetMatch
{
    /// <summary>The first preset that is the page as captured, or null when none is.</summary>
    public static T? Active<T>(IEnumerable<T> presets, T page) where T : class, IFilterPreset =>
        presets.FirstOrDefault(p => Matches(p, page));

    public static bool Matches<T>(T preset, T page) where T : class, IFilterPreset => (preset, page) switch
    {
        (BrowseFilterPreset b, BrowseFilterPreset p) => Same(b, p),
        (InstalledFilterPreset i, InstalledFilterPreset p) => Same(i, p),
        _ => false,
    };

    public static bool Same(BrowseFilterPreset preset, BrowseFilterPreset page) =>
        Same(preset.Filters, page.Filters)
        && SameDays(preset, page)
        && SameExtras(preset, page);

    public static bool Same(InstalledFilterPreset preset, InstalledFilterPreset page) =>
        Same(preset.Filters, page.Filters)
        && SameExtras(preset, page);

    public static bool Same(BrowsePageDefaults a, BrowsePageDefaults b) =>
        Name(a.Sort, b.Sort)
        && Name(a.Featured, b.Featured)
        && Category(a.Category, b.Category)
        && a.PageSize == b.PageSize
        && Set(a.Attributes, b.Attributes)
        && SptLines(a.SptVersions, b.SptVersions)
        && Name(a.Published, b.Published)
        && Name(a.Updated, b.Updated)
        && Name(a.SearchScope, b.SearchScope);

    public static bool Same(InstalledPageDefaults a, InstalledPageDefaults b) =>
        Name(a.ViewMode, b.ViewMode)
        && Name(a.UpdateStatus, b.UpdateStatus)
        && Name(a.Enabled, b.Enabled)
        && Category(a.Category, b.Category)
        && Name(a.Group, b.Group)
        && Name(a.Sort, b.Sort)
        && Name(a.GroupSort, b.GroupSort)
        && a.PageSize == b.PageSize
        && Set(a.Attributes, b.Attributes);

    // The days only count when the range is Custom; a preset range like the last 30 days has none.
    private static bool SameDays(BrowseFilterPreset a, BrowseFilterPreset b) =>
        (!IsCustom(a.Filters.Published) || (Day(a.PublishedFrom, b.PublishedFrom) && Day(a.PublishedUntil, b.PublishedUntil)))
        && (!IsCustom(a.Filters.Updated) || (Day(a.UpdatedFrom, b.UpdatedFrom) && Day(a.UpdatedUntil, b.UpdatedUntil)));

    //
    // The Filter presets section's own name in the pinned list. Left out of the comparison: whether
    // the presets dropdown is pinned is not part of any preset (R6 - applying one never unpins it).
    //
    public const string PresetsSection = "Presets";

    private static bool SameExtras(IFilterPreset preset, IFilterPreset page) =>
        (preset.Search is null || string.Equals((preset.Search ?? "").Trim(), (page.Search ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
        && (preset.Pinned is null || Set(WithoutPresets(preset.Pinned), WithoutPresets(page.Pinned ?? [])));

    private static IEnumerable<string> WithoutPresets(IEnumerable<string> pinned) =>
        pinned.Where(n => !string.Equals(n.Trim(), PresetsSection, StringComparison.OrdinalIgnoreCase));

    private static bool IsCustom(string? range) => string.Equals(range, "Custom", StringComparison.OrdinalIgnoreCase);

    private static bool Day(DateTime? a, DateTime? b) => a?.Date == b?.Date;

    private static bool Name(string? a, string? b) =>
        string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

    private static bool Category(string? a, string? b) => Name(a, b);

    // Null is "follow the install", empty is "every SPT version" - different answers, kept apart.
    private static bool SptLines(List<string>? a, List<string>? b) =>
        a is null || b is null ? a is null && b is null : Set(a, b);

    private static bool Set(IEnumerable<string> a, IEnumerable<string> b) =>
        new HashSet<string>(a.Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase)
            .SetEquals(b.Select(x => x.Trim()));
}
