using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    //
    // A mod and its installed addons on their own (right-click "Show ... and its addons"), shown as a
    // pill above the results. Not a saved filter: Clear filters, a preset or the pill's x drops it.
    //
    private (int ModId, string Name)? _family;

    // The mod a card belongs with: its own id for a mod, its parent's for an addon.
    private static int? FamilyOf(InstalledModCardViewModel card) => card.IsAddon ? card.ParentModId : card.ModId;

    // The right-click entry's text, or null when the card has no family to show.
    public string? FamilyMenuLabel(InstalledModCardViewModel mod) =>
        FamilyParent(mod) is { } parent ? Text(Strings.Installed_ShowFamilyFormat, parent.Name) : null;

    private (int ModId, string Name)? FamilyParent(InstalledModCardViewModel mod)
    {
        if (FamilyOf(mod) is not { } id) return null;

        var parent = _all.FirstOrDefault(c => c is { IsAddon: false } && c.ModId == id);
        var hasAddons = _all.Any(c => c.IsAddon && c.ParentModId == id);

        if (mod.IsAddon) return (id, parent?.DisplayTitle ?? mod.ParentModName ?? mod.DisplayTitle);
        return hasAddons ? (id, mod.DisplayTitle) : null;
    }

    [RelayCommand]
    private void ShowFamily(InstalledModCardViewModel? mod)
    {
        if (mod is not null && FamilyParent(mod) is { } family) SetFamily(family);
    }

    private void SetFamily((int ModId, string Name)? family)
    {
        _family = family;
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }
}
