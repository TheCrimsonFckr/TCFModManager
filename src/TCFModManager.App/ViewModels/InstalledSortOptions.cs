namespace TCFModManager.App.ViewModels;

// Installed page's "Sort by" dropdown - orders both the flat grid and each group's mod list in
// group view.
public enum ModSortOption
{
    NameAscending,
    NameDescending,
    AuthorAscending,
    AuthorDescending,

    // Newest InstalledAt first; a mod with no determinable install date sorts last.
    RecentlyInstalled,
}

// One entry in the "Sort by" dropdown. Overrides ToString() so the label shows instead of the enum name.
public sealed record ModSortItem(string Label, ModSortOption Value)
{
    public override string ToString() => Label;
}

// Group view's "Sort groups" dropdown - orders the section list itself. Manual is each group's own
// up/down-reorderable ModGroup.SortOrder; the header's move-up/down buttons only make sense - and
// only show (see InstalledViewModel.CanReorderGroups) - while this is selected, since an
// alphabetical sort would just override anything they did.
public enum GroupSortOption
{
    Manual,
    NameAscending,
    NameDescending,
}

public sealed record GroupSortItem(string Label, GroupSortOption Value)
{
    public override string ToString() => Label;
}
