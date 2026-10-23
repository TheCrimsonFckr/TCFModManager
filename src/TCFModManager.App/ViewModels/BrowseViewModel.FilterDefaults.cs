using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel
{
    //
    // What this page opens filtered and sorted to, saved from the page itself by SaveAsDefault
    // rather than set from Options - see Core's PageDefaults. Null on an install that has never
    // saved one, in which case every Default* helper below answers with the app's own default.
    //
    // Not readonly: Save as default and Restore my defaults both bring it up to date.
    private BrowsePageDefaults? _defaults;

    // Whether the saved category default has reached its dropdown yet. The list is built from the
    // catalog, so the first build is the earliest moment it can be resolved to a real entry.
    private bool _categoryDefaultApplied;

    //
    // What the Default* helpers below read: the saved defaults, except while Clear filters runs.
    // Clearing puts the page back to the app's own defaults, not the saved ones, so every pill goes
    // (Chris, 2026-10-06) - the saved defaults are still what the page opens at next time.
    //
    private BrowsePageDefaults? Defaults => _presetFilters ?? (_clearingToAppDefaults ? null : _defaults);

    // A filter preset's filters while ApplyPreset runs (OPEN-25), read through the same Default*
    // helpers as Restore my defaults.
    private BrowsePageDefaults? _presetFilters;

    private bool _clearingToAppDefaults;

    private SortOptionItem DefaultSortOption() =>
        SavedFilterDefaults.Parse<ModSortOrder>(Defaults?.Sort) is { } value
            ? SortOptions.FirstOrDefault(o => o.Value == value) ?? SortOptions[0]
            : SortOptions[0];

    private FeaturedFilterItem DefaultFeaturedFilter() =>
        SavedFilterDefaults.Parse<FeaturedFilter>(Defaults?.Featured) is { } value
            ? FeaturedFilterOptions.FirstOrDefault(o => o.Value == value) ?? FeaturedFilterOptions[0]
            : FeaturedFilterOptions[0];

    // A saved Custom never comes back as Custom - its days were not saved (R20).
    private DateRangeItem DefaultDateRange(string? saved) =>
        SavedFilterDefaults.Parse<DateRangePreset>(saved) is { } value && value != DateRangePreset.Custom
            ? DateRangeOptions.FirstOrDefault(o => o.Value == value) ?? DateRangeOptions[0]
            : DateRangeOptions[0];

    private SearchScopeItem DefaultSearchScope() =>
        SavedFilterDefaults.Parse<SearchScope>(Defaults?.SearchScope) is { } value
            ? SearchScopeOptions.FirstOrDefault(o => o.Value == value) ?? SearchScopeOptions[0]
            : SearchScopeOptions[0];

    private int DefaultPageSize() =>
        SavedFilterDefaults.PageSize(Defaults?.PageSize, PageSizeOptions, DefaultPageSizeValue);

    // Described rather than looked up: the entry may not be in the list yet, or at all if The Forge
    // has stopped using that category. CategoryFilterItem.SameAs matches on the title.
    private CategoryFilterItem DefaultCategory() =>
        string.IsNullOrWhiteSpace(Defaults?.Category)
            ? CategoryFilterItem.All
            : new CategoryFilterItem(Defaults.Category!, Defaults.Category);

    private void ApplyCustomRanges(BrowseFilterPreset preset)
    {
        var custom = DateRangeOptions.First(o => o.Value == DateRangePreset.Custom);

        if (SavedFilterDefaults.Parse<DateRangePreset>(preset.Filters.Published) == DateRangePreset.Custom)
        {
            (PublishedFrom, PublishedUntil) = (preset.PublishedFrom, preset.PublishedUntil);
            SelectedPublishedRange = custom;
        }

        if (SavedFilterDefaults.Parse<DateRangePreset>(preset.Filters.Updated) == DateRangePreset.Custom)
        {
            (UpdatedFrom, UpdatedUntil) = (preset.UpdatedFrom, preset.UpdatedUntil);
            SelectedUpdatedRange = custom;
        }
    }

    //
    // Two ways back (Chris, 2026-10-06):
    //
    // Clear filters - every filter and view option back to the app's own defaults, so every pill
    // goes. Defaults reads null while it runs, so each Default* helper answers with the app's own,
    // and the SPT version ticks go back to the line this install is on.
    //
    // Restore my defaults - back to what Save as default last captured, which is also what the page
    // opens at. Read from disk each time, so a save from the page or a reset in Options since the
    // page opened is what you get. With nothing saved it is the same as Clear filters.
    //
    // Either re-applies once, immediately.
    //
    [RelayCommand]
    private void ClearFilters() => ResetFilters(toSaved: false);

    [RelayCommand]
    private void RestoreDefaults()
    {
        _defaults = new SettingsService().Load().BrowseDefaults;
        ResetFilters(toSaved: true);
        AppLog.Info("Browse", _defaults is null
            ? "restore defaults: none saved, cleared to the app's own"
            : "restored the saved filters");
    }

    private void ResetFilters(bool toSaved, BrowseFilterPreset? preset = null)
    {
        _suppressAutoApplyFilter = true;
        _clearingToAppDefaults = !toSaved;
        try
        {
            // A preset brings its own search text; a hand-edited one with none leaves the box alone.
            if (preset is null) SearchText = string.Empty;
            else if (preset.Search is { } search) SearchText = search;
            var savedVersions = Defaults?.SptVersions;
            foreach (var option in SptVersionOptions)
                option.IsSelected = savedVersions is null ? option.IsInstalledLine : savedVersions.Contains(option.Label);
            UpdateSptVersionFilterSummary();
            SelectedSortOption = DefaultSortOption();
            PageSize = DefaultPageSize();
            SelectedFeaturedFilter = DefaultFeaturedFilter();
            SelectedCategory = CategoryOptions.FirstOrDefault(c => c.SameAs(DefaultCategory()))
                ?? CategoryOptions[0];
            SavedFilterDefaults.ApplyAttributes(AttributeOptions, Defaults?.Attributes ?? []);
            UpdateAttributeFilterSummary();
            PublishedFrom = PublishedUntil = UpdatedFrom = UpdatedUntil = null;
            SelectedPublishedRange = DefaultDateRange(Defaults?.Published);
            SelectedUpdatedRange = DefaultDateRange(Defaults?.Updated);
            SelectedSearchScope = DefaultSearchScope();

            // A preset's custom range comes back with its days (OPEN-25 R5); a default's never does.
            if (preset is not null) ApplyCustomRanges(preset);
        }
        finally
        {
            _clearingToAppDefaults = false;
            _suppressAutoApplyFilter = false;
        }

        // Only re-run the filter if there's actually a catalog to filter against yet.
        if (HasLoadedResults) ApplyFilter();
    }

    //
    // Captures the page exactly as it currently stands as what it opens at next time. Same
    // arrangement as Installed's - see InstalledViewModel.SaveAsDefault for why this lives on the
    // page rather than as a second set of dropdowns in Options.
    //
    // The search box is left out on purpose. The SPT version ticks are included, and saving them
    // replaces the app's own behaviour of pre-ticking whichever line your install is on - which is
    // the point for anyone who browses for a version they are not currently running.
    //
    [RelayCommand]
    private void SaveAsDefault()
    {
        var service = new SettingsService();
        var settings = service.Load();

        settings.BrowseDefaults = _defaults = CaptureFilters(keepCustomRanges: false);

        service.Save(settings);

        StatusMessage = Strings.Browse_SavedAsDefault;
        AppLog.Info("Browse", "saved the current filters as this page's default");
    }

    //
    // The page's filters as a record - what Save as default saves, and the heart of a filter preset
    // (OPEN-25). A custom range is a one-off for a default, so there it saves as Any time (OPEN-12
    // R20); a preset keeps it, with its days (OPEN-25 R5).
    //
    private BrowsePageDefaults CaptureFilters(bool keepCustomRanges) => new()
    {
        Sort = SelectedSortOption.Value.ToString(),
        Featured = SelectedFeaturedFilter.Value.ToString(),
        Category = SelectedCategory.Title,
        PageSize = PageSize,
        Attributes = SavedFilterDefaults.CapturedAttributes(AttributeOptions),
        Published = SavedRange(SelectedPublishedRange, keepCustomRanges),
        Updated = SavedRange(SelectedUpdatedRange, keepCustomRanges),
        SearchScope = SelectedSearchScope.Value.ToString(),

        // Only once the options exist. Saving an empty list from a page whose version filter has not
        // been built yet would read back as "show every SPT version", which is a real setting and not
        // what was on screen.
        SptVersions = _sptVersionOptionsBuilt
            ? SptVersionOptions.Where(o => o.IsSelected).Select(o => o.Label).ToList()
            : _defaults?.SptVersions,
    };

    private static string SavedRange(DateRangeItem range, bool keepCustom) =>
        (range.Value == DateRangePreset.Custom && !keepCustom ? DateRangePreset.AnyTime : range.Value).ToString();

    // The page as it is now, as a filter preset called `name` (OPEN-25 R3, R5).
    private BrowseFilterPreset CapturePreset(string name)
    {
        var preset = new BrowseFilterPreset
        {
            Name = name,
            Filters = CaptureFilters(keepCustomRanges: true),
            Search = SearchText ?? "",
            Pinned = FilterPins.ForPreset(),
        };

        if (IsPublishedCustom) (preset.PublishedFrom, preset.PublishedUntil) = (PublishedFrom, PublishedUntil);
        if (IsUpdatedCustom) (preset.UpdatedFrom, preset.UpdatedUntil) = (UpdatedFrom, UpdatedUntil);
        return preset;
    }

    //
    // Sets the page up as a filter preset (OPEN-25 §2.3): every filter, sort and page size replaced -
    // anything the preset doesn't name goes to the app's own - then its search text and pins, and
    // one filter pass. Returns a note when the category it names is no longer in the catalog.
    //
    private string? ApplyPreset(BrowseFilterPreset preset)
    {
        string? note = null;
        _presetFilters = preset.Filters;
        try
        {
            ResetFilters(toSaved: true, preset);

            if (preset.Filters.Category is { Length: > 0 } category && SelectedCategory.Title is null)
                note = Text(Strings.Filter_PresetCategoryGoneFormat, preset.Name, category);
        }
        finally
        {
            _presetFilters = null;
        }

        FilterPins.ApplyPreset(preset.Pinned);
        return note;
    }
}
