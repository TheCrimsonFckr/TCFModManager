namespace TCFModManager.App.ViewModels;

//
// The sections of each page's Filters panel, in panel order (OPEN-24). The order here is the order
// pinned sections appear in under the search bar (R5), so keep it matching the panel. Stored in
// settings.json by name, so renaming a member unpins it for anyone who had it pinned.
//
public enum BrowseFilterSection
{
    // OPEN-25: first in the panel (R1). Named to match Core's FilterPresetMatch.PresetsSection.
    Presets,
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
    // OPEN-25: first in the panel (R1). Named to match Core's FilterPresetMatch.PresetsSection.
    Presets,
    UpdateStatus,
    Enabled,
    Show,
    Category,
    Group,
    Sort,
    GroupSort,
    PageSize,
}
