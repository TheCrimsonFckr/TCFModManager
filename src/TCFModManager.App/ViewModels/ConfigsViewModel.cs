using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// The Configs page: every config file the installed mods actually have, one rail row per mod, and an
// editor for the selected file. Edits are held per file (ConfigOpenFile) until Save or Save all.
//
// The list is grouped by where a file lives, because that is the difference that matters to whoever
// is editing it - a client mod's settings sit in the shared BepInEx\config folder and outlive the
// mod, a server mod's sit inside its own folder and travel with it. See ModConfigDiscovery for how
// each file is found and attributed.
//
// A .cfg file opens in a form built from BepInExConfigFile (OPEN-39), with the raw text one click
// away. JSON is edited as text and checked before it is written. Either way EditorText is the one
// copy of the file being edited: the form writes into it, and Save, Revert and the dirty check read it.
//
public sealed partial class ConfigsViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private List<ConfigEntryViewModel> _all = [];

    // Per-mod update policy, and what recent updates did to each file.
    private readonly ModConfigOptionsStore _options = new();
    private readonly ConfigUpdateLog _updates = new();

    // Guards the policy dropdown being set to match the newly selected file, so showing a stored
    // choice doesn't read as making one.
    private bool _showingPolicy;

    private bool _formStale = true;

    public ConfigsViewModel()
    {
        Form = new ConfigFormViewModel();
        UnsavedConfigEdits.Page = this;
        Form.FileEdited += (_, file) => OnFormEdited(file);

        SourceFilterOptions =
        [
            new ConfigSourceFilterItem(nameof(Strings.Filter_ConfigAll), ConfigSourceFilter.All),
            new ConfigSourceFilterItem(nameof(Strings.Filter_ConfigClient), ConfigSourceFilter.Client),
            new ConfigSourceFilterItem(nameof(Strings.Filter_ConfigServer), ConfigSourceFilter.Server),
            new ConfigSourceFilterItem(nameof(Strings.Filter_ConfigOther), ConfigSourceFilter.Other),
        ];

        _selectedSourceFilter = SourceFilterOptions[0];

        PolicyOptions =
        [
            new ConfigPolicyOption(nameof(Strings.ConfigPolicy_Merge), ModConfigPolicy.Merge),
            new ConfigPolicyOption(nameof(Strings.ConfigPolicy_KeepMine), ModConfigPolicy.KeepMine),
            new ConfigPolicyOption(nameof(Strings.ConfigPolicy_TakeNew), ModConfigPolicy.TakeNew),
        ];

        _selectedPolicyOption = PolicyOptions[0];
    }

    //
    // What an update does with this mod's config when the user has changed it. Per mod rather than per
    // file: a mod's configs are one decision, and the store is keyed by its folder.
    //
    public IReadOnlyList<ConfigPolicyOption> PolicyOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PolicyNote))]
    private ConfigPolicyOption _selectedPolicyOption;

    public string PolicyNote => ConfigUpdateWording.PolicyNote(SelectedPolicyOption.Value);

    public bool ShowPolicy => SelectedEntry?.CanSetPolicy == true;

    //
    // The places this mod keeps things that no convention would find, and the ones that only look like
    // config. Shown as chips under the policy, because they are per mod exactly as the policy is.
    //
    public ObservableCollection<ConfigLocationChip> Locations { get; } = [];

    public bool HasLocations => Locations.Count > 0;

    //
    // A folder inside the mod holding documents the user wrote - SVM's Presets. An update leaves it
    // alone; a removal rescues it.
    //
    [RelayCommand]
    private async Task AddUserDataFolderAsync()
    {
        if (ModFolder() is not ({ } modName, { } modFolder)) return;

        var dialog = new OpenFolderDialog
        {
            Title = Text(Strings.Configs_AddUserDataTitleFormat, modName),
            InitialDirectory = modFolder,
        };

        if (dialog.ShowDialog() != true) return;

        if (Inside(modFolder, dialog.FolderName) is not { } relative)
        {
            StatusMessage = Strings.Configs_FolderNotInsideMod;
            return;
        }

        // The guard rail D7 exists for: an opted-in folder is somebody's presets, not a database.
        var count = CountFiles(dialog.FolderName);
        if (count > ModConfigFiles.MaxUserDataFiles)
        {
            StatusMessage = Text(Strings.Configs_TooManyFilesFormat, relative, count);
            return;
        }

        var stored = _options.For(modName);
        _options.SetUserData(modName, [.. stored.UserData, relative]);
        await AfterLocationsChangedAsync(
            modName,
            Text(Strings.Configs_UserDataAddedFormat, relative, modName));
    }

    // A settings file the mod keeps somewhere nothing would look - SVM's Loader\loader.json.
    [RelayCommand]
    private async Task AddSettingsFileAsync()
    {
        if (ModFolder() is not ({ } modName, { } modFolder)) return;

        var dialog = new OpenFileDialog
        {
            Title = Text(Strings.Configs_AddSettingsFileTitleFormat, modName),
            InitialDirectory = modFolder,
            Filter = Strings.Configs_FileFilter,
        };

        if (dialog.ShowDialog() != true) return;

        if (Inside(modFolder, dialog.FileName) is not { } relative)
        {
            StatusMessage = Strings.Configs_FileNotInsideMod;
            return;
        }

        var stored = _options.For(modName);
        _options.SetSettings(modName, [.. stored.Settings, relative]);
        await AfterLocationsChangedAsync(modName, Text(Strings.Configs_SettingsAddedFormat, relative));
    }

    // The opposite case: a file that sits in a folder called "config" and is really data.
    [RelayCommand(CanExecute = nameof(ShowPolicy))]
    private async Task IgnoreSelectedFileAsync()
    {
        if (SelectedEntry is not { Entry.ModName: { } modName } entry) return;
        if (ModFolder() is not (_, { } modFolder)) return;
        if (Inside(modFolder, entry.FullPath) is not { } relative) return;

        var stored = _options.For(modName);
        _options.SetExclude(modName, [.. stored.Exclude, relative]);
        await AfterLocationsChangedAsync(modName, Text(Strings.Configs_ExcludedFormat, relative, modName));
    }

    [RelayCommand]
    private async Task RemoveLocationAsync(ConfigLocationChip? chip)
    {
        if (chip is null || SelectedEntry?.Entry.ModName is not { } modName) return;

        var stored = _options.For(modName);

        switch (chip.Kind)
        {
            case ConfigLocationKind.UserData:
                _options.SetUserData(modName, stored.UserData.Where(p => p != chip.Path));
                break;
            case ConfigLocationKind.Settings:
                _options.SetSettings(modName, stored.Settings.Where(p => p != chip.Path));
                break;
            default:
                _options.SetExclude(modName, stored.Exclude.Where(p => p != chip.Path));
                break;
        }

        await AfterLocationsChangedAsync(modName, Text(Strings.Configs_LocationRemovedFormat, chip.Path));
    }

    //
    // Every one of these changes what counts as a config, so the list is rebuilt from disk rather than
    // patched - which is also the only honest way to show a file that has just started counting.
    //
    private async Task AfterLocationsChangedAsync(string modName, string message)
    {
        ShowLocations(SelectedEntry);
        AppLog.Info("Configs", $"{modName} config locations changed");

        await ScanAsync();
        StatusMessage = message;
    }

    private void ShowLocations(ConfigEntryViewModel? entry)
    {
        Locations.Clear();

        if (entry is { CanSetPolicy: true, Entry.ModName: { } modName })
        {
            var stored = _options.For(modName);

            foreach (var path in stored.UserData) Locations.Add(new ConfigLocationChip(ConfigLocationKind.UserData, path));
            foreach (var path in stored.Settings) Locations.Add(new ConfigLocationChip(ConfigLocationKind.Settings, path));
            foreach (var path in stored.Exclude) Locations.Add(new ConfigLocationChip(ConfigLocationKind.Exclude, path));
        }

        OnPropertyChanged(nameof(HasLocations));
    }

    //
    // The selected file's mod: its folder name, and the full path of the folder itself, found by
    // climbing out of the file rather than rebuilt from the install path - the server root is nested
    // differently on different installs and the file already knows where it is.
    //
    private (string ModName, string Folder)? ModFolder()
    {
        if (SelectedEntry is not { CanSetPolicy: true, Entry.ModName: { } modName }) return null;

        for (var dir = Path.GetDirectoryName(SelectedEntry.FullPath); dir is not null; dir = Path.GetDirectoryName(dir))
            if (string.Equals(Path.GetFileName(dir), modName, StringComparison.OrdinalIgnoreCase))
                return (modName, dir);

        return null;
    }

    // The path of something inside the mod, relative to the mod's folder - or null when it isn't.
    private static string? Inside(string modFolder, string path)
    {
        var relative = Path.GetRelativePath(modFolder, path).Replace('\\', '/');

        return relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : ModConfigPaths.Normalise(relative);
    }

    private static int CountFiles(string folder)
    {
        try
        {
            return Directory
                .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Take(ModConfigFiles.MaxUserDataFiles + 1)
                .Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    partial void OnSelectedPolicyOptionChanged(ConfigPolicyOption value)
    {
        if (_showingPolicy) return;
        if (SelectedEntry is not { CanSetPolicy: true, Entry.ModName: { } modName }) return;

        _options.SetPolicy(modName, value.Value);

        // A whole sentence per policy rather than the dropdown's own label lower-cased and dropped
        // into one: a label is a noun phrase in English and need not be one in another language,
        // and lower-casing a word is not a translation.
        StatusMessage = Text(
            value.Value switch
            {
                ModConfigPolicy.KeepMine => Strings.Configs_PolicyKeepMineFormat,
                ModConfigPolicy.TakeNew => Strings.Configs_PolicyTakeNewFormat,
                _ => Strings.Configs_PolicyMergeFormat,
            },
            modName);
        AppLog.Info("Configs", $"{modName} update policy set to {value.Value}");
    }

    //
    // The filtered list, flat and pre-sorted by section then title. The view groups it with a
    // CollectionViewSource rather than it being handed over already nested, so the whole list stays
    // one ListBox with one selection - see ConfigSectionHeader.
    //
    public ObservableCollection<ConfigModRow> Results { get; } = [];

    public IReadOnlyList<ConfigSourceFilterItem> SourceFilterOptions { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = Strings.Configs_StatusScanning;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ConfigSourceFilterItem _selectedSourceFilter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowPolicy))]
    [NotifyCanExecuteChangedFor(nameof(IgnoreSelectedFileCommand))]
    private ConfigEntryViewModel? _selectedEntry;

    public bool HasSelection => SelectedEntry is not null;

    public bool ShowEditor => ShowListView || (ShowModView && HasSelection);

    public bool ShowEmptyState => ShowModView && !HasSelection;

    public ConfigFormViewModel Form { get; }

    [ObservableProperty]
    private bool _showRaw;

    public bool CanUseForm => ShowModView && SelectedEntry?.Entry.Format == ModConfigFormat.BepInExCfg && _current is not null;

    public bool ShowFormView => CanUseForm && !ShowRaw;

    public bool ShowRawView => ShowModView && !ShowFormView;

    public bool ShowFormList => ShowFormView || ShowListView;

    partial void OnShowRawChanged(bool value) => SyncForm();

    [RelayCommand]
    private void UseForm() => ShowRaw = false;

    [RelayCommand]
    private void UseRaw() => ShowRaw = true;

    private void SyncForm()
    {
        OnPropertyChanged(nameof(CanUseForm));
        OnPropertyChanged(nameof(ShowFormView));
        OnPropertyChanged(nameof(ShowRawView));
        OnPropertyChanged(nameof(ShowFormList));
        OnPropertyChanged(nameof(ShowToolbar));
        NotifyActions();

        if (!ShowModView) return;

        if (!CanUseForm)
        {
            Form.Clear();
            _formStale = true;
            return;
        }

        if (!ShowFormView || !_formStale || _current is null) return;

        Form.ShowFile(_current);
        _formStale = false;
    }

    // The editor's contents. Bound two-way, so every keystroke re-checks whether anything differs
    // from what was read off disk.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private string _editorText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private bool _isDirty;

    // Why the last save was refused, or why the file couldn't be read. Cleared on every edit.
    [ObservableProperty]
    private string? _editorError;

    //
    // Shown while SPT is running. A warning rather than a block: the file is perfectly writable, but
    // BepInEx writes its whole .cfg back out when the game closes, so an edit made now is likely to
    // be overwritten - and a server mod reads its config at startup, so an edit won't take effect
    // until the server restarts either way.
    //
    [ObservableProperty]
    private string? _runningWarning;

    [ObservableProperty]
    private bool _hasResults;

    //
    // The warning at the top of the page. Closable, and deliberately not remembered anywhere: the
    // page is built once per launch (its nav item caches it), so dismissing it lasts for the session
    // and it is back the next time the app opens. A disclaimer nobody ever sees again after the
    // first click isn't one.
    //
    [ObservableProperty]
    private bool _showDisclaimer = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedSourceFilterChanged(ConfigSourceFilterItem value) => ApplyFilter();

    partial void OnEditorTextChanged(string value)
    {
        EditorError = null;
        if (_syncingEditor || _current is null) return;

        _formStale = true;
        _current.Text = value;
        FileChanged(_current);
    }

    partial void OnSelectedEntryChanged(ConfigEntryViewModel? value)
    {
        OnPropertyChanged(nameof(ShowEditor));
        OnPropertyChanged(nameof(ShowEmptyState));

        if (value is not null && SelectedMod is { } mod && mod.Files.Contains(value)) mod.LastFile = value;
        MarkTabs();

        Load(value);
        ShowStoredPolicy(value);
        ShowLocations(value);
    }

    // Puts the dropdown on this mod's stored choice without that counting as a change.
    private void ShowStoredPolicy(ConfigEntryViewModel? entry)
    {
        if (entry is not { CanSetPolicy: true, Entry.ModName: { } modName }) return;

        var stored = _options.For(modName).Policy;

        _showingPolicy = true;
        SelectedPolicyOption = PolicyOptions.FirstOrDefault(o => o.Value == stored) ?? PolicyOptions[0];
        _showingPolicy = false;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _all = [];
            ApplyFilter();
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        IsBusy = true;
        try
        {
            var catalog = AppServices.ModCache.AllMods;
            var addons = AppServices.Addons.AllAddons;
            var records = AppServices.InstallManifest.Load().Mods;
            var effective = _options.Effective();
            var history = _updates.Load().Reports;

            var scan = await Task.Run(() =>
            {
                var installed = InstalledScan.Run(installPath);
                var entries = ModConfigDiscovery.Find(installPath, installed, effective)
                    .Select(e => Row(e, history))
                    .ToList();
                var cards = InstalledModCardViewModel.BuildFrom(installed, catalog, null, records, addons);

                return (Entries: entries, Mods: ModRows(entries, cards), Files: ReadCfgFiles(entries));
            });

            var reopen = SelectedEntry?.FullPath;

            _all = scan.Entries;
            Adopt(scan.Files);
            _mods = scan.Mods;
            foreach (var mod in _mods) Recount(mod);
            RecountTotals();

            ApplyFilter();
            RefreshRunningWarning();

            if (ShowListView) ShowList();
            else Reopen(reopen);

            var mods = _all
                .Where(e => e.Entry.ModName is not null)
                .Select(e => e.Entry.ModName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            StatusMessage = _all.Count == 0
                ? Strings.Configs_NoneFound
                : Text(Strings.Configs_CountFormat, _all.Count, mods);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Text(Strings.Configs_ReadFailedFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    //
    // One row, carrying what the most recent recorded update did to that exact file so the page can
    // say it days later - which is the other half of the report the Downloads queue shows once.
    //
    private static ConfigEntryViewModel Row(ModConfigEntry entry, List<ConfigUpdateReport> history)
    {
        foreach (var report in history)
        {
            var outcome = report.Files.FirstOrDefault(f =>
                string.Equals(f.Path, entry.DisplayPath, StringComparison.OrdinalIgnoreCase));

            if (outcome is not null)
                return new ConfigEntryViewModel { Entry = entry, LastUpdate = report, LastUpdateOutcome = outcome };
        }

        return new ConfigEntryViewModel { Entry = entry };
    }

    // Rebuilds the grouped list from the search box and the source dropdown.
    private void ApplyFilter()
    {
        var term = SearchText.Trim();

        var filtered = _mods
            .Where(m => m.Files.Any(f => SelectedSourceFilter.Matches(f.Source)))
            .Where(m => term.Length == 0 || m.MatchesName(term) || m.Files.Any(f => OpenedFile(f)?.HasSetting(term) == true))
            .OrderBy(m => m.Section.Rank)
            .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Results.Clear();
        foreach (var mod in filtered) Results.Add(mod);

        HasResults = filtered.Count > 0;

        if (SelectedMod is not null && !filtered.Contains(SelectedMod) && !IsDirty)
        {
            SelectedMod = null;
            SelectedEntry = null;
        }
    }

    private void Load(ConfigEntryViewModel? entry)
    {
        EditorError = null;
        _current = null;

        if (entry is not null)
        {
            try
            {
                _current = Open(entry);
                RefreshRunningWarning();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                EditorError = Text(Strings.Configs_FileReadFailedFormat, ex.Message);
            }

            if (_current is not null) FileChanged(_current);
        }

        SetEditor(_current?.Text ?? string.Empty);
        _formStale = true;
        SyncForm();
    }

    private bool CanEdit => SelectedEntry is not null && _current is not null;

    private bool CanSave => CanEdit && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => SaveInternal(overwriteChangesOnDisk: false);

    private void SaveInternal(bool overwriteChangesOnDisk)
    {
        var entry = SelectedEntry;
        if (entry is null || _current is null) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        EditorError = null;

        var result = ModConfigStore.Save(
            installPath,
            entry.FullPath,
            _current.Text,
            _current.Loaded,
            DateTimeOffset.Now,
            overwriteChangesOnDisk);

        switch (result.Outcome)
        {
            case ModConfigSaveOutcome.Saved:
                _current.Saved(result.Saved!);
                FileChanged(_current);
                StatusMessage = result.BackupPath is null
                    ? Text(Strings.Configs_SavedFormat, entry.FileName)
                    : Text(
                        Strings.Configs_SavedBackupFormat,
                        entry.FileName,
                        ModConfigStore.BackupDisplayPath);
                break;

            case ModConfigSaveOutcome.Invalid:
                EditorError = result.Error;
                break;

            case ModConfigSaveOutcome.ChangedOnDisk:
                HandleChangedOnDisk(entry);
                break;

            case ModConfigSaveOutcome.BackupFailed:
                EditorError = Text(Strings.Configs_BackupFailedFormat, entry.FileName, result.Error);
                break;

            default:
                EditorError = result.Error;
                break;
        }
    }

    //
    // The file changed underneath the editor - the game wrote it, or it was edited elsewhere. Three
    // ways out, and the default is the safe one: nothing has been written yet at this point.
    //
    private void HandleChangedOnDisk(ConfigEntryViewModel entry)
    {
        var answer = AskWindow.Show(new AskRequest
        {
            Title = Strings.Configs_ChangedOnDiskTitle,
            Lead = Text(Strings.Configs_ChangedOnDiskLeadFormat, entry.FileName),
            Detail = [Strings.Configs_ChangedOnDiskBackup],
            Footnote = ModConfigStore.BackupDisplayPath,
            First = Strings.Configs_SaveOverIt,
            Second = Strings.Configs_ReloadLoseChanges,
            Default = AskChoice.Cancel,
        });

        switch (answer)
        {
            case AskChoice.First:
                SaveInternal(overwriteChangesOnDisk: true);
                break;

            case AskChoice.Second:
                // Copied aside before it is thrown away, so a reload can't lose anything either.
                var installPath = AppServices.SptEnvironment.InstallPath;
                if (!string.IsNullOrWhiteSpace(installPath))
                    ModConfigStore.Backup(installPath, entry.FullPath, DateTimeOffset.Now);

                _current?.Revert();
                Load(entry);
                StatusMessage = Text(Strings.Configs_ReloadedFormat, entry.FileName);
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Revert()
    {
        if (_current is null) return;

        _current.Revert();
        SetEditor(_current.Text);
        FileChanged(_current);
        EditorError = null;
        _formStale = true;
        SyncForm();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFolder()
    {
        if (SelectedEntry is null) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{SelectedEntry.FullPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Configs", $"couldn't open the folder for {SelectedEntry.FullPath}: {ex.Message}");
            StatusMessage = Strings.Common_FolderOpenFailed;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyPath()
    {
        if (SelectedEntry is null) return;

        try
        {
            Clipboard.SetText(SelectedEntry.FullPath);
            StatusMessage = Strings.Configs_PathCopied;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The clipboard is held by another process often enough to be worth not crashing over.
            StatusMessage = Strings.Configs_PathCopyFailed;
        }
    }

    //
    // Reuses the same running-process check the install and disable paths use, rather than adding
    // another one. The wording differs from theirs on purpose: there it is a blocker, here it is a
    // note about the edit likely being undone.
    //
    private void RefreshRunningWarning()
    {
        if (ModInstallService.RunningBlockers(AppServices.SptEnvironment.InstallPath) is not { Count: > 0 } blockers)
        {
            RunningWarning = null;
            return;
        }

        // One whole sentence per count: "SPT.Server.exe and EscapeFromTarkov.exe is running" never
        // agreed in English either.
        var bepInEx = SelectedEntry?.Entry.Format == ModConfigFormat.BepInExCfg;

        RunningWarning = bepInEx
            ? Strings.Configs_RunningBepInEx(blockers.Count, TextLists.Join(blockers))
            : Strings.Configs_RunningServer(blockers.Count, TextLists.Join(blockers));
    }
}
