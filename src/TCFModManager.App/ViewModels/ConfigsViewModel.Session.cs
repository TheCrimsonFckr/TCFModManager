using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public enum ConfigsView
{
    Mod,
    Changed,
    Keybinds,
}

public sealed partial class ConfigsViewModel
{
    private readonly Dictionary<string, ConfigOpenFile> _open = new(StringComparer.OrdinalIgnoreCase);

    private ConfigOpenFile? _current;

    private List<ConfigModRow> _mods = [];

    private bool _syncingEditor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetModCommand))]
    private ConfigModRow? _selectedMod;

    [ObservableProperty]
    private ConfigsView _view;

    public bool ShowModView => View == ConfigsView.Mod;

    public bool ShowListView => View != ConfigsView.Mod;

    public bool ShowChangedView => View == ConfigsView.Changed;

    public bool ShowKeybindsView => View == ConfigsView.Keybinds;

    public bool ShowToolbar => CanUseForm || ShowListView;

    public string ListTitle => ShowKeybindsView ? Strings.Configs_KeybindsViewTitle : Strings.Configs_ChangedViewTitle;

    public string ListNote => ShowKeybindsView ? Strings.Configs_KeybindsViewNote : Strings.Configs_ChangedViewNote;

    public int TotalChanged { get; private set; }

    public int TotalKeybinds { get; private set; }

    public string ChangedViewLabel => Text(Strings.Configs_ChangedViewFormat, TotalChanged);

    public string KeybindsViewLabel => Text(Strings.Configs_KeybindsViewFormat, TotalKeybinds);

    public string Footer { get; private set; } = string.Empty;

    public int DirtyCount => _open.Values.Count(f => f.IsDirty);

    public bool HasDirty => DirtyCount > 0;

    public string DirtyLabel => Text(Strings.Configs_UnsavedFilesFormat, DirtyCount);

    partial void OnSelectedModChanged(ConfigModRow? value)
    {
        if (value is null) return;

        View = ConfigsView.Mod;

        var term = SearchText.Trim();
        Form.SearchText = term.Length > 0 && !value.MatchesName(term) ? term : string.Empty;
        Form.ShowChangedOnly = false;

        if (ReferenceEquals(SelectedEntry, value.LastFile))
        {
            MarkTabs();
            Load(value.LastFile);
        }
        else
        {
            SelectedEntry = value.LastFile;
        }
    }

    partial void OnViewChanged(ConfigsView value)
    {
        OnPropertyChanged(nameof(ShowModView));
        OnPropertyChanged(nameof(ShowListView));
        OnPropertyChanged(nameof(ShowChangedView));
        OnPropertyChanged(nameof(ShowKeybindsView));
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(ListNote));
        OnPropertyChanged(nameof(ShowEditor));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowRawView));
        OnPropertyChanged(nameof(ShowFormList));
        OnPropertyChanged(nameof(ShowToolbar));
        OnPropertyChanged(nameof(CanUseForm));
        OnPropertyChanged(nameof(ShowFormView));

        _formStale = true;

        if (value == ConfigsView.Mod)
        {
            SyncForm();
            return;
        }

        SelectedMod = null;
        SelectedEntry = null;
        Form.SearchText = string.Empty;
        ShowList();
        SyncForm();
    }

    [RelayCommand]
    private void OpenChangedView()
    {
        if (View == ConfigsView.Changed) ShowList();
        else View = ConfigsView.Changed;
    }

    [RelayCommand]
    private void OpenKeybindsView()
    {
        if (View == ConfigsView.Keybinds) ShowList();
        else View = ConfigsView.Keybinds;
    }

    [RelayCommand]
    private void SelectFile(ConfigFileTab? tab)
    {
        if (tab is not null) SelectedEntry = tab.Entry;
    }

    private bool CanResetMod => ShowModView && SelectedMod is { HasChanges: true };

    [RelayCommand(CanExecute = nameof(CanResetMod))]
    private void ResetMod()
    {
        if (SelectedMod is null) return;

        Form.ResetFiles(CfgFiles(SelectedMod));
        _formStale = true;
        SyncForm();
    }

    private bool CanResetEverything => ShowChangedView && TotalChanged > 0;

    [RelayCommand(CanExecute = nameof(CanResetEverything))]
    private void ResetEverything() => Form.ResetFiles(_mods.SelectMany(CfgFiles).ToList());

    private bool CanCopyChanges => ShowModView ? SelectedMod is { HasChanges: true } : ShowChangedView && TotalChanged > 0;

    [RelayCommand(CanExecute = nameof(CanCopyChanges))]
    private void CopyChanges()
    {
        IReadOnlyList<ConfigModRow> mods = ShowModView && SelectedMod is { } mod ? [mod] : _mods;
        var files = mods.SelectMany(m => CfgFiles(m).Select(f => (Text(Strings.Configs_CopyHeadingFormat, m.Title, f.Entry.FileName), f)));

        try
        {
            Clipboard.SetText(ConfigFormViewModel.ChangesAsText(files));
            StatusMessage = Strings.Configs_ChangesCopied;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            StatusMessage = Strings.Configs_ChangesCopyFailed;
        }
    }

    [RelayCommand(CanExecute = nameof(HasDirty))]
    private void SaveAll()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var dirty = _open.Values.Where(f => f.IsDirty).ToList();
        var problems = new List<string>();
        var saved = 0;

        foreach (var file in dirty)
        {
            var result = ModConfigStore.Save(installPath, file.Entry.FullPath, file.Text, file.Loaded, DateTimeOffset.Now, overwriteChangesOnDisk: false);

            switch (result.Outcome)
            {
                case ModConfigSaveOutcome.Saved:
                    file.Saved(result.Saved!);
                    saved++;
                    break;
                case ModConfigSaveOutcome.ChangedOnDisk:
                    problems.Add(Text(Strings.Configs_NotSavedChangedOnDiskFormat, file.Entry.FileName));
                    break;
                case ModConfigSaveOutcome.Invalid:
                    problems.Add(Text(Strings.Configs_NotSavedInvalidFormat, file.Entry.FileName));
                    break;
                default:
                    problems.Add(Text(Strings.Configs_NotSavedFailedFormat, file.Entry.FileName));
                    break;
            }

            FileChanged(file);
        }

        AppLog.Info("Configs", $"save all: {saved} saved, {problems.Count} not saved");

        StatusMessage = problems.Count == 0
            ? Text(Strings.Configs_SavedAllFormat, saved)
            : Text(Strings.Configs_SavedSomeFormat, saved, string.Join("; ", problems));
    }

    [RelayCommand(CanExecute = nameof(HasDirty))]
    private void RevertAll()
    {
        var dirty = _open.Values.Where(f => f.IsDirty).ToList();
        if (dirty.Count == 0) return;

        var drop = AskWindow.Show(new AskRequest
        {
            Title = Strings.Configs_DiscardTitle,
            Lead = Text(Strings.Configs_DiscardFormat, TextLists.Join(dirty.Select(f => f.Entry.FileName).ToList())),
            First = Strings.Configs_DiscardButton,
            Cancel = Strings.Configs_KeepEditing,
        }) == AskChoice.First;

        if (!drop) return;

        foreach (var file in dirty)
        {
            file.Revert();
            FileChanged(file);
        }

        if (_current is not null) SetEditor(_current.Text);
        _formStale = true;

        if (ShowListView) ShowList();
        else SyncForm();
    }

    private void Reopen(string? path)
    {
        if (path is null) return;

        var mod = Results.FirstOrDefault(m => m.Files.Any(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase)));
        if (mod is null)
        {
            SelectedMod = null;
            SelectedEntry = null;
            return;
        }

        mod.LastFile = mod.Files.First(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (ReferenceEquals(SelectedMod, mod)) Load(mod.LastFile);
        else SelectedMod = mod;
    }

    private void OnFormEdited(ConfigOpenFile file)
    {
        if (ReferenceEquals(file, _current)) SetEditor(file.Text);
        FileChanged(file);
    }

    private void SetEditor(string text)
    {
        _syncingEditor = true;
        EditorText = text;
        _syncingEditor = false;
        IsDirty = _current?.IsDirty == true;
    }

    private void FileChanged(ConfigOpenFile file)
    {
        if (_mods.FirstOrDefault(m => m.Files.Contains(file.Entry)) is { } mod) Recount(mod);
        if (ReferenceEquals(file, _current)) IsDirty = file.IsDirty;

        RecountTotals();
    }

    private void Recount(ConfigModRow mod)
    {
        mod.ChangedCount = mod.Files.Sum(f => OpenedFile(f)?.ChangedCount ?? 0);
        mod.IsDirty = mod.Files.Any(f => OpenedFile(f)?.IsDirty == true);

        foreach (var tab in mod.Tabs) tab.IsDirty = OpenedFile(tab.Entry)?.IsDirty == true;
    }

    private void RecountTotals()
    {
        var files = _mods.SelectMany(CfgFiles).ToList();

        TotalChanged = _mods.Sum(m => m.ChangedCount);
        TotalKeybinds = files.Sum(f => f.Parsed!.Settings.Count(s => s.Kind == BepInExValueKind.Shortcut));
        Footer = Text(Strings.Configs_FooterFormat, _mods.Count, files.Sum(f => f.SettingCount), TotalChanged);

        OnPropertyChanged(nameof(TotalChanged));
        OnPropertyChanged(nameof(TotalKeybinds));
        OnPropertyChanged(nameof(ChangedViewLabel));
        OnPropertyChanged(nameof(KeybindsViewLabel));
        OnPropertyChanged(nameof(Footer));
        OnPropertyChanged(nameof(DirtyCount));
        OnPropertyChanged(nameof(HasDirty));
        OnPropertyChanged(nameof(DirtyLabel));
        NotifyActions();
    }

    private void NotifyActions()
    {
        SaveAllCommand.NotifyCanExecuteChanged();
        RevertAllCommand.NotifyCanExecuteChanged();
        ResetModCommand.NotifyCanExecuteChanged();
        ResetEverythingCommand.NotifyCanExecuteChanged();
        CopyChangesCommand.NotifyCanExecuteChanged();
    }

    private void MarkTabs()
    {
        if (SelectedMod is null) return;

        foreach (var tab in SelectedMod.Tabs) tab.IsSelected = ReferenceEquals(tab.Entry, SelectedEntry);
    }

    private void ShowList()
    {
        var sources = _mods
            .SelectMany(m => m.Files
                .Where(f => f.Entry.Format == ModConfigFormat.BepInExCfg)
                .Select(f => TryOpen(f) is { } file ? new ConfigFormSource(m, file) : null))
            .OfType<ConfigFormSource>()
            .ToList();

        if (ShowKeybindsView) Form.ShowKeybinds(sources);
        else Form.ShowChanged(sources);

        foreach (var mod in _mods) Recount(mod);
        RecountTotals();
    }

    private ConfigOpenFile Open(ConfigEntryViewModel entry)
    {
        if (_open.TryGetValue(entry.FullPath, out var file))
        {
            if (!file.IsDirty) file.Reread(ModConfigStore.Load(entry.FullPath));
            return file;
        }

        file = new ConfigOpenFile(entry, ModConfigStore.Load(entry.FullPath));
        _open[entry.FullPath] = file;
        return file;
    }

    private ConfigOpenFile? TryOpen(ConfigEntryViewModel entry)
    {
        try
        {
            return Open(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Configs", $"couldn't read {entry.FullPath}: {ex.Message}");
            return null;
        }
    }

    private ConfigOpenFile? OpenedFile(ConfigEntryViewModel entry) => _open.GetValueOrDefault(entry.FullPath);

    private IEnumerable<ConfigOpenFile> CfgFiles(ConfigModRow mod) =>
        mod.Files
            .Where(f => f.Entry.Format == ModConfigFormat.BepInExCfg)
            .Select(OpenedFile)
            .OfType<ConfigOpenFile>()
            .Where(f => f.IsCfg);

    private void Adopt(IReadOnlyList<ConfigOpenFile> fresh)
    {
        var kept = _open.Values.Where(f => f.IsDirty).ToDictionary(f => f.Entry.FullPath, StringComparer.OrdinalIgnoreCase);
        var entries = _all.ToDictionary(e => e.FullPath, StringComparer.OrdinalIgnoreCase);

        _open.Clear();

        foreach (var file in fresh) _open[file.Entry.FullPath] = file;

        foreach (var (path, file) in kept)
        {
            if (!entries.TryGetValue(path, out var entry)) continue;

            file.Entry = entry;
            _open[path] = file;
        }

        if (_current is not null)
            _current = _open.GetValueOrDefault(_current.Entry.FullPath);
    }

    private static List<ConfigOpenFile> ReadCfgFiles(IEnumerable<ConfigEntryViewModel> entries)
    {
        var files = new List<ConfigOpenFile>();

        foreach (var entry in entries.Where(e => e.Entry.Format == ModConfigFormat.BepInExCfg))
        {
            try
            {
                var file = new ConfigOpenFile(entry, ModConfigStore.Load(entry.FullPath));
                _ = file.Parsed;
                files.Add(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Configs", $"couldn't read {entry.FullPath}: {ex.Message}");
            }
        }

        return files;
    }

    private static List<ConfigModRow> ModRows(List<ConfigEntryViewModel> entries, List<InstalledModCardViewModel> cards)
    {
        var groups = new List<(object Key, InstalledModCardViewModel? Card, List<ConfigEntryViewModel> Files)>();

        foreach (var entry in entries)
        {
            var card = CardOf(entry, cards);
            object key = card is not null
                ? card
                : entry.Entry.ModName is { } name && entry.Source is ModConfigSource.Client or ModConfigSource.Server
                    ? name.ToLowerInvariant()
                    : entry.FullPath;

            var index = groups.FindIndex(g => g.Key.Equals(key));
            if (index >= 0) groups[index].Files.Add(entry);
            else groups.Add((key, card, [entry]));
        }

        return groups
            .Select(g =>
            {
                var files = g.Files
                    .OrderBy(f => f.Section.Rank)
                    .ThenBy(f => f.Entry.Format == ModConfigFormat.BepInExCfg ? 0 : 1)
                    .ThenBy(f => f.Subtitle, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new ConfigModRow(
                    g.Card?.DisplayTitle ?? files[0].Title,
                    files,
                    g.Card?.InstalledVersion,
                    g.Card?.Author,
                    g.Card?.Guid ?? files.Select(f => f.Entry.ModGuid).FirstOrDefault(id => id is not null));
            })
            .ToList();
    }

    private static InstalledModCardViewModel? CardOf(ConfigEntryViewModel entry, List<InstalledModCardViewModel> cards)
    {
        if (entry.Entry.ModName is not { } name) return null;

        var target = entry.Source switch
        {
            ModConfigSource.Client => InstalledModTarget.Client,
            ModConfigSource.Server => InstalledModTarget.Server,
            _ => (InstalledModTarget?)null,
        };

        if (target is null) return null;

        return cards
            .Where(c => c.Entries.Any(m => m.Target == target && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => c.IsAddon ? 1 : 0)
            .FirstOrDefault();
    }
}
