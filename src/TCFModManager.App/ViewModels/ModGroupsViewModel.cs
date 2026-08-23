using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Backs ModGroupsWindow - a standalone window (opened from the Installed page, but independent of
// InstalledViewModel) for sorting installed mods into user-defined groups purely for the player's
// own organization, MO2-separator style. Scans disk itself on load rather than being handed
// InstalledViewModel's results, so opening it never depends on - or changes - the Installed page.
//
public partial class ModGroupsViewModel : ObservableObject
{
    private readonly ModGroupStore _store = AppServices.ModGroups;
    private List<InstalledModCardViewModel> _allMods = [];

    public ObservableCollection<ModGroupSectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => LoadAsync();

    public async Task LoadAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _allMods = [];
            RebuildSections();
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        IsBusy = true;
        try
        {
            await AppServices.ModCache.EnsureLoadedAsync();

            var scanned = await Task.Run(() => InstalledModScanner.Scan(installPath));
            var installRecords = AppServices.InstallManifest.Load().Mods;

            _allMods = InstalledModCardViewModel.BuildFrom(
                    scanned, AppServices.ModCache.AllMods, AppServices.SptEnvironment.InstalledVersion, installRecords)
                .OrderBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            RebuildSections();
            StatusMessage = _allMods.Count == 0
                ? $"No mods found under \"{installPath}\"."
                : $"{_allMods.Count} mod(s) loaded. Drag a mod onto a group to sort it.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Rebuilds Sections from the current store contents and _allMods. Called after every mutation
    // (add/rename/delete/move/assign) rather than patched incrementally - this window's lists are
    // small enough that a full rebuild is simpler and can't drift out of sync with the store.
    private void RebuildSections()
    {
        var data = _store.Load();

        Sections.Clear();
        foreach (var group in data.Groups.OrderBy(g => g.SortOrder))
            Sections.Add(ModGroupSectionViewModel.FromGroup(group));

        var ungrouped = ModGroupSectionViewModel.Ungrouped();
        Sections.Add(ungrouped);

        var byId = Sections.Where(s => s.GroupId is not null).ToDictionary(s => s.GroupId!.Value);

        foreach (var mod in _allMods)
        {
            var key = ModGroupStore.KeyFor(mod.Name);
            var section = data.Assignments.TryGetValue(key, out var groupId) && byId.TryGetValue(groupId, out var found)
                ? found
                : ungrouped;

            section.Items.Add(mod);
        }
    }

    [RelayCommand]
    private void AddGroup()
    {
        var name = NewGroupName.Trim();
        if (name.Length == 0) return;

        _store.AddGroup(name);
        NewGroupName = string.Empty;
        RebuildSections();
    }

    [RelayCommand]
    private void BeginRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        section.IsEditing = true;
    }

    [RelayCommand]
    private void CommitRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        section.IsEditing = false;

        var name = section.Name.Trim();
        if (name.Length == 0)
        {
            // Blank name isn't allowed - reload the real name from disk rather than saving it.
            RebuildSections();
            return;
        }

        _store.RenameGroup(section.GroupId!.Value, name);
        RebuildSections();
    }

    // Discards an in-progress rename without saving - the edited Name only lives on the section VM
    // in memory, so reloading fresh ones from the store (still holding the old name) undoes it.
    [RelayCommand]
    private void CancelRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        RebuildSections();
    }

    [RelayCommand]
    private void DeleteGroup(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;

        var message = section.Items.Count == 0
            ? $"Delete \"{section.Name}\"?"
            : $"Delete \"{section.Name}\"? Its {section.CountLabel} move back to Ungrouped.";

        if (MessageBox.Show(message, "Delete group?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _store.DeleteGroup(section.GroupId!.Value);
        RebuildSections();
    }

    [RelayCommand]
    private void ToggleCollapsed(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;

        section.IsCollapsed = !section.IsCollapsed;
        _store.SetCollapsed(section.GroupId!.Value, section.IsCollapsed);
    }

    [RelayCommand]
    private void MoveUp(ModGroupSectionViewModel? section) => Move(section, -1);

    [RelayCommand]
    private void MoveDown(ModGroupSectionViewModel? section) => Move(section, 1);

    private void Move(ModGroupSectionViewModel? section, int direction)
    {
        if (section is not { IsRealGroup: true }) return;

        _store.Move(section.GroupId!.Value, direction);
        RebuildSections();
    }

    // Called by the window's drag-drop code-behind when a mod card is dropped on a section
    // (groupId null for the Ungrouped bucket).
    public void MoveModToGroup(InstalledModCardViewModel mod, Guid? groupId)
    {
        _store.AssignMod(mod.Name, groupId);
        RebuildSections();
    }
}
