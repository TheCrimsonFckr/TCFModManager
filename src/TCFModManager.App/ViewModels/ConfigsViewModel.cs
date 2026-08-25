using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// The Configs page: every config file the installed mods actually have, and an editor for the
// selected one.
//
// The list is grouped by where a file lives, because that is the difference that matters to whoever
// is editing it - a client mod's settings sit in the shared BepInEx\config folder and outlive the
// mod, a server mod's sit inside its own folder and travel with it. See ModConfigDiscovery for how
// each file is found and attributed.
//
// This is the raw-text stage: both formats are edited as text, with JSON checked before it is
// written. The generated form for .cfg files comes next, on top of BepInExConfigFile, which already
// parses everything it needs.
//
public sealed partial class ConfigsViewModel : ObservableObject
{
    private List<ConfigEntryViewModel> _all = [];

    // The file as it was last read from disk. Carries the write time a save is checked against and
    // the byte order mark to reproduce, and is what Revert and the dirty check compare with.
    private ModConfigDocument? _loaded;

    // The entry _loaded belongs to, which is not always SelectedEntry - the selection moves first
    // and the load follows, and a discarded prompt puts the selection back.
    private ConfigEntryViewModel? _loadedEntry;

    // Guards the selection being put back after the user declines to discard their edits, so the
    // change that restores it doesn't run the prompt a second time.
    private bool _restoringSelection;

    public ConfigsViewModel()
    {
        SourceFilterOptions =
        [
            new ConfigSourceFilterItem("All", ConfigSourceFilter.All),
            new ConfigSourceFilterItem("Client only", ConfigSourceFilter.Client),
            new ConfigSourceFilterItem("Server only", ConfigSourceFilter.Server),
            new ConfigSourceFilterItem("Not a mod's", ConfigSourceFilter.Other),
        ];

        _selectedSourceFilter = SourceFilterOptions[0];
    }

    //
    // The filtered list, flat and pre-sorted by section then title. The view groups it with a
    // CollectionViewSource rather than it being handed over already nested, so the whole list stays
    // one ListBox with one selection - see ConfigSectionHeader.
    //
    public ObservableCollection<ConfigEntryViewModel> Results { get; } = [];

    public IReadOnlyList<ConfigSourceFilterItem> SourceFilterOptions { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Scanning for mod configs...";

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
    private ConfigEntryViewModel? _selectedEntry;

    public bool HasSelection => SelectedEntry is not null;

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
        IsDirty = _loaded is not null && !string.Equals(value, _loaded.Text, StringComparison.Ordinal);
    }

