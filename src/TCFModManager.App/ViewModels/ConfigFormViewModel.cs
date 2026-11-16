using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public sealed partial class ConfigFormViewModel : LocalizedViewModel
{
    private readonly Action<string> _write;

    private BepInExConfigFile? _file;

    private List<(ConfigSectionItem Section, List<ConfigSettingRow> Rows)> _sections = [];

    public ConfigFormViewModel(Action<string> write) => _write = write;

    [ObservableProperty]
    private IReadOnlyList<ConfigFormItem> _items = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showChangedOnly;

    public bool ShowAllSettings => !ShowChangedOnly;

    public int ChangedCount => _file?.ChangedFromDefaultCount ?? 0;

    public bool HasChanges => ChangedCount > 0;

    public bool HasSettings => _file is { Settings.Count: > 0 };

    public bool HasItems => Items.Count > 0;

    public string ChangedLabel => LocalizationService.Text(Strings.Configs_ShowChangedFormat, ChangedCount);

    public string EmptyMessage =>
        !HasSettings ? Strings.Configs_FormNoSettings
        : ShowChangedOnly && SearchText.Trim().Length == 0 ? Strings.Configs_NothingChanged
        : Strings.Configs_NoSettingsMatch;

    public event EventHandler? Rebuilt;

    public void Load(string text)
    {
        _file = BepInExConfigFile.Parse(text);
        _sections = _file.Sections
            .Where(s => s.Settings.Count > 0)
            .Select(s => (new ConfigSectionItem(s.DisplayName), s.Settings.Select(setting => new ConfigSettingRow(this, setting)).ToList()))
            .ToList();

        Rebuild();
        NotifyCounts();
    }

    public void Clear()
    {
        _file = null;
        _sections = [];
        Rebuild();
        NotifyCounts();
    }

    public void ResetAll()
    {
        if (_file is null) return;

        _file.ResetAllToDefault();
        _write(_file.ToText());

        foreach (var (_, rows) in _sections)
        {
            foreach (var row in rows) row.Refresh();
        }

        Rebuild();
        NotifyCounts();
    }

    public string ChangesAsText(string heading)
    {
        var text = new StringBuilder(heading).AppendLine();

        foreach (var section in _file?.Sections ?? [])
        {
            var changed = section.Settings.Where(s => s.DiffersFromDefault).ToList();
            if (changed.Count == 0) continue;

            text.AppendLine().AppendLine($"[{section.DisplayName}]");
            foreach (var setting in changed)
            {
                text.AppendLine(LocalizationService.Text(
                    Strings.Configs_CopyLineFormat,
                    setting.Key,
                    ConfigSettingRow.Shown(setting.Value),
                    ConfigSettingRow.Shown(setting.DefaultValue)));
            }
        }

        return text.ToString().TrimEnd() + Environment.NewLine;
    }

    internal void Edited()
    {
        if (_file is null) return;

        _write(_file.ToText());
        NotifyCounts();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnShowChangedOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAllSettings));
        Rebuild();
    }

    [RelayCommand]
    private void ShowAll() => ShowChangedOnly = false;

    [RelayCommand]
    private void ShowChanged() => ShowChangedOnly = true;

    private void Rebuild()
    {
        var term = SearchText.Trim();
        var items = new List<ConfigFormItem>();

        foreach (var (section, rows) in _sections)
        {
            var sectionMatches = term.Length > 0 && section.Name.Contains(term, StringComparison.OrdinalIgnoreCase);

            var shown = rows
                .Where(r => !ShowChangedOnly || r.DiffersFromDefault)
                .Where(r => term.Length == 0 || sectionMatches || r.Matches(term))
                .ToList();

            if (shown.Count == 0) continue;

            items.Add(section);
            for (var i = 0; i < shown.Count; i++)
            {
                shown[i].IsLast = i == shown.Count - 1;
                items.Add(shown[i]);
            }
        }

        Items = items;
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(EmptyMessage));
        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(ChangedCount));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangedLabel));
        OnPropertyChanged(nameof(HasSettings));
        OnPropertyChanged(nameof(EmptyMessage));
    }
}

public abstract partial class ConfigFormItem : LocalizedViewModel
{
    [ObservableProperty]
    private bool _isLast;
}

public sealed class ConfigSectionItem(string name) : ConfigFormItem
{
    public string Name { get; } = name;
}
