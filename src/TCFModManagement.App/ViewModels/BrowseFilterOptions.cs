namespace TCFModManager.App.ViewModels;

public enum ModSortOrder
{
    Newest,
    LastUpdated,
    MostDownloaded,
    MostFavourited,
}

// One entry in Browse's Sort by dropdown. Overrides ToString() so the label shows instead of the enum name.
public sealed record SortOptionItem(string Label, ModSortOrder Value)
{
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
public sealed record FeaturedFilterItem(string Label, FeaturedFilter Value)
{
    public override string ToString() => Label;
}
