using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    public List<UpdateFilterItem> UpdateFilterOptions { get; } =
    [
        new(nameof(Strings.Filter_UpdateAny), UpdateFilter.All),
        new(nameof(Strings.Filter_UpdateNeeded), UpdateFilter.NeedsUpdate),
        new(nameof(Strings.Filter_UpdateUpToDate), UpdateFilter.UpToDate),
        new(nameof(Strings.Filter_UpdateNotFound), UpdateFilter.NotFound),
    ];

    // How far back the Installed in the last 7 days tick box looks.
    private const int RecentDays = 7;

    [ObservableProperty]
    private UpdateFilterItem _selectedUpdateFilter;

    public List<EnabledFilterItem> EnabledFilterOptions { get; } =
    [
        new(nameof(Strings.Filter_EnabledAll), EnabledFilter.All),
        new(nameof(Strings.Filter_EnabledOnly), EnabledFilter.EnabledOnly),
        new(nameof(Strings.Filter_DisabledOnly), EnabledFilter.DisabledOnly),
    ];

    [ObservableProperty]
    private EnabledFilterItem _selectedEnabledFilter;

    // Rebuilt from the group store by RefreshGroups whenever groups change, so the dropdown always
    // lists exactly the groups that exist.
    public ObservableCollection<GroupFilterItem> GroupFilterOptions { get; } =
        [GroupFilterItem.All, GroupFilterItem.Ungrouped];

    [ObservableProperty]
    private GroupFilterItem _selectedGroupFilter;

    public List<ModSortItem> SortOptions { get; } =
    [
        // "By ..." rather than a bare "Name (A-Z)": with the inline "Sort by:" label gone, a
        // dropdown reading "Group (A-Z)" sitting next to the Group *filter* would be read as
        // another filter. The prefix is what makes these read as orderings on their own.
        new(nameof(Strings.Sort_NameAscending), ModSortOption.NameAscending),
        new(nameof(Strings.Sort_NameDescending), ModSortOption.NameDescending),
        new(nameof(Strings.Sort_AuthorAscending), ModSortOption.AuthorAscending),
        new(nameof(Strings.Sort_AuthorDescending), ModSortOption.AuthorDescending),
        new(nameof(Strings.Sort_GroupAscending), ModSortOption.GroupAscending),
        new(nameof(Strings.Sort_GroupDescending), ModSortOption.GroupDescending),
        new(nameof(Strings.Sort_InstalledNewest), ModSortOption.InstalledNewest),
        new(nameof(Strings.Sort_InstalledOldest), ModSortOption.InstalledOldest),
    ];

    [ObservableProperty]
    private ModSortItem _selectedSortOption;

    // Set while ClearFilters is resetting several properties at once, so each individual
    // OnXxxChanged below doesn't run its own AutoApplyFilter.
    private bool _suppressAutoApplyFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    //
    // The same five tick boxes Browse carries, in one dropdown rather than three toggle switches.
    // "Has dependencies" is a stronger answer here than on Browse: it comes from the dependency
    // graph built off the scan, which reads BepInEx's own metadata, so it covers everything
    // installed rather than whatever has been looked up so far.
    //
    // Plus one of Installed's own: Monitor mode downloads that are on disk but unconfirmed (R8).
    public ObservableCollection<ModAttributeOption> AttributeOptions { get; } =
    [
        .. ModAttributeOption.Standard(
            nameof(Strings.Filter_HasDependenciesInstalledToolTip),
            nameof(Strings.Filter_NotFikaCompatibleInstalledToolTip),
            nameof(Strings.Filter_FikaUnknownInstalledToolTip)),
        new(ModAttributeFilter.DownloadedNotConfirmed,
            nameof(Strings.Filter_DownloadedNotConfirmed),
            nameof(Strings.Filter_DownloadedNotConfirmedToolTip)),
        new(ModAttributeFilter.HasConflicts,
            nameof(Strings.Filter_HasConflicts),
            nameof(Strings.Filter_HasConflictsToolTip)),
        new(ModAttributeFilter.NotForThisSpt,
            nameof(Strings.Filter_NotForThisSpt),
            nameof(Strings.Filter_NotForThisSptToolTip)),
        new(ModAttributeFilter.InstalledRecently, nameof(Strings.Filter_InstalledRecently)),
        new(ModAttributeFilter.HasInstalledAddons,
            nameof(Strings.Filter_HasInstalledAddons),
            nameof(Strings.Filter_HasInstalledAddonsToolTip)),
    ];

    //
    // Downloads the confirm prompt has already put to the user this session and been told "not
    // now". A scan runs after every remove, enable and dialog close, so without this the same
    // question would come back every few seconds.
    //
    private readonly HashSet<(int ModId, bool IsAddon, string Version)> _deferredDownloads = [];

    //
    // The Filters and view options button (everything behind it in the side panel), with a count when
    // any of its filters narrow the list, so nothing hidden in the panel goes unnoticed. Sort and page
    // size don't narrow anything and aren't counted. Computed, so a language change rewrites it.
    //
    public string MoreFiltersLabel => MoreFiltersSet is var set && set > 0
        ? Text(Strings.Filter_MoreCountFormat, set)
        : Strings.Filter_More;

    private int MoreFiltersSet =>
        AttributeOptions.Count(o => o.IsSelected)
        + (SelectedUpdateFilter.Value != UpdateFilter.All ? 1 : 0)
        + (SelectedEnabledFilter.Value != EnabledFilter.All ? 1 : 0)
        + (SelectedCategory.Title is not null ? 1 : 0)
        + (SelectedGroupFilter.AllGroups ? 0 : 1)
        + (_family is null ? 0 : 1);

    //
    // The filters narrowing the list, as pills above the results - see ActiveFilterPill. Rebuilt
    // whenever the count on the Filters and view options button changes, and on a language change.
    //
    public ObservableCollection<ActiveFilterPill> ActiveFilters { get; } = [];

    public bool HasActiveFilters => ActiveFilters.Count > 0;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is null or "" or nameof(MoreFiltersLabel)) RebuildActiveFilters();
        Presets?.Refresh();
        FilterPins?.RefreshLabels();
    }

    //
    // The text on a pinned section's dropdown under the search bar (OPEN-24): the section and what it
    // is set to, unset values included - "Update status: Any update status", "Group: All groups".
    // Null while the constructor is still filling the filters in.
    //
    private string? PinnedLabel(InstalledFilterSection section)
    {
        if (SelectedUpdateFilter is null || SelectedEnabledFilter is null || SelectedCategory is null
            || SelectedGroupFilter is null || SelectedSortOption is null || SelectedGroupSortOption is null) return null;

        return section switch
        {
            InstalledFilterSection.Presets => Pill(Strings.Filter_SectionPresets, Presets?.ActiveName ?? Strings.Filter_PresetsNone),
            InstalledFilterSection.UpdateStatus => Pill(Strings.Filter_SectionUpdateStatus, SelectedUpdateFilter),
            InstalledFilterSection.Enabled => Pill(Strings.Filter_SectionEnabled, SelectedEnabledFilter),
            InstalledFilterSection.Show => Pill(Strings.Filter_ShowHeader, AttributeOptions.Where(o => o.IsSelected).ToList() switch
            {
                [] => Strings.Filter_ShowNone,
                [var one] => one.Label,
                var many => LocalizationService.Text(Strings.Filter_SelectedCountFormat, many.Count),
            }),
            InstalledFilterSection.Category => Pill(Strings.Filter_SectionCategory, SelectedCategory),
            InstalledFilterSection.Group => Pill(Strings.Filter_SectionGroup, SelectedGroupFilter),
            InstalledFilterSection.Sort => Pill(Strings.Filter_SectionSort, SelectedSortOption),
            InstalledFilterSection.GroupSort => Pill(Strings.Filter_SectionGroupSort, SelectedGroupSortOption),
            InstalledFilterSection.PageSize => Converters.PageSizeLabelConverter.Label(PageSize),
            _ => null,
        };
    }

    // The same rule the panel applies to these two sections.
    private bool PinnedShown(InstalledFilterSection section) => section switch
    {
        InstalledFilterSection.GroupSort => ShowGroups,
        InstalledFilterSection.PageSize => ShowCards,
        _ => true,
    };

    private static string Pill(string section, object? value) =>
        LocalizationService.Text(Strings.Filter_PillFormat, section, value?.ToString() ?? string.Empty);

    private void RebuildActiveFilters()
    {
        // The generated properties raise during construction, before every option is in place.
        if (SelectedUpdateFilter is null || SelectedEnabledFilter is null || SelectedCategory is null || SelectedGroupFilter is null)
            return;

        ActiveFilters.Clear();

        if (SelectedUpdateFilter.Value != UpdateFilter.All)
            ActiveFilters.Add(new(Pill(Strings.Filter_SectionUpdateStatus, SelectedUpdateFilter),
                () => SelectedUpdateFilter = UpdateFilterOptions.First(o => o.Value == UpdateFilter.All)));

        if (SelectedEnabledFilter.Value != EnabledFilter.All)
            ActiveFilters.Add(new(Pill(Strings.Filter_SectionEnabled, SelectedEnabledFilter),
                () => SelectedEnabledFilter = EnabledFilterOptions.First(o => o.Value == EnabledFilter.All)));

        foreach (var option in AttributeOptions.Where(o => o.IsSelected))
            ActiveFilters.Add(new(Pill(Strings.Filter_ShowHeader, option.Label), () => option.IsSelected = false));

        if (SelectedCategory.Title is not null)
            ActiveFilters.Add(new(Pill(Strings.Filter_SectionCategory, SelectedCategory),
                () => SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Title is null) ?? CategoryFilterItem.All));

        if (!SelectedGroupFilter.AllGroups)
            ActiveFilters.Add(new(Pill(Strings.Filter_SectionGroup, SelectedGroupFilter),
                () => SelectedGroupFilter = GroupFilterOptions.FirstOrDefault(o => o.AllGroups) ?? GroupFilterItem.All));

        if (_family is { } family)
            ActiveFilters.Add(new(Text(Strings.Installed_FamilyPillFormat, family.Name), () => SetFamily(null)));

        OnPropertyChanged(nameof(HasActiveFilters));
    }

    /// <summary>The Category dropdown's entries, rebuilt after each scan from the categories actually present in the install.</summary>
    public ObservableCollection<CategoryFilterItem> CategoryOptions { get; } = [CategoryFilterItem.All];

    [ObservableProperty]
    private CategoryFilterItem _selectedCategory = CategoryFilterItem.All;

    private bool IsOn(ModAttributeFilter filter) =>
        AttributeOptions.Any(o => o.Value == filter && o.IsSelected);

    private UpdateFilterItem UpdatesAvailableFilter() =>
        UpdateFilterOptions.First(o => o.Value == UpdateFilter.NeedsUpdate);

    partial void OnSelectedCategoryChanged(CategoryFilterItem value)
    {
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    // Ticking a box in the Filters panel changes the count on its button.
    private void UpdateAttributeFilterSummary() => OnPropertyChanged(nameof(MoreFiltersLabel));

    //
    // Rebuilt from what is actually installed rather than from the whole catalog: filtering your
    // own mods by a category none of them are in would only ever produce an empty page.
    //
    private void RebuildCategoryOptions()
    {
        // The saved default on the first build, and whatever is currently chosen on every rebuild
        // after it - a rescan must not quietly reset a filter the user has since changed.
        var previous = _categoryDefaultApplied ? SelectedCategory : DefaultCategory();
        _categoryDefaultApplied = true;

        CategoryOptions.Clear();
        CategoryOptions.Add(CategoryFilterItem.All);

        var categories = _all
            .Select(c => c.CategoryTag)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories) CategoryOptions.Add(new CategoryFilterItem(category, category));

        // Keep the current choice if that category is still installed, rather than silently
        // resetting the page's filter under someone every time they come back to it.
        SelectedCategory = CategoryOptions.FirstOrDefault(c => c.SameAs(previous)) ?? CategoryOptions[0];
    }

    partial void OnSelectedUpdateFilterChanged(UpdateFilterItem value)
    {
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    partial void OnSelectedEnabledFilterChanged(EnabledFilterItem value)
    {
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    partial void OnSelectedGroupFilterChanged(GroupFilterItem value) 
    {
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    // Leaving selection mode drops the selection with it, so a stale tick can't be acted on later.
    partial void OnSelectionModeChanged(bool value)
    {
        if (!value) ClearSelection();
    }

    partial void OnSelectedSortOptionChanged(ModSortItem value) => AutoApplyFilter();

    partial void OnPageSizeChanged(int value)
    {
        OnPropertyChanged(nameof(IsEndless));
        OnPropertyChanged(nameof(ShowPager));
        AutoApplyFilter();
    }

    //
    // Deliberately not AutoApplyFilter: nothing about what is shown changes, only whether each row
    // draws its chips, so the three views don't need rebuilding.
    //
    partial void OnShowListBadgesChanged(bool value)
    {
        foreach (var card in _all) card.ShowBadges = value;

        var settings = new SettingsService();
        var current = settings.Load();
        current.ShowModListBadges = value;
        settings.Save(current);
    }

    partial void OnSearchTextChanged(string value) => AutoApplyFilter();


    partial void OnViewModeChanged(InstalledViewMode value)
    {
        // Multi-select works in all three views (OPEN-12 A1), so the ticks carry across a switch.

        // Deliberately not AutoApplyFilter: no filter, search or sort has changed, so re-running
        // ApplyFilter would produce the same _filtered it already holds. Refreshing the view being
        // switched to is all that's needed, and that does nothing at all when it is already current.
        // CurrentPage rather than the default 1 so switching away from Cards and back returns you to
        // the page you were on.
        RefreshActiveView(CurrentPage);

        // Cards and List hold their own expansion, so the same mods can be all-open in one and
        // all-closed in the other - the two buttons have to re-read whichever is now on screen.
        NotifyExpandedState();
    }

    partial void OnSelectedGroupSortOptionChanged(GroupSortItem value)
    {
        OnPropertyChanged(nameof(CanReorderGroups));
        RebuildSections();
    }

    /// <summary>Re-filters/re-sorts and refreshes whichever of the three views is active whenever a
    /// filter/search/sort control changes. Suppressed while ClearFilters resets several properties
    /// at once.</summary>
    private void AutoApplyFilter()
    {
        if (_suppressAutoApplyFilter) return;
        ApplyFilter();

        // CurrentPage, not 1. Narrowing a filter shrinks TotalPages and GoToPage clamps to it, so a
        // page that no longer exists still lands somewhere sensible - and nothing is left that can
        // silently put you back on page 1. Dropping the resetPage flag was not enough on its own:
        // this method runs whenever any filter/sort/search/page-size property changes, including
        // when one is reassigned in code, and its RefreshActiveView() was taking the default of 1.
        RefreshActiveView(CurrentPage);
    }
}
