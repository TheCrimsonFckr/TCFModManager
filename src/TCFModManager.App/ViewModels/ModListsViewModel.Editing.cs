using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class ModListsViewModel
{
    //
    // Adds mods to the selected list by hand, from what is installed and from the catalog.
    //
    // Edits the panel, not the file: nothing is written until Save, and nothing on disk moves until
    // Apply. Those are three separate steps on purpose.
    //
    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private async Task AddModsAsync()
    {
        if (Selected is not { } row) return;

        await RunAsync(async () =>
        {
            var options = await _service.AddOptionsAsync();

            if (options.Installed.Count == 0 && options.Catalog.Count == 0)
            {
                StatusMessage = Strings.ModLists_NothingToAddFrom;
                return;
            }

            // The picker is handed what the panel holds, unsaved edits included, so a mod added a
            // minute ago and not yet saved still shows there as already on the list.
            var chosen = ModListAddModWindow.Pick(row.Name, [.. Entries.Select(e => e.Entry)], options);
            if (chosen.Count == 0) return;

            var added = 0;

            foreach (var entry in chosen)
            {
                if (Entries.Any(e => ModListEntries.SameMod(e.Entry, entry))) continue;

                Entries.Add(Row(entry));
                added++;
            }

            if (added == 0)
            {
                StatusMessage = Strings.ModLists_NothingAdded;
                return;
            }

            Notify();
            UnsavedCount += added;

            StatusMessage = Strings.ModLists_Added(added, added, row.Name);
        });
    }

    //
    // Takes one mod off the panel. The mod stays installed and enabled whatever happens next: saving
    // changes what the list says, and only applying an Exclusive list afterwards sets it aside.
    //
    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void RemoveEntry(ModListEntryRowViewModel? entry)
    {
        if (Selected is not { } row || entry is null) return;
        if (!Entries.Remove(entry)) return;

        UnsavedCount++;
        Notify();

        StatusMessage = Text(Strings.ModLists_RemovedEntryFormat, entry.Name, row.Name);
    }

    public void ChooseScope(ModListEntryRowViewModel entry, ModListEntryScope scope)
    {
        if (SelectionIsEditable && scope != entry.Entry.EffectiveScope) SetScope(entry, scope);
    }

    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void TakeAdvice(ModListEntryRowViewModel? entry)
    {
        if (entry?.AdviceScope is { } scope) SetScope(entry, scope);
    }

    private void SetScope(ModListEntryRowViewModel entry, ModListEntryScope next)
    {
        if (Selected is null) return;

        var index = Entries.IndexOf(entry);
        if (index < 0) return;

        // ModListEntry is a class with init-only properties, not a record, so this is a rebuild
        // rather than a `with`. Every field is carried across deliberately - a missed one here
        // would silently drop a version pin.
        var source = entry.Entry;

        Entries[index] = Row(new ModListEntry
        {
            Name = source.Name,
            ModId = source.ModId,
            IsAddon = source.IsAddon,
            VersionId = source.VersionId,
            Version = source.Version,
            Guid = source.Guid,
            Folders = [.. source.Folders],
            FitOverride = source.FitOverride,

            // Cycling back round to Everyone stores null - see ModListEntry.Scope - so the entry
            // ends up exactly as it was before anyone touched it, rather than carrying a value that
            // means what silence already meant.
            Scope = next,
        });
        UnsavedCount++;

        // The scope filter may no longer match this row, so the counts move even though the list
        // did not.
        Notify();

        StatusMessage = next switch
        {
            ModListEntryScope.Server =>
                Text(Strings.ModLists_ScopeServerOnlyFormat, entry.Name),
            ModListScopes.ClientsAndHeadless =>
                Text(Strings.ModLists_ScopeClientsAndHeadlessFormat, entry.Name),
            ModListEntryScope.Client =>
                Text(Strings.ModLists_ScopeClientOnlyFormat, entry.Name),
            ModListEntryScope.Headless =>
                Text(Strings.ModLists_ScopeHeadlessOnlyFormat, entry.Name),
            ModListScopes.ServerAndClients =>
                Text(Strings.ModLists_ScopeServerAndClientsFormat, entry.Name),
            _ => Text(Strings.ModLists_ScopeEveryoneFormat, entry.Name),
        };
    }

    //
    // Re-reads the installed version of every mod on this list.
    //
    // The alternative was taking a mod off the list and putting it back, which is what re-captures
    // the version it has now - correct, and ridiculous once a list runs to seventy entries and an
    // update round has moved a dozen of them.
    //
    // It only changes versions. Scope stays exactly as set, entries for mods this machine does not
    // have are left alone, and nothing is written to the list - the changes land in the panel as
    // unsaved, so Save is still the thing that commits them and Discard still throws them away.
    //
    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task RefreshVersionsAsync()
    {
        if (Selected is not { } row) return;

        IsBusy = true;

        try
        {
            var result = await _service.RefreshVersionsAsync(Entries.Select(e => e.Entry));

            if (result is null)
            {
                StatusMessage = AppMessages.NoSptInstallFolder;
                return;
            }

            if (result.Changed.Count == 0)
            {
                StatusMessage = result.NotInstalled == Entries.Count
                    ? Strings.ModLists_NoneInstalledHere
                    : Text(Strings.ModLists_VersionsAlreadyMatchFormat, row.Name);
                return;
            }

            var index = 0;
            foreach (var entry in result.Entries) Entries[index++] = Row(entry);

            UnsavedCount += result.Changed.Count;
            Notify();

            // Named rather than counted: "12 versions updated" is not something anybody can check,
            // and the whole point of the button is to be able to see what it decided.
            var named = string.Join(Strings.Common_ListSeparator, result.Changed
                .Take(5)
                .Select(c => Text(
                    Strings.ModLists_VersionChangeEntryFormat,
                    c.Name,
                    c.From ?? Strings.ModLists_VersionUnlocked,
                    c.To ?? Strings.ModLists_VersionUnlocked)));

            var rest = result.Changed.Count > 5
                ? Text(Strings.ModLists_AndMoreFormat, result.Changed.Count - 5)
                : string.Empty;

            StatusMessage = Strings.ModLists_VersionsUpdated(
                result.Changed.Count, result.Changed.Count, named, rest);
        }
        finally
        {
            IsBusy = false;
        }
    }

    //
    // Writes the panel to the list. This is the only thing on this page that changes a saved list's
    // contents, and it changes nothing else: no downloads, no folders moved, no revision.
    //
    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void Save()
    {
        if (Selected is not { } row) return;

        var count = Entries.Count;

        AppServices.ModLists.ReplaceEntries(row.Id, ModListEntries.Sorted(Entries.Select(e => e.Entry)));
        UnsavedCount = 0;
        Refresh(row.Id);

        StatusMessage = Strings.ModLists_Saved(count, row.Name, count);
    }

    // Throws the unsaved edits away and shows the list as it is stored.
    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void Discard()
    {
        if (Selected is not { } row) return;

        ShowEntries();
        StatusMessage = Text(Strings.ModLists_RevertedFormat, row.Name);
    }

    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void Rename()
    {
        IsRenaming = false;

        if (Selected is not { } row) return;

        var name = EditName.Trim();
        if (name.Length == 0 || name == row.Name) return;

        AppServices.ModLists.Rename(row.Id, name);
        Refresh(row.Id);
        StatusMessage = Text(Strings.ModLists_RenamedFormat, name);
    }

    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void StartRename()
    {
        if (Selected is not { } row) return;

        EditName = row.Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRename()
    {
        EditName = Selected?.Name ?? string.Empty;
        IsRenaming = false;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Selected is not { } row) return;

        AppServices.ModLists.Delete(row.Id);
        Refresh();
        StatusMessage = Text(Strings.ModLists_DeletedFormat, row.Name);
    }

    // Imported and server lists are read-only; editing one starts a local copy that points back.
    [RelayCommand(CanExecute = nameof(SelectionIsImported))]
    private void Fork()
    {
        if (Selected is not { } row) return;

        var fork = AppServices.ModLists.Fork(
            row.Id, Text(Strings.ModLists_ForkNameFormat, row.Name), DateTimeOffset.UtcNow);
        Refresh(fork.Id);
        StatusMessage = Text(Strings.ModLists_ForkedFormat, fork.Name);
    }
}
