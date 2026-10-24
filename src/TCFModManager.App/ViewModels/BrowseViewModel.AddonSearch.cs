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
    // At most this many rows, so a short search that matches most addons stays quick to draw.
    private const int AddonMatchesShownMax = 24;

    [ObservableProperty]
    private IReadOnlyList<AddonSearchRowViewModel> _addonMatches = [];

    [ObservableProperty]
    private bool _addonMatchesExpanded = true;

    private int _addonMatchCount;

    public bool HasAddonMatches => _addonMatchCount > 0;

    public string AddonMatchesHeading => Text(Strings.Browse_AddonMatchesHeadingFormat, _addonMatchCount);

    public string? AddonMatchesMore => _addonMatchCount > AddonMatches.Count
        ? Text(Strings.Browse_AddonMatchesMoreFormat, _addonMatchCount - AddonMatches.Count)
        : null;

    private void RebuildAddonMatches(IReadOnlyList<Addon> matches)
    {
        var shown = _filtered.ToDictionary(m => m.Id);

        var rows = new List<AddonSearchRowViewModel>();
        foreach (var addon in matches)
        {
            if (addon.IsDetached == true || addon.ModId is not { } parentId) rows.Add(new AddonSearchRowViewModel(addon, null));
            else if (shown.TryGetValue(parentId, out var parent)) rows.Add(new AddonSearchRowViewModel(addon, parent));
        }

        var visible = rows.Take(AddonMatchesShownMax).ToList();
        var unchanged = rows.Count == _addonMatchCount
            && visible.Count == AddonMatches.Count
            && visible.Zip(AddonMatches).All(p => p.First.Addon.Id == p.Second.Addon.Id && p.First.Parent?.Id == p.Second.Parent?.Id);
        if (unchanged) return;

        _addonMatchCount = rows.Count;
        AddonMatches = visible;

        OnPropertyChanged(nameof(HasAddonMatches));
        OnPropertyChanged(nameof(AddonMatchesHeading));
        OnPropertyChanged(nameof(AddonMatchesMore));
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