    partial void OnSelectedEntryChanged(ConfigEntryViewModel? value)
    {
        if (_restoringSelection) return;

        // Moving away from an edited file would drop the edit silently, so it is offered back first.
        if (IsDirty && _loadedEntry is not null && !ReferenceEquals(_loadedEntry, value))
        {
            var keep = MessageBox.Show(
                $"Discard your unsaved changes to {_loadedEntry.FileName}?",
                "Unsaved changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.No;

            if (keep)
            {
                _restoringSelection = true;
                SelectedEntry = _loadedEntry;
                _restoringSelection = false;
                return;
            }
        }

        Load(value);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _all = [];
            ApplyFilter();
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        IsBusy = true;
        try
        {
            // Walking BepInEx\config and every server mod's folder is disk work, and the scan it
            // starts from is the same one the Installed page runs - neither belongs on the UI thread.
            var entries = await Task.Run(() =>
            {
                var installed = InstalledModScanner.Scan(installPath);
                return ModConfigDiscovery.Find(installPath, installed);
            });

            _all = entries.Select(e => new ConfigEntryViewModel { Entry = e }).ToList();

            ApplyFilter();
            RefreshRunningWarning();

            var mods = _all
                .Where(e => e.Entry.ModName is not null)
                .Select(e => e.Entry.ModName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            StatusMessage = _all.Count == 0
                ? "No mod configs found. Plenty of plugins only write one the first time the game runs."
                : $"{_all.Count} config file(s) across {mods} mod(s).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Couldn't read the install: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Rebuilds the grouped list from the search box and the source dropdown.
    private void ApplyFilter()
    {
        var term = SearchText.Trim();

        var filtered = _all
            .Where(e => SelectedSourceFilter.Matches(e.Source))
            .Where(e => term.Length == 0 || e.MatchesSearch(term))
            .OrderBy(e => e.Section.Rank)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Subtitle, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Results.Clear();
        foreach (var entry in filtered) Results.Add(entry);

        HasResults = filtered.Count > 0;

        // A filter that hides the open file leaves the editor showing something the list no longer
        // offers, so the selection goes with it.
        if (SelectedEntry is not null && !filtered.Contains(SelectedEntry) && !IsDirty) SelectedEntry = null;
    }

    private void Load(ConfigEntryViewModel? entry)
    {
        EditorError = null;
        _loadedEntry = entry;

        if (entry is null)
        {
            _loaded = null;
            EditorText = string.Empty;
            IsDirty = false;
            return;
        }

        try
        {
            _loaded = ModConfigStore.Load(entry.FullPath);
            EditorText = _loaded.Text;
            IsDirty = false;
            RefreshRunningWarning();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _loaded = null;
            EditorText = string.Empty;
            IsDirty = false;
            EditorError = $"Couldn't read this file: {ex.Message}";
        }
    }

    private bool CanEdit => SelectedEntry is not null && _loaded is not null;

    private bool CanSave => CanEdit && IsDirty;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => SaveInternal(overwriteChangesOnDisk: false);

    private void SaveInternal(bool overwriteChangesOnDisk)
    {
        var entry = SelectedEntry;
        if (entry is null || _loaded is null) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        EditorError = null;

        var result = ModConfigStore.Save(
            installPath,
            entry.FullPath,
            EditorText,
            _loaded,
            DateTimeOffset.Now,
            overwriteChangesOnDisk);

        switch (result.Outcome)
        {
            case ModConfigSaveOutcome.Saved:
                _loaded = result.Saved;
                IsDirty = false;
                StatusMessage = result.BackupPath is null
                    ? $"Saved {entry.FileName}."
                    : $"Saved {entry.FileName}. The previous version is in {ModConfigStore.BackupDisplayPath}.";
                break;

            case ModConfigSaveOutcome.Invalid:
                EditorError = result.Error;
                break;

            case ModConfigSaveOutcome.ChangedOnDisk:
                HandleChangedOnDisk(entry);
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
        var answer = MessageBox.Show(
            $"{entry.FileName} has changed on disk since you opened it here.\n\n" +
            "Yes  -  overwrite it with what's in the editor\n" +
            "No  -  reload it and lose your changes\n" +
            "Cancel  -  leave everything as it is\n\n" +
            $"Whichever you pick, the version currently on disk is copied into {ModConfigStore.BackupDisplayPath} first.",
            "File changed on disk",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        switch (answer)
        {
            case MessageBoxResult.Yes:
                SaveInternal(overwriteChangesOnDisk: true);
                break;

            case MessageBoxResult.No:
                // Copied aside before it is thrown away, so a reload can't lose anything either.
                var installPath = AppServices.SptEnvironment.InstallPath;
                if (!string.IsNullOrWhiteSpace(installPath))
                    ModConfigStore.Backup(installPath, entry.FullPath, DateTimeOffset.Now);

                Load(entry);
                StatusMessage = $"Reloaded {entry.FileName} from disk.";
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Revert()
    {
        if (_loaded is null) return;

        EditorText = _loaded.Text;
        IsDirty = false;
        EditorError = null;
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
            StatusMessage = "Couldn't open that folder.";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyPath()
    {
        if (SelectedEntry is null) return;

        try
        {
            Clipboard.SetText(SelectedEntry.FullPath);
            StatusMessage = "Path copied.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // The clipboard is held by another process often enough to be worth not crashing over.
            StatusMessage = "Couldn't copy the path - something else is holding the clipboard.";
        }
    }

    //
    // Reuses the same running-process check the install and disable paths use, rather than adding
    // another one. The wording differs from theirs on purpose: there it is a blocker, here it is a
    // note about the edit likely being undone.
    //
    private void RefreshRunningWarning()
    {
        if (ModInstallService.RunningBlockers() is not { Count: > 0 } blockers)
        {
            RunningWarning = null;
            return;
        }

        var running = string.Join(" and ", blockers);

        RunningWarning = SelectedEntry?.Entry.Format == ModConfigFormat.BepInExCfg
            ? $"{running} is running. BepInEx writes its config files back out when the game closes, which would undo anything saved here - close it first."
            : $"{running} is running. A server mod reads its config when the server starts, so anything saved here takes effect on the next restart.";
    }
}
