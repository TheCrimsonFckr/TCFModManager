using CommunityToolkit.Mvvm.ComponentModel;

namespace TCFModManager.App.ViewModels;

//
// The tick-box filters that describe a mod itself rather than its relationship to this install.
// Shared by Browse and Installed so the same five options mean the same thing on both pages.
//
public enum ModAttributeFilter
{
    // Only mods flagged Fika compatible.
    FikaCompatible,

    // Mods flagged as containing ads are hidden.
    HideAds,

    // Mods flagged as containing AI-generated content are hidden.
    HideAiContent,

    // Only mods that pull in other mods.
    HasDependencies,

    // Only mods that have addons published for them.
    HasAddons,
}

//
// One tickable line in an attribute filter dropdown.
//
// Three of these hide things and two of them require things, which reads oddly as a list until you
// notice every one of them narrows what you see - that is the whole contract of the dropdown, and
// why "Hide ads" sits happily beside "Has addons".
//
public partial class ModAttributeOption(ModAttributeFilter value, string label, string? toolTip = null) : ObservableObject
{
    public ModAttributeFilter Value { get; } = value;

    public string Label { get; } = label;

    // Null for the options that need no explaining.
    public string? ToolTip { get; } = toolTip;

    [ObservableProperty]
    private bool _isSelected;
}

//
// One entry in a Category dropdown. Categories come from the cached catalog rather than a fixed
// list, so the app never shows a category The Forge has stopped using or misses one it has added.
//
public sealed record CategoryFilterItem(string Label, string? Title)
{
    // No restriction.
    public static CategoryFilterItem All { get; } = new("All categories", null);

    public bool SameAs(CategoryFilterItem? other) =>
        other is not null && string.Equals(other.Title, Title, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Label;
}
