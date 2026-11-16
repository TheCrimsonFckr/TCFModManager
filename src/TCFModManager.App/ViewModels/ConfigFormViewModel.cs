using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public enum ConfigFormMode
{
    File,
    Changed,
    Keybinds,
}

public sealed record ConfigFormSource(ConfigModRow Mod, ConfigOpenFile File);

public sealed partial class ConfigFormViewModel : LocalizedViewModel
{
    private const string Arrow = " › ";

    private List<(ConfigSectionItem Header, List<ConfigSettingRow> Rows)> _groups = [];

    private IReadOnlyList<ConfigOpenFile> _files = [];

    [ObservableProperty]
    private IReadOnlyList<ConfigFormItem> _items = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAllSettings))]
    private bool _showChangedOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFileMode))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private ConfigFormMode _mode;

    public event EventHandler<ConfigOpenFile>? FileEdited;

    public event EventHandler? Rebuilt;

    public bool IsFileMode => Mode == ConfigFormMode.File;

    public bool ShowAllSettings => !ShowChangedOnly;

    public int ChangedCount => _files.Sum(f => f.ChangedCount);

    public bool HasChanges => ChangedCount > 0;

    public bool HasSettings => _groups.Count > 0;

    public bool HasItems => Items.Count > 0;

    public string ChangedLabel => LocalizationService.Text(Strings.Configs_ShowChangedFormat, ChangedCount);

    public string EmptyMessage => SearchText.Trim().Length > 0 && HasSettings
        ? Strings.Configs_NoSettingsMatch
        : Mode switch
        {
            ConfigFormMode.Changed => Strings.Configs_NothingChangedAnywhere,
            ConfigFormMode.Keybinds => Strings.Configs_NoKeybinds,
            _ when !HasSettings => Strings.Configs_FormNoSettings,
            _ when ShowChangedOnly => Strings.Configs_NothingChanged,
            _ => Strings.Configs_NoSettingsMatch,
        };

    public void ShowFile(ConfigOpenFile file)
    {
        Mode = ConfigFormMode.File;
        _files = [file];
        _groups = (file.Parsed?.Sections ?? [])
            .Where(s => s.Settings.Count > 0)
            .Select(s => (new ConfigSectionItem(s.DisplayName), s.Settings.Select(setting => new ConfigSettingRow(this, file, setting)).ToList()))
            .ToList();

        Rebuild();
        NotifyCounts();
    }

    public void ShowChanged(IReadOnlyList<ConfigFormSource> sources)
    {
        Mode = ConfigFormMode.Changed;
        ShowChangedOnly = false;
        _files = sources.Select(s => s.File).ToList();
        _groups = [];

        foreach (var mod in sources.GroupBy(s => s.Mod))
        {
            var rows = RowsOf(mod.Key, mod.ToList(), s => s.DiffersFromDefault);
            if (rows.Count == 0) continue;

            var files = mod.Select(s => s.File).ToList();
            _groups.Add((new ConfigSectionItem(mod.Key.Title, () => ResetFiles(files)), rows));
        }

        Rebuild();
        NotifyCounts();
    }

    public void ShowKeybinds(IReadOnlyList<ConfigFormSource> sources)
    {
        Mode = ConfigFormMode.Keybinds;
        ShowChangedOnly = false;
        _files = sources.Select(s => s.File).ToList();
        _groups = [];

        var byMod = sources
            .GroupBy(s => s.Mod)
            .Select(mod => (Mod: mod.Key, Rows: RowsOf(mod.Key, mod.ToList(), s => s.Kind == BepInExValueKind.Shortcut)))
            .Where(m => m.Rows.Count > 0)
            .ToList();

        var shared = Clashes(byMod.SelectMany(m => m.Rows))
            .SelectMany(g => g.Keybinds.Select(k => k.Owner))
            .ToList();

        if (shared.Count > 0)
        {
            var sharedRows = shared
                .Select(r => new ConfigSettingRow(this, r.File, r.Setting, string.Concat(r.ModTitle, Arrow, r.Context), r.ModTitle))
                .ToList();
            _groups.Add((new ConfigSectionItem(Strings.Configs_SharedKeysHeading), sharedRows));
        }

        foreach (var (mod, rows) in byMod)
        {
            var own = rows.Where(r => !shared.Any(s => ReferenceEquals(s.Setting, r.Setting))).ToList();
            if (own.Count > 0) _groups.Add((new ConfigSectionItem(mod.Title), own));
        }

        NoteClashes();
        Rebuild();
        NotifyCounts();
    }

    public void Clear()
    {
        _files = [];
        _groups = [];
        Rebuild();
        NotifyCounts();
    }

    public void ResetFiles(IEnumerable<ConfigOpenFile> files)
    {
        foreach (var file in files)
        {
            if (file.Parsed is not { } parsed) continue;

            parsed.ResetAllToDefault();
            file.Commit();
            FileEdited?.Invoke(this, file);
        }

        foreach (var (_, rows) in _groups)
        {
            foreach (var row in rows) row.Refresh();
        }

        NoteClashes();
        Rebuild();
        NotifyCounts();
    }

    public static string ChangesAsText(IEnumerable<(string Heading, ConfigOpenFile File)> files)
    {
        var text = new StringBuilder();

        foreach (var (heading, file) in files)
        {
            if (file.Parsed is not { } parsed || parsed.ChangedFromDefaultCount == 0) continue;

            if (text.Length > 0) text.AppendLine();
            text.AppendLine(heading);

            foreach (var section in parsed.Sections)
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
        }

        return text.ToString().TrimEnd() + Environment.NewLine;
    }

    internal void Edited(ConfigSettingRow row)
    {
        row.File.Commit();

        foreach (var (_, rows) in _groups)
        {
            foreach (var other in rows)
            {
                if (other != row && ReferenceEquals(other.Setting, row.Setting)) other.Refresh();
            }
        }

        if (row.Kind == BepInExValueKind.Shortcut) NoteClashes();

        FileEdited?.Invoke(this, row.File);
        NotifyCounts();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnShowChangedOnlyChanged(bool value) => Rebuild();

    [RelayCommand]
    private void ShowAll() => ShowChangedOnly = false;

    [RelayCommand]
    private void ShowChanged() => ShowChangedOnly = true;

    private List<ConfigSettingRow> RowsOf(ConfigModRow mod, List<ConfigFormSource> sources, Func<BepInExConfigSetting, bool> include)
    {
        var several = sources.Count > 1;

        return sources
            .SelectMany(s => s.File.Parsed!.Sections.SelectMany(section => section.Settings
                .Where(include)
                .Select(setting => new ConfigSettingRow(
                    this,
                    s.File,
                    setting,
                    several ? string.Concat(s.File.Entry.FileName, Arrow, section.DisplayName) : section.DisplayName,
                    mod.Title))))
            .ToList();
    }

    private static IEnumerable<BepInExKeybindGroup<ConfigSettingRow>> Clashes(IEnumerable<ConfigSettingRow> rows) =>
        BepInExKeybinds.Group(rows
                .GroupBy(r => r.Setting)
                .Select(g => g.First())
                .Select(r => new BepInExKeybind<ConfigSettingRow>(r, r.Setting, BepInExShortcut.Read(r.Setting.Value))))
            .Where(g => g.IsClash);

    private void NoteClashes()
    {
        if (Mode != ConfigFormMode.Keybinds) return;

        var all = _groups.SelectMany(g => g.Rows).ToList();
        var others = new Dictionary<BepInExConfigSetting, List<ConfigSettingRow>>();

        foreach (var group in Clashes(all))
        {
            foreach (var member in group.Keybinds)
                others[member.Setting] = group.Keybinds.Where(k => k != member).Select(k => k.Owner).ToList();
        }

        foreach (var row in all)
        {
            row.Note = others.TryGetValue(row.Setting, out var with)
                ? LocalizationService.Text(Strings.Configs_SameKeyAsFormat, string.Join(", ", with.Select(w => string.Concat(w.ModTitle, Arrow, w.Name))))
                : null;
        }
    }

    private void Rebuild()
    {
        var term = SearchText.Trim();
        var items = new List<ConfigFormItem>();

        foreach (var (header, rows) in _groups)
        {
            var headerMatches = term.Length > 0 && header.Name.Contains(term, StringComparison.OrdinalIgnoreCase);

            var shown = rows
                .Where(r => !ShowChangedOnly || r.DiffersFromDefault)
                .Where(r => term.Length == 0 || headerMatches || r.Matches(term))
                .ToList();

            if (shown.Count == 0) continue;

            items.Add(header);
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

public sealed class ConfigSectionItem : ConfigFormItem
{
    public ConfigSectionItem(string name, Action? reset = null)
    {
        Name = name;
        if (reset is not null) ResetCommand = new RelayCommand(reset);
    }

    public string Name { get; }

    public IRelayCommand? ResetCommand { get; }

    public bool CanReset => ResetCommand is not null;
}
