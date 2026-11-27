using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

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
        if (family is not null) _neededByFocus = null;
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    private (string Name, HashSet<string> Keys)? _neededByFocus;

    [RelayCommand]
    private void ShowNeededBy(InstalledModCardViewModel? mod)
    {
        if (mod is not { IsNeeded: true }) return;
        var keys = mod.NeededBy.Append(mod).Select(c => ModGroupStore.KeyFor(c.Name)).ToHashSet();
        SetNeededBy((mod.DisplayTitle, keys));
    }

    private bool InNeededByFocus(InstalledModCardViewModel mod) =>
        _neededByFocus is not { } focus || focus.Keys.Contains(ModGroupStore.KeyFor(mod.Name));

    private void SetNeededBy((string Name, HashSet<string> Keys)? focus)
    {
        _neededByFocus = focus;
        if (focus is not null) _family = null;
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }
}
