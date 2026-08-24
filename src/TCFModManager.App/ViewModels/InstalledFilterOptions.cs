namespace TCFModManager.App.ViewModels;

public enum UpdateFilter
{
    // No restriction - every installed mod shows regardless of update status.
    All,

    // Only mods with a confirmed newer version on sp-mod.com show (UpdateAvailable == true).
    NeedsUpdate,

    // Only mods confirmed up to date show (UpdateAvailable == false).
    UpToDate,

    // Only mods with no sp-mod.com match at all show (MatchedModName == null).
    NotFound,
}

// One entry in Installed's Update status dropdown. Overrides ToString() so the label shows instead of the enum name.
public sealed record UpdateFilterItem(string Label, UpdateFilter Value)
{
    public override string ToString() => Label;
}

public enum EnabledFilter
{
    // No restriction - enabled and disabled mods both show.
    All,

    // Only mods SPT actually loads.
    EnabledOnly,

    // Only mods moved out into a ".disabled" container.
    DisabledOnly,
}

// One entry in Installed's Enabled/Disabled dropdown.
public sealed record EnabledFilterItem(string Label, EnabledFilter Value)
{
    public override string ToString() => Label;
}
