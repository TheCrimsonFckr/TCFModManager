using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    //
    // What this page opens filtered and sorted to, saved from the page itself by SaveAsDefault
    // rather than set from Options - see Core's PageDefaults. Null on an install that has never
    // saved one, in which case every Default* helper below answers with the app's own default.
    //
    // Not readonly: Save as default and Restore my defaults both bring it up to date.
    private InstalledPageDefaults? _defaults;

    // What the Default* helpers read: the saved defaults, except while Clear filters runs, when
    // they answer with the app's own (see ClearFilters).
    private InstalledPageDefaults? Defaults => _presetFilters ?? (_clearingToAppDefaults ? null : _defaults);

    // A filter preset's filters while ApplyPreset runs (OPEN-25), read through the same Default*
    // helpers as Restore my defaults.
    private InstalledPageDefaults? _presetFilters;

    private bool _clearingToAppDefaults;

    //
    // Whether the saved category/group defaults have been handed to their dropdowns yet. Both lists
    // are rebuilt from what is installed, so the first rebuild is the earliest moment either
    // default can be resolved to a real entry - and every rebuild after that has to preserve
    // whatever is currently selected instead, or coming back to the page would silently reset it.
    //
    private bool _categoryDefaultApplied;

    private bool _groupDefaultApplied;

    // The name Recently installed was saved under, as a Sort before v1.19.0 and as an UpdateStatus
    // in v1.19.0. The first is read as Last installed (newest), the second as the tick box.
    private const string LegacyRecentlyInstalled = "RecentlyInstalled";

    private UpdateFilterItem DefaultUpdateFilter() =>
        SavedFilterDefaults.Parse<UpdateFilter>(Defaults?.UpdateStatus) is { } value
            ? UpdateFilterOptions.FirstOrDefault(o => o.Value == value) ?? UpdateFilterOptions[0]
            : UpdateFilterOptions[0];

    private IReadOnlyCollection<string>? DefaultAttributes() =>
        Defaults?.UpdateStatus == LegacyRecentlyInstalled
            ? [.. Defaults.Attributes, nameof(ModAttributeFilter.InstalledRecently)]
            : Defaults?.Attributes;

    private EnabledFilterItem DefaultEnabledFilter() =>
        SavedFilterDefaults.Parse<EnabledFilter>(Defaults?.Enabled) is { } value
            ? EnabledFilterOptions.FirstOrDefault(o => o.Value == value) ?? EnabledFilterOptions[0]
            : EnabledFilterOptions[0];

    private ModSortItem DefaultSortOption() =>
        SavedFilterDefaults.Parse<ModSortOption>(Defaults?.Sort) is { } value
            ? SortOptions.FirstOrDefault(o => o.Value == value) ?? SortOptions[0]
            : Defaults?.Sort == LegacyRecentlyInstalled
                ? SortOptions.First(o => o.Value == ModSortOption.InstalledNewest)
                : SortOptions[0];

    private GroupSortItem DefaultGroupSortOption() =>
        SavedFilterDefaults.Parse<GroupSortOption>(Defaults?.GroupSort) is { } value
            ? GroupSortOptions.FirstOrDefault(o => o.Value == value) ?? GroupSortOptions[0]
            : GroupSortOptions[0];

    private InstalledViewMode DefaultViewMode() =>
        SavedFilterDefaults.Parse<InstalledViewMode>(Defaults?.ViewMode) ?? InstalledViewMode.Cards;

    private int DefaultPageSize() =>
        SavedFilterDefaults.PageSize(Defaults?.PageSize, PageSizeOptions, InstalledViewModel.DefaultPageSizeValue);

    //
    // Described rather than looked up, because the entry it describes may not exist: the category
    // list is built from what is installed, so a saved category can be one nothing currently
    // matches. CategoryFilterItem.SameAs compares titles, so this finds its entry when there is one
    // and falls back to "All categories" when there isn't.
    //
    private CategoryFilterItem DefaultCategory() =>
        string.IsNullOrWhiteSpace(Defaults?.Category)
            ? CategoryFilterItem.All
            : new CategoryFilterItem(Defaults.Category!, Defaults.Category);

    //
    // Same again for groups. A saved group that has since been deleted reads as "All groups" -
    // the same thing RebuildSections already does with an assignment pointing at a deleted group.
    //
    private GroupFilterItem DefaultGroupFilter()
    {
        var saved = Defaults?.Group;

        if (string.IsNullOrWhiteSpace(saved)) return GroupFilterItem.All;
        if (saved.Equals("ungrouped", StringComparison.OrdinalIgnoreCase)) return GroupFilterItem.Ungrouped;

        // Label is irrelevant here - SameAs matches on the id and the all-groups flag only.
        return Guid.TryParse(saved, out var id)
            ? new GroupFilterItem(string.Empty, id, allGroups: false)
            : GroupFilterItem.All;
    }

    //
    // Two ways back (Chris, 2026-10-06), the same pair as Browse's:
    //
    // Clear filters - every filter, sort and search control back to the app's own defaults, so
    // every pill goes. Defaults reads null while it runs.
    //
    // Restore my defaults - back to what Save as default last captured, read from disk each time.
    // With nothing saved it is the same as Clear filters.
    //
    // The view mode is deliberately left alone: which of Cards/Groups/List you are looking at is
    // not a filter, and a button in the filter row that also switched view would be a surprise.
    //
    [RelayCommand]
    private void ClearFilters() => ResetFilters(toSaved: false);

    [RelayCommand]
    private void RestoreDefaults()
    {
        _defaults = new SettingsService().Load().InstalledDefaults;
        ResetFilters(toSaved: true);
        AppLog.Info("Installed", _defaults is null
            ? "restore defaults: none saved, cleared to the app's own"
            : "restored the saved filters");
    }

    private void ResetFilters(bool toSaved, InstalledFilterPreset? preset = null)
    {
        _suppressAutoApplyFilter = true;
        _clearingToAppDefaults = !toSaved;
        try
        {
            _family = null;

            // A preset brings its own search text; a hand-edited one with none leaves the box alone.
            if (preset is null) SearchText = string.Empty;
            else if (preset.Search is { } search) SearchText = search;
            SelectedUpdateFilter = DefaultUpdateFilter();
            SelectedEnabledFilter = DefaultEnabledFilter();
            SelectedGroupFilter = GroupFilterOptions.FirstOrDefault(o => o.SameAs(DefaultGroupFilter()))
                ?? GroupFilterOptions[0];
            SelectedSortOption = DefaultSortOption();
            SelectedGroupSortOption = DefaultGroupSortOption();
            SelectedCategory = CategoryOptions.FirstOrDefault(c => c.SameAs(DefaultCategory()))
                ?? CategoryOptions[0];
            SavedFilterDefaults.ApplyAttributes(AttributeOptions, DefaultAttributes() ?? []);
            UpdateAttributeFilterSummary();
            PageSize = DefaultPageSize();
        }
        finally
        {
            _clearingToAppDefaults = false;
            _suppressAutoApplyFilter = false;
        }

        ApplyFilter();
        RefreshActiveView(CurrentPage);
    }

    //
    // Captures the page exactly as it currently stands as what it opens at next time.
    //
    // Taken from the page rather than restated as a second set of dropdowns in Options: these
    // controls already exist, you can see what they do as you set them, and a filter added to this
    // page later is covered without anything in Options needing to know about it.
    //
    // The search box is not included. A page that opens already filtered to a phrase you typed
    // weeks ago looks broken rather than configured.
    //
    [RelayCommand]
    private void SaveAsDefault()
    {
        var service = new SettingsService();
        var settings = service.Load();

        settings.InstalledDefaults = _defaults = CaptureFilters();

        service.Save(settings);

        StatusMessage = Strings.Installed_SavedAsDefault;
        AppLog.Info("Installed", "saved the current filters as this page's default");
    }

    // The page's filters and view mode as a record - what Save as default saves, and the heart of a
    // filter preset (OPEN-25 R3).
    private InstalledPageDefaults CaptureFilters() => new()
    {
        ViewMode = ViewMode.ToString(),
        UpdateStatus = SelectedUpdateFilter.Value.ToString(),
        Enabled = SelectedEnabledFilter.Value.ToString(),
        Category = SelectedCategory.Title,
        Group = SelectedGroupFilter.AllGroups
            ? "all"
            : SelectedGroupFilter.GroupId?.ToString() ?? "ungrouped",
        Sort = SelectedSortOption.Value.ToString(),
        GroupSort = SelectedGroupSortOption.Value.ToString(),
        PageSize = PageSize,
        Attributes = SavedFilterDefaults.CapturedAttributes(AttributeOptions),
    };

    // The page as it is now, as a filter preset called `name` (OPEN-25 R3).
    private InstalledFilterPreset CapturePreset(string name) => new()
    {
        Name = name,
        Filters = CaptureFilters(),
        Search = SearchText ?? "",
        Pinned = FilterPins.ForPreset(),
    };

    //
    // Sets the page up as a filter preset (OPEN-25 §2.3): every filter, sort and page size replaced -
    // anything the preset doesn't name goes to the app's own - then its search text, pins and view
    // mode. Returns a note when the category or group it names no longer exists.
    //
    private string? ApplyPreset(InstalledFilterPreset preset)
    {
        string? note = null;
        _presetFilters = preset.Filters;
        try
        {
            ResetFilters(toSaved: true, preset);

            if (preset.Filters.Group is { Length: > 0 } group
                && !string.Equals(group, "all", StringComparison.OrdinalIgnoreCase)
                && SelectedGroupFilter.AllGroups)
            {
                note = LocalizationService.Text(Strings.Filter_PresetGroupGoneFormat, preset.Name);
            }
            else if (preset.Filters.Category is { Length: > 0 } category && SelectedCategory.Title is null)
            {
                note = LocalizationService.Text(Strings.Filter_PresetCategoryGoneFormat, preset.Name, category);
            }
        }
        finally
        {
            _presetFilters = null;
        }

        FilterPins.ApplyPreset(preset.Pinned);

        // Last: switching view rebuilds that view from the filters just set.
        if (SavedFilterDefaults.Parse<InstalledViewMode>(preset.Filters.ViewMode) is { } mode) ViewMode = mode;

        return note;
    }
}
