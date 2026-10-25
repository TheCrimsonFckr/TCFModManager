using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

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

    // Collapsed until opened, every search (Chris, 2026-10-24).
    [ObservableProperty]
    private bool _addonMatchesExpanded;

    // The bar can be turned off altogether; remembered between sessions, and not a filter, so Clear
    // filters leaves it alone. Matching addons still bring their mods into the results.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddonMatches))]
    private bool _showAddonMatches;

    partial void OnShowAddonMatchesChanged(bool value)
    {
        var settings = new SettingsService();
        var current = settings.Load();
        current.ShowAddonMatches = value;
        settings.Save(current);
    }

    private int _addonMatchCount;

    public bool HasAddonMatches => ShowAddonMatches && _addonMatchCount > 0;

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

    // D3 (Chris, 2026-10-24): the addon on its own, from the cached listing - no fetch, no mod
    // dialog to draw. D6: a detached addon's own sp-mod page.
    [RelayCommand]
    private void OpenAddonMatch(AddonSearchRowViewModel? row)
    {
        if (row is null) return;

        if (row.Parent is { } parent)
        {
            AppServices.ModDetailsOverlay.ShowAddon(row.Addon, parent, FindInstalledMatch(parent)?.InstalledVersion);
            return;
        }

        if (!string.IsNullOrWhiteSpace(row.Addon.DetailUrl)) MarkupActions.OpenInBrowser(row.Addon.DetailUrl);
    }
}
