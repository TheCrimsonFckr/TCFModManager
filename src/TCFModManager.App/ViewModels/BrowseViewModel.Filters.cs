using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel
{
    // Set while ClearFilters is resetting several properties at once, so each individual
    // OnXxxChanged below doesn't run its own ApplyFilter.
    private bool _suppressAutoApplyFilter;

    //
    // Typing re-filters once it pauses rather than on every key: each pass rebuilds the page of
    // cards and the Addons strip, which a fast typist could otherwise queue up a dozen of.
    //
    private System.Windows.Threading.DispatcherTimer? _searchDelay;

    partial void OnSearchTextChanged(string value)
    {
        if (_searchDelay is null)
        {
            _searchDelay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _searchDelay.Tick += (_, _) =>
            {
                _searchDelay.Stop();
                AutoApplyFilter();
            };
        }

        _searchDelay.Stop();
        _searchDelay.Start();
    }

    partial void OnSelectedSortOptionChanged(SortOptionItem value) => AutoApplyFilter();

    partial void OnPageSizeChanged(int value) => AutoApplyFilter();

    partial void OnSelectedFeaturedFilterChanged(FeaturedFilterItem value) 
    {
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    partial void OnSelectedCategoryChanged(CategoryFilterItem value)
    {
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    partial void OnSelectedPublishedRangeChanged(DateRangeItem value)
    {
        OnPropertyChanged(nameof(IsPublishedCustom));
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    partial void OnPublishedFromChanged(DateTime? value) => DateChanged();

    partial void OnPublishedUntilChanged(DateTime? value) => DateChanged();

    partial void OnSelectedUpdatedRangeChanged(DateRangeItem value)
    {
        OnPropertyChanged(nameof(IsUpdatedCustom));
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    partial void OnUpdatedFromChanged(DateTime? value) => DateChanged();

    partial void OnUpdatedUntilChanged(DateTime? value) => DateChanged();

    partial void OnSelectedSearchScopeChanged(SearchScopeItem value)
    {
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    private void DateChanged()
    {
        OnPropertyChanged(nameof(MoreFiltersLabel));
        AutoApplyFilter();
    }

    private void AutoApplyFilter()
    {
        if (!_suppressAutoApplyFilter && HasLoadedResults) ApplyFilter();
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    public List<SortOptionItem> SortOptions { get; } =
    [
        // Read as orderings without the "Sort:" label that used to sit beside them.
        new(nameof(Strings.Sort_BrowseNewest), ModSortOrder.Newest),
        new(nameof(Strings.Sort_BrowseLastUpdated), ModSortOrder.LastUpdated),
        new(nameof(Strings.Sort_BrowseMostDownloaded), ModSortOrder.MostDownloaded),
        new(nameof(Strings.Sort_BrowseMostFavourited), ModSortOrder.MostFavourited),
        new(nameof(Strings.Sort_BrowseMostEndorsed), ModSortOrder.MostEndorsed),
    ];

    [ObservableProperty]
    private SortOptionItem _selectedSortOption;

    //
    // OPEN-12 F19: when a mod was first published, and when it last had a new version - a preset
    // counted back from today, or Custom with two days picked under it (R20). Both sections share the
    // one list of presets.
    //
    public List<DateRangeItem> DateRangeOptions { get; } =
    [
        new(nameof(Strings.Filter_DateAnyTime), DateRangePreset.AnyTime),
        new(nameof(Strings.Filter_DateLast7Days), DateRangePreset.Last7Days),
        new(nameof(Strings.Filter_DateLast30Days), DateRangePreset.Last30Days),
        new(nameof(Strings.Filter_DateLast90Days), DateRangePreset.Last90Days),
        new(nameof(Strings.Filter_DateLastYear), DateRangePreset.LastYear),
        new(nameof(Strings.Filter_DateCustom), DateRangePreset.Custom),
    ];

    [ObservableProperty]
    private DateRangeItem _selectedPublishedRange;

    [ObservableProperty]
    private DateTime? _publishedFrom;

    [ObservableProperty]
    private DateTime? _publishedUntil;

    public bool IsPublishedCustom => SelectedPublishedRange?.Value == DateRangePreset.Custom;

    [ObservableProperty]
    private DateRangeItem _selectedUpdatedRange;

    [ObservableProperty]
    private DateTime? _updatedFrom;

    [ObservableProperty]
    private DateTime? _updatedUntil;

    public bool IsUpdatedCustom => SelectedUpdatedRange?.Value == DateRangePreset.Custom;

    public List<SearchScopeItem> SearchScopeOptions { get; } =
    [
        new(nameof(Strings.Filter_SearchTitleAndTeaser), SearchScope.TitleAndTeaser),
        new(nameof(Strings.Filter_SearchTitleOnly), SearchScope.TitleOnly),
    ];

    [ObservableProperty]
    private SearchScopeItem _selectedSearchScope;

    public List<FeaturedFilterItem> FeaturedFilterOptions { get; } =
    [
        // The worst case for dropping a label: "Include" / "Exclude" / "Only" say nothing at all
        // on their own about what is being included.
        new(nameof(Strings.Filter_FeaturedIncluded), FeaturedFilter.Include),
        new(nameof(Strings.Filter_FeaturedExcluded), FeaturedFilter.Exclude),
        new(nameof(Strings.Filter_FeaturedOnly), FeaturedFilter.Only),
    ];

    [ObservableProperty]
    private FeaturedFilterItem _selectedFeaturedFilter;

    //
    // The tick-box filters that describe the mod itself, in one dropdown rather than three toggle
    // switches strung across the top of the page. Every one of them narrows the result set.
    //
    public ObservableCollection<ModAttributeOption> AttributeOptions { get; } =
    [
        .. ModAttributeOption.Standard(nameof(Strings.Filter_HasDependenciesBrowseToolTip)),
        new(ModAttributeFilter.HideInstalled,
            nameof(Strings.Filter_HideInstalled),
            nameof(Strings.Filter_HideInstalledToolTip)),
        new(ModAttributeFilter.ByFollowedAuthors,
            nameof(Strings.Filter_ByFollowedAuthors),
            nameof(Strings.Filter_ByFollowedAuthorsToolTip)),
        new(ModAttributeFilter.IgnoredOnly,
            nameof(Strings.Filter_IgnoredOnly),
            nameof(Strings.Filter_IgnoredOnlyToolTip)),
    ];

    //
    // The Filters button (the filters behind it in a panel): "Filters", or "Filters (2)" when any of
    // them narrow the list, so nothing hidden in the panel goes unnoticed. Sort and page size don't
    // narrow anything and aren't counted. Computed, so a language change rewrites it.
    //
    public string MoreFiltersLabel => MoreFiltersSet is var set && set > 0
        ? Text(Strings.Filter_MoreCountFormat, set)
        : Strings.Filter_More;

    private int MoreFiltersSet =>
        AttributeOptions.Count(o => o.IsSelected)
        + (SelectedCategory.Title is not null ? 1 : 0)
        + (SelectedFeaturedFilter.Value == FeaturedFilter.Include ? 0 : 1)
        + (DateRangeFilter.IsActive(SelectedPublishedRange.Value, PublishedFrom, PublishedUntil) ? 1 : 0)
        + (DateRangeFilter.IsActive(SelectedUpdatedRange.Value, UpdatedFrom, UpdatedUntil) ? 1 : 0)
        + (SelectedSearchScope.Value == SearchScope.TitleAndTeaser ? 0 : 1)
        + (SptVersionsChanged ? 1 : 0);

    // Whether the SPT version ticks differ from the app's own - just the line this install is on.
    private bool SptVersionsChanged => SptVersionOptions.Any(o => o.IsSelected != o.IsInstalledLine);

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
    // is set to, unset values included - "Category: All categories", "Sort by: Newest". Null while
    // the constructor is still filling the filters in.
    //
    private string? PinnedLabel(BrowseFilterSection section)
    {
        if (SelectedCategory is null || SelectedFeaturedFilter is null || SelectedSortOption is null
            || SelectedPublishedRange is null || SelectedUpdatedRange is null || SelectedSearchScope is null) return null;

        return section switch
        {
            BrowseFilterSection.Presets => Pill(Strings.Filter_SectionPresets, Presets?.ActiveName ?? Strings.Filter_PresetsNone),
            BrowseFilterSection.SptVersion => SptVersionFilterSummary,
            BrowseFilterSection.SearchIn => Pill(Strings.Filter_SectionSearchIn, SelectedSearchScope),
            BrowseFilterSection.Show => Pill(Strings.Filter_ShowHeader, AttributeOptions.Where(o => o.IsSelected).ToList() switch
            {
                [] => Strings.Filter_ShowNone,
                [var one] => one.Label,
                var many => Text(Strings.Filter_SelectedCountFormat, many.Count),
            }),
            BrowseFilterSection.Category => Pill(Strings.Filter_SectionCategory, SelectedCategory),
            BrowseFilterSection.Featured => Pill(Strings.Filter_SectionFeatured, SelectedFeaturedFilter),
            BrowseFilterSection.Published => Pill(Strings.Filter_SectionPublished, DateLabel(SelectedPublishedRange, PublishedFrom, PublishedUntil)),
            BrowseFilterSection.Updated => Pill(Strings.Filter_SectionUpdated, DateLabel(SelectedUpdatedRange, UpdatedFrom, UpdatedUntil)),
            BrowseFilterSection.Sort => Pill(Strings.Filter_SectionSort, SelectedSortOption),
            BrowseFilterSection.PageSize => Converters.PageSizeLabelConverter.Label(PageSize),
            _ => null,
        };
    }

    private static string Pill(string section, object? value) =>
        LocalizationService.Text(Strings.Filter_PillFormat, section, value?.ToString() ?? string.Empty);

    // A preset by its name; a custom range by its days, in the PC's own short date format.
    private static string DateLabel(DateRangeItem range, DateTime? from, DateTime? until)
    {
        if (range.Value != DateRangePreset.Custom) return range.Label;

        var (a, b) = DateRangeFilter.Window(DateRangePreset.Custom, DateTime.Today, from, until);
        return (a, b) switch
        {
            ({ } x, { } y) => LocalizationService.Text(Strings.Filter_DateBetweenFormat, x.ToShortDateString(), y.ToShortDateString()),
            ({ } x, null) => LocalizationService.Text(Strings.Filter_DateFromFormat, x.ToShortDateString()),
            (null, { } y) => LocalizationService.Text(Strings.Filter_DateUntilFormat, y.ToShortDateString()),
            _ => range.Label,
        };
    }

    private void RebuildActiveFilters()
    {
        // The generated properties raise during construction, before every option is in place.
        if (SelectedCategory is null || SelectedFeaturedFilter is null
            || SelectedPublishedRange is null || SelectedUpdatedRange is null || SelectedSearchScope is null) return;

        ActiveFilters.Clear();

        foreach (var option in AttributeOptions.Where(o => o.IsSelected))
            ActiveFilters.Add(new(Pill(Strings.Filter_ShowHeader, option.Label), () => option.IsSelected = false));

        if (SelectedCategory.Title is not null)
            ActiveFilters.Add(new(Pill(Strings.Filter_SectionCategory, SelectedCategory),
                () => SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Title is null) ?? CategoryFilterItem.All));

        if (SelectedFeaturedFilter.Value != FeaturedFilter.Include)
            ActiveFilters.Add(new(Pill(Strings.Filter_SectionFeatured, SelectedFeaturedFilter),
                () => SelectedFeaturedFilter = FeaturedFilterOptions.First(o => o.Value == FeaturedFilter.Include)));

        if (DateRangeFilter.IsActive(SelectedPublishedRange.Value, PublishedFrom, PublishedUntil))
            ActiveFilters.Add(new(Pill(Strings.Filter_SectionPublished, DateLabel(SelectedPublishedRange, PublishedFrom, PublishedUntil)),
                () =>
                {
                    PublishedFrom = PublishedUntil = null;
                    SelectedPublishedRange = DateRangeOptions[0];
                }));

        if (DateRangeFilter.IsActive(SelectedUpdatedRange.Value, UpdatedFrom, UpdatedUntil))
            ActiveFilters.Add(new(Pill(Strings.Filter_SectionUpdated, DateLabel(SelectedUpdatedRange, UpdatedFrom, UpdatedUntil)),
                () =>
                {
                    UpdatedFrom = UpdatedUntil = null;
                    SelectedUpdatedRange = DateRangeOptions[0];
                }));

        if (SelectedSearchScope.Value != SearchScope.TitleAndTeaser)
            ActiveFilters.Add(new(Pill(Strings.Filter_SectionSearchIn, SelectedSearchScope),
                () => SelectedSearchScope = SearchScopeOptions[0]));

        if (SptVersionsChanged)
        {
            var ticked = SptVersionOptions.Where(o => o.IsSelected).Select(o => o.Label).ToList();
            ActiveFilters.Add(new(
                Pill(Strings.Filter_SectionSptVersion,
                    ticked.Count == 0 ? Strings.Browse_AllSptVersions : string.Join(Strings.Common_ListSeparator, ticked)),
                ResetSptVersions));
        }

        OnPropertyChanged(nameof(HasActiveFilters));
    }

    /// <summary>The Category dropdown's entries, rebuilt from the cached catalog once it has loaded.</summary>
    public ObservableCollection<CategoryFilterItem> CategoryOptions { get; } = [CategoryFilterItem.All];

    [ObservableProperty]
    private CategoryFilterItem _selectedCategory = CategoryFilterItem.All;

    private bool IsOn(ModAttributeFilter filter) =>
        AttributeOptions.Any(o => o.Value == filter && o.IsSelected);

    /// <summary>The SPT version filter's checkable options, built once per session from the distinct
    /// major.minor release lines present in the cached catalog. The detected install's version starts
    /// pre-checked; an empty selection means no filter.</summary>
    public ObservableCollection<SptVersionOption> SptVersionOptions { get; } = [];

    [ObservableProperty]
    private string _sptVersionFilterSummary = Strings.Browse_AllSptVersions;

    private bool _sptVersionOptionsBuilt;
}
