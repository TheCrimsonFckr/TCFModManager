using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    public ObservableCollection<ModGroupSectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    public List<GroupSortItem> GroupSortOptions { get; } =
    [
        new(nameof(Strings.Sort_GroupsManual), GroupSortOption.Manual),
        new(nameof(Strings.Sort_GroupNameAscending), GroupSortOption.NameAscending),
        new(nameof(Strings.Sort_GroupNameDescending), GroupSortOption.NameDescending),
    ];

    [ObservableProperty]
    private GroupSortItem _selectedGroupSortOption;

    /// <summary>Whether the group header's move-up/move-down buttons should show - only while
    /// groups are in their manual order; an alphabetical group sort would just override them.</summary>
    public bool CanReorderGroups => SelectedGroupSortOption.Value == GroupSortOption.Manual;

    //
    // Copies each mod's group assignment onto its card and rebuilds the Group dropdown, from the
    // one place groups are stored. Run after every scan and every group change, so the cards, the
    // filter and the store can't drift apart. The current filter selection is carried across by
    // value rather than by instance, since the dropdown's items are replaced each time.
    //
    // OPEN-31 R6: an addon card seen for the first time starts in its parent's group.
    private void SeedAddonGroups()
    {
        var parents = _all
            .Where(c => c is { IsAddon: false, ModId: not null })
            .GroupBy(c => c.ModId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var pairs = _all
            .Where(c => c.IsAddon && c.ParentModId is { } p && parents.ContainsKey(p))
            .Select(c => (c.Name, parents[c.ParentModId!.Value].Name))
            .ToList();

        if (pairs.Count > 0) AppServices.ModGroups.SeedAddons(pairs);
    }

    private void RefreshGroups()
    {
        var data = AppServices.ModGroups.Load();
        var namesById = data.Groups.ToDictionary(g => g.Id, g => g.Name);

        foreach (var card in _all)
        {
            // An assignment pointing at a group that's since been deleted reads as ungrouped, the
            // same way RebuildSections already treats it.
            Guid? assigned = data.Assignments.TryGetValue(ModGroupStore.KeyFor(card.Name), out var id)
                && namesById.ContainsKey(id)
                ? id
                : null;

            card.GroupId = assigned;
            card.GroupName = assigned is { } value ? namesById[value] : null;
        }

        // Same as the category list above: the saved default the first time, the current selection
        // every time after.
        var previous = _groupDefaultApplied ? SelectedGroupFilter : DefaultGroupFilter();
        _groupDefaultApplied = true;

        _suppressAutoApplyFilter = true;
        try
        {
            GroupFilterOptions.Clear();
            GroupFilterOptions.Add(GroupFilterItem.All);
            GroupFilterOptions.Add(GroupFilterItem.Ungrouped);

            foreach (var group in data.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
                GroupFilterOptions.Add(new GroupFilterItem(group.Name, group.Id, allGroups: false));

            SelectedGroupFilter = GroupFilterOptions.FirstOrDefault(o => o.SameAs(previous)) ?? GroupFilterItem.All;
        }
        finally
        {
            _suppressAutoApplyFilter = false;
        }
    }

    // Everything that has to happen after groups are added, renamed, deleted, reordered, or a mod
    // is moved between them.
    private void GroupsChanged()
    {
        RefreshGroups();
        ApplyFilter();
        RefreshActiveView(CurrentPage);
    }

    // Rebuilds Sections from the current group store contents and _filtered (so group view honors
    // the same search/update-status/Fika/ads/AI filters and sort as the flat grid) - called after
    // every filter/sort change and every group mutation (add/rename/delete/move/assign) rather than
    // patched incrementally; Installed's mod counts are small enough that a full rebuild is simpler
    // and can't drift out of sync with the store.
    private void RebuildSections()
    {
        var data = AppServices.ModGroups.Load();

        IEnumerable<ModGroup> ordered = SelectedGroupSortOption.Value switch
        {
            GroupSortOption.NameAscending => data.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            GroupSortOption.NameDescending => data.Groups.OrderByDescending(g => g.Name, StringComparer.OrdinalIgnoreCase),
            _ => data.Groups.OrderBy(g => g.SortOrder),
        };

        // Built in full off to the side, then handed over a couple of sections per frame - every
        // section is a new object, so every one needs a new container anyway. See GradualFill.
        var sections = ordered.Select(ModGroupSectionViewModel.FromGroup).ToList();

        var ungrouped = ModGroupSectionViewModel.Ungrouped();
        sections.Add(ungrouped);

        var byId = sections.Where(s => s.GroupId is not null).ToDictionary(s => s.GroupId!.Value);

        foreach (var mod in _filtered)
        {
            var key = ModGroupStore.KeyFor(mod.Name);
            var section = data.Assignments.TryGetValue(key, out var groupId) && byId.TryGetValue(groupId, out var found)
                ? found
                : ungrouped;

            section.Items.Add(mod);
        }

        _sectionsFill.Apply(Sections, sections);

        _sectionsDirty = false;
    }

    [RelayCommand]
    private void AddGroup()
    {
        var name = NewGroupName.Trim();
        if (name.Length == 0) return;

        AppServices.ModGroups.AddGroup(name);
        NewGroupName = string.Empty;
        GroupsChanged();
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
            GroupsChanged();
            return;
        }

        AppServices.ModGroups.RenameGroup(section.GroupId!.Value, name);
        GroupsChanged();
    }

    // Discards an in-progress rename without saving - the edited Name only lives on the section VM
    // in memory, so reloading fresh ones from the store (still holding the old name) undoes it.
    [RelayCommand]
    private void CancelRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        GroupsChanged();
    }

    [RelayCommand]
    private void DeleteGroup(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;

        var message = section.Items.Count == 0
            ? Text(Strings.Installed_DeleteGroupEmptyFormat, section.Name)
            : Strings.Installed_DeleteGroup(
                section.Items.Count, section.Name, section.Items.Count);

        if (MessageBox.Show(
                message,
                Strings.Installed_DeleteGroupTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        AppServices.ModGroups.DeleteGroup(section.GroupId!.Value);
        GroupsChanged();
    }

    [RelayCommand]
    private void ToggleCollapsed(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;

        section.IsCollapsed = !section.IsCollapsed;
        AppServices.ModGroups.SetCollapsed(section.GroupId!.Value, section.IsCollapsed);
    }

    [RelayCommand]
    private void MoveGroupUp(ModGroupSectionViewModel? section) => MoveGroup(section, -1);

    [RelayCommand]
    private void MoveGroupDown(ModGroupSectionViewModel? section) => MoveGroup(section, 1);

    private void MoveGroup(ModGroupSectionViewModel? section, int direction)
    {
        if (section is not { IsRealGroup: true }) return;

        AppServices.ModGroups.Move(section.GroupId!.Value, direction);
        GroupsChanged();
    }

    // Called by InstalledPage's drag-drop code-behind when a mod card is dropped on a section
    // (groupId null for the Ungrouped bucket).
    public void MoveModToGroup(InstalledModCardViewModel mod, Guid? groupId)
    {
        AppServices.ModGroups.AssignMod(mod.Name, groupId);
        GroupsChanged();
    }
}
