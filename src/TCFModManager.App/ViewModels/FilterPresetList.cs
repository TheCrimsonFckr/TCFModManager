using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

/// <summary>One saved filter preset in the Filter presets section (OPEN-25).</summary>
public sealed partial class FilterPresetRow(IFilterPreset preset) : ObservableObject
{
    public IFilterPreset Preset { get; set; } = preset;

    public string Name => Preset.Name;

    // The page is set up exactly as this preset right now.
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = "";

    public void NameChanged() => OnPropertyChanged(nameof(Name));
}

//
// A page's Filter presets section (OPEN-25 §2.2-2.4): the saved presets, which one the page is set
// up as right now, and save / update / rename / delete / apply. Shared by Browse and Installed; the
// page hands it how to capture itself and how to apply a preset, and where in settings.json its list
// lives. Separate from the page defaults (R2): nothing here changes what the page opens at.
//
public sealed partial class FilterPresetList<T> : ObservableObject where T : class, IFilterPreset
{
    private readonly string _page;
    private readonly Func<AppSettings, List<T>> _list;
    private readonly Func<string, T> _capture;
    private readonly Func<T, string?> _apply;
    private readonly Action<string> _say;

    /// <param name="page">"Browse" or "Installed", for the log.</param>
    /// <param name="list">The page's list in settings.</param>
    /// <param name="capture">The page as it is now, as a preset with the given name.</param>
    /// <param name="apply">Sets the page up as the preset; returns a note when part of it no longer
    /// exists (a category, a group), else null.</param>
    /// <param name="say">Shows a line in the page's status.</param>
    public FilterPresetList(string page, AppSettings settings, Func<AppSettings, List<T>> list,
        Func<string, T> capture, Func<T, string?> apply, Action<string> say)
    {
        _page = page;
        _list = list;
        _capture = capture;
        _apply = apply;
        _say = say;

        foreach (var preset in list(settings)) Rows.Add(new FilterPresetRow(preset));
    }

    public ObservableCollection<FilterPresetRow> Rows { get; } = [];

    public bool HasNone => Rows.Count == 0;

    // What the name box under the list holds.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _newName = "";

    // The preset the page is set up as, or null - a pinned dropdown reads its name.
    [ObservableProperty]
    private string? _activeName;

    private bool _ready;

    //
    // Works out which preset the page is set up as. The page calls it whenever anything on it
    // changes; it compares, so nothing about it is ever stored or goes stale.
    //
    public void Refresh()
    {
        _ready = true;
        if (Rows.Count == 0)
        {
            ActiveName = null;
            return;
        }

        var page = _capture("");
        var active = FilterPresetMatch.Active(Rows.Select(r => (T)r.Preset), page);
        foreach (var row in Rows) row.IsActive = ReferenceEquals(row.Preset, active);
        ActiveName = active?.Name;
    }

    private bool CanSave() => FilterPresetRules.CleanName(NewName).Length > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var name = FilterPresetRules.CleanName(NewName);
        var settings = Load(out var service);
        var list = _list(settings);

        if (FilterPresetRules.Find(list, name) is { } existing
            && !Ask(Strings.Filter_PresetReplaceTitle, LocalizationService.Text(Strings.Filter_PresetReplaceQuestionFormat, existing.Name)))
        {
            return;
        }

        var saved = FilterPresetRules.Save(list, _capture(name));
        service.Save(settings);
        Reload(settings);

        NewName = "";
        _say(LocalizationService.Text(Strings.Filter_PresetSavedFormat, saved.Name));
        AppLog.Info(_page, $"saved filter preset '{saved.Name}'");
    }

    [RelayCommand]
    private void Apply(FilterPresetRow? row)
    {
        if (row?.Preset is not T preset) return;

        var note = _apply(preset);
        _say(note ?? LocalizationService.Text(Strings.Filter_PresetAppliedFormat, preset.Name));
        AppLog.Info(_page, $"applied filter preset '{preset.Name}'{(note is null ? "" : " (part of it no longer exists)")}");
        Refresh();
    }

    // The page as it is now, saved over this preset - same name, same place, same Id.
    [RelayCommand]
    private void Update(FilterPresetRow? row)
    {
        if (row is null) return;

        var settings = Load(out var service);
        var saved = FilterPresetRules.Save(_list(settings), _capture(row.Name));
        service.Save(settings);
        Reload(settings);

        _say(LocalizationService.Text(Strings.Filter_PresetSavedFormat, saved.Name));
        AppLog.Info(_page, $"updated filter preset '{saved.Name}'");
    }

    [RelayCommand]
    private void StartRename(FilterPresetRow? row)
    {
        if (row is null) return;

        foreach (var other in Rows) other.IsRenaming = false;
        row.RenameText = row.Name;
        row.IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRename(FilterPresetRow? row)
    {
        if (row is not null) row.IsRenaming = false;
    }

    [RelayCommand]
    private void FinishRename(FilterPresetRow? row)
    {
        if (row is null || !row.IsRenaming) return;

        var settings = Load(out var service);
        var list = _list(settings);

        if (FilterPresetRules.CleanName(row.RenameText) == row.Name)
        {
            row.IsRenaming = false;
            return;
        }

        if (!FilterPresetRules.Rename(list, row.Preset.Id, row.RenameText))
        {
            _say(FilterPresetRules.Check(list, row.RenameText, row.Preset.Id) == PresetNameProblem.Taken
                ? Strings.Filter_PresetNameTaken
                : Strings.Filter_PresetNameEmpty);
            return;
        }

        service.Save(settings);
        Reload(settings);
        AppLog.Info(_page, "renamed a filter preset");
    }

    [RelayCommand]
    private void Delete(FilterPresetRow? row)
    {
        if (row is null) return;
        if (!Ask(Strings.Filter_PresetDeleteTitle, LocalizationService.Text(Strings.Filter_PresetDeleteQuestionFormat, row.Name))) return;

        var settings = Load(out var service);
        _list(settings).RemoveAll(p => p.Id == row.Preset.Id);
        service.Save(settings);
        Reload(settings);

        AppLog.Info(_page, $"deleted filter preset '{row.Name}'");
    }

    // Rows rebuilt from the saved list, in its order.
    private void Reload(AppSettings settings)
    {
        Rows.Clear();
        foreach (var preset in _list(settings)) Rows.Add(new FilterPresetRow(preset));
        OnPropertyChanged(nameof(HasNone));
        if (_ready) Refresh();
    }

    private static AppSettings Load(out SettingsService service)
    {
        service = new SettingsService();
        return service.Load();
    }

    private static bool Ask(string title, string question) =>
        MessageBox.Show(question, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
