namespace TCFModManager.App.ViewModels;

//
// The sections of each page's Filters panel, in panel order (OPEN-24). The order here is the order
// pinned sections appear in under the search bar (R5), so keep it matching the panel. Stored in
// settings.json by name, so renaming a member unpins it for anyone who had it pinned.
//
public enum BrowseFilterSection
{
    SptVersion,
    SearchIn,
    Show,
    Category,
    Featured,
    Published,
    Updated,
    Sort,
    PageSize,
}

public enum InstalledFilterSection
{
    UpdateStatus,
    Enabled,
    Show,
    Category,
    Group,
    Sort,
    GroupSort,
    PageSize,
}
