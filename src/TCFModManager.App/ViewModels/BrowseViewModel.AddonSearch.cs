using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel
{
    //
    // OPEN-36 D2: the addons the search matched, above the grid. An addon whose mod the filters hide
    // is left out (D5); a detached addon has no mod to hide it and is listed (D6).
    //
    public ObservableCollection<AddonSearchRowViewModel> AddonMatches { get; } = [];

    [ObservableProperty]
    private bool _addonMatchesExpanded = true;

    public bool HasAddonMatches => AddonMatches.Count > 0;

    public string AddonMatchesHeading => Text(Strings.Browse_AddonMatchesHeadingFormat, AddonMatches.Count);

    private void RebuildAddonMatches(IReadOnlyList<Addon> matches)
    {
        var shown = _filtered.ToDictionary(m => m.Id);

        AddonMatches.Clear();
        foreach (var addon in matches)
        {
            if (addon.IsDetached == true || addon.ModId is not { } parentId) AddonMatches.Add(new AddonSearchRowViewModel(addon, null));
            else if (shown.TryGetValue(parentId, out var parent)) AddonMatches.Add(new AddonSearchRowViewModel(addon, parent));
        }

        OnPropertyChanged(nameof(HasAddonMatches));
        OnPropertyChanged(nameof(AddonMatchesHeading));
    }

    // D3: the mod's details opened on this addon; D6: a detached addon's own sp-mod page.
    [RelayCommand]
    private async Task OpenAddonMatchAsync(AddonSearchRowViewModel? row)
    {
        if (row is null) return;

        if (row.Parent is { } parent)
        {
            await LoadDetailsAsync(parent, row.Addon.Id);
            return;
        }

        if (!string.IsNullOrWhiteSpace(row.Addon.DetailUrl)) MarkupActions.OpenInBrowser(row.Addon.DetailUrl);
    }
}
