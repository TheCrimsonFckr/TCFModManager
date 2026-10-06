using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public enum ModSortOrder
{
    Newest,
    LastUpdated,
    MostDownloaded,
    MostFavourited,
    MostEndorsed,
}

// One entry in Browse's Sort by dropdown. Overrides ToString() so the label shows instead of the
// enum name.
//
// Holds a KEY, not a label: the text is read on every get, so choosing a language relabels the
// dropdown in place. Rebuilding the list instead would replace the item instances and drop the
// SelectedItem binding, which resets the dropdown to its first entry every time - see D9.
//
public sealed class SortOptionItem(string key, ModSortOrder value) : LocalizedViewModel
{
    public ModSortOrder Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

public enum FeaturedFilter
{
    // No restriction - featured and non-featured mods both show.
    Include,

    // Featured mods are hidden.
    Exclude,

    // Only featured mods show.
    Only,
}

// One entry in Browse's Featured dropdown.
public sealed class FeaturedFilterItem(string key, FeaturedFilter value) : LocalizedViewModel
{
    public FeaturedFilter Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

// One entry in Browse's Published and Updated sections (OPEN-12 F19) - a preset, or Custom.
public sealed class DateRangeItem(string key, DateRangePreset value) : LocalizedViewModel
{
    public DateRangePreset Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

// What Browse's search box looks at (OPEN-12 F19, R21). @name always searches authors whatever this
// is set to. No description search: descriptions are not in the cached catalog.
public enum SearchScope
{
    // Name, slug and teaser - what the search box has always looked at.
    TitleAndTeaser,

    // Name and slug only.
    TitleOnly,
}

public sealed class SearchScopeItem(string key, SearchScope value) : LocalizedViewModel
{
    public SearchScope Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}
