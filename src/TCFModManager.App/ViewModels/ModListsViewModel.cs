using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One saved list in the left-hand list.
public sealed partial class ModListRowViewModel(ModList list, bool isActive) : ObservableObject
{
    public ModList List { get; } = list;

    public Guid Id => List.Id;

    public string Name => List.Name;

    public bool IsEditable => List.IsEditable;

    public bool IsActive { get; } = isActive;

    public string Detail
    {
        get
        {
            var parts = new List<string> { List.Entries.Count == 1 ? "1 mod" : $"{List.Entries.Count} mods" };

            if (List.IsSnapshot) parts.Add("snapshot");

            parts.Add(List.Origin switch
            {
                ModListOrigin.Imported => List.Source is null ? "imported" : $"from {List.Source}",
                ModListOrigin.Server => List.Source is null ? "from a server" : $"from {List.Source}",
                _ => "made here",
            });

            if (List.Revision > 1) parts.Add($"revision {List.Revision}");

            return string.Join(" · ", parts);
        }
    }
}

// One line of a plan, as the diff shows it.
public sealed record ModListActionRowViewModel(string Kind, string Name, string Detail, int Order);

// One mod on the selected list, as the contents panel shows it.
public sealed record ModListEntryRowViewModel(ModListEntry Entry, string Name, string Detail);

//
// The Mod lists page: what lists this install holds, what applying one would do, and applying it.
//
// Every decision this page makes is Core's - it captures, previews and applies through
// ModListService and renders what comes back. See [ModListApplier] for the order an apply runs in
// and why nothing is disabled until every download has worked.
//
public partial class ModListsViewModel : ObservableObject
{
    private readonly ModListService _service = AppServices.ModListWorkflow;

    private ModListPreview? _preview;

    public ObservableCollection<ModListRowViewModel> Lists { get; } = [];

    // What the selected list names, in the order it stores them.
    public ObservableCollection<ModListEntryRowViewModel> Entries { get; } = [];

    public ObservableCollection<ModListActionRowViewModel> PlanRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionIsEditable))]
    [NotifyPropertyChangedFor(nameof(SelectionIsImported))]
    [NotifyPropertyChangedFor(nameof(SelectionIsActive))]
    [NotifyPropertyChangedFor(nameof(SelectionDetail))]
    [NotifyPropertyChangedFor(nameof(ShowContents))]
    [NotifyPropertyChangedFor(nameof(CanEditList))]
    [NotifyPropertyChangedFor(nameof(CanUseStoredList))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddModsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForkCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    private ModListRowViewModel? _selected;

    [ObservableProperty]
    private string _newListName = string.Empty;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "No mod list is being followed.";

    //
    // The one undo point: how the install stood before the last apply. Null when nothing has been
    // applied, and cleared once used - a revert is the end of the chain, not another step in it.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRevert))]
    [NotifyPropertyChangedFor(nameof(CanRevert))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private string? _revertLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    [NotifyPropertyChangedFor(nameof(ShowContents))]
    private string? _planSummary;

    // How many adds and removes are waiting to be saved. Zero means what is on screen is what is
    // in the file.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    [NotifyPropertyChangedFor(nameof(CanUseStoredList))]
    [NotifyPropertyChangedFor(nameof(UnsavedLabel))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    private int _unsavedCount;

    // Shown when the selected list was captured on a different SPT version than this install.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersionWarning))]
    private string? _versionWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRevert))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private bool _isBusy;

    public bool HasSelection => Selected is not null;

    public bool SelectionIsEditable => Selected?.IsEditable == true;

    public bool SelectionIsImported => Selected is not null && !Selected.IsEditable;

    public bool SelectionIsActive => Selected?.IsActive == true;

    // The same one-line summary the left-hand row shows, repeated under the name so the detail
    // pane says what it is without the eye having to go back to the list.
    public string SelectionDetail => Selected?.Detail ?? string.Empty;

    public bool HasLists => Lists.Count > 0;

    public bool HasRevert => RevertLabel is not null;

    public bool CanRevert => HasRevert && !IsBusy;

    public bool HasPlan => PlanSummary is not null;

    public bool HasVersionWarning => VersionWarning is not null;

    //
    // The contents panel and the plan share one cell and swap. Previewing answers a different
    // question - what applying this would do, rather than what it says - and two long lists at
    // once is the congestion this page was corrected for once already.
    //
    public bool ShowContents => HasSelection && !HasPlan;

    //
    // Edits are held here until Save, and Save writes the list and nothing else.
    //
    // The split is the point: saving changes what the list *says*, applying changes what is on
    // disk. Nothing on this page installs, enables or disables a mod except Apply.
    //
    public bool HasUnsavedChanges => UnsavedCount > 0;

    // Preview and Apply work off the stored list, so they wait for the edits to be stored. Letting
    // them run against a list that no longer matches what is on screen is the one confusing state
    // an editable list can get into.
    public bool CanEditList => SelectionIsEditable;

    public bool CanUseStoredList => HasSelection && !HasUnsavedChanges;

    public bool HasEntries => Entries.Count > 0;

    public string EntriesHeader => Entries.Count == 1 ? "1 mod on this list" : $"{Entries.Count} mods on this list";

    public string UnsavedLabel => UnsavedCount == 1 ? "1 unsaved change" : $"{UnsavedCount} unsaved changes";

    partial void OnSelectedChanged(ModListRowViewModel? value)
    {
        var dropped = UnsavedCount;

        EditName = value?.Name ?? string.Empty;
        ShowEntries();
        ClearPlan();

        // Switching lists drops whatever was not saved. Said out loud rather than silently - nothing
        // here is destructive, but work disappearing without a word is its own kind of bug.
        if (dropped > 0) StatusMessage = $"Dropped {dropped} unsaved change(s) - they were never written to the list.";
    }

    //
    // Fills the contents panel from the selected list.
    //
    // Read off the row's own ModList rather than the store: Refresh has just loaded it, and going
    // back for a second read would let the panel and the row disagree about the same list.
    //
    private void ShowEntries()
    {
        Entries.Clear();

        _titles = CatalogTitles();

        if (Selected is { } row)
        {
            foreach (var entry in ModListEntries.Sorted(row.List.Entries)) Entries.Add(Row(entry));
        }

        // Whatever was in the buffer is gone; the panel now shows the file again.
        UnsavedCount = 0;
        Notify();
    }

    //
    // Listing titles by mod id, rebuilt whenever the panel is.
    //
    // A list captured before entries carried a listing title stores the folder name, so without
    // this the row reads "acidphantasm-itemvaluewatermark" for a mod published as "Item Value
    // Watermark" - and then prints the folder underneath as well, saying the same thing twice.
    // Looking the title up here fixes the lists already on disk rather than only the next capture.
    //
    private Dictionary<(int Id, bool IsAddon), string> _titles = [];

    private static Dictionary<(int Id, bool IsAddon), string> CatalogTitles()
    {
        var titles = new Dictionary<(int, bool), string>();

        foreach (var mod in AppServices.ModCache.AllMods)
            if (!string.IsNullOrWhiteSpace(mod.Name)) titles.TryAdd((mod.Id, false), mod.Name!.Trim());

        // Addons are numbered in their own sequence, so the addon flag is part of the key.
        foreach (var addon in AppServices.Addons.AllAddons)
            if (!string.IsNullOrWhiteSpace(addon.Name)) titles.TryAdd((addon.Id, true), addon.Name!.Trim());

        return titles;
    }

    //
    // What the entry stores, unless the catalog knows the mod under a better name. An unresolved
    // entry, an addon the cache hasn't loaded and an empty catalog all fall back to the stored name.
    //
    private ModListEntryRowViewModel Row(ModListEntry entry)
    {
        var name = entry.ModId is { } id && _titles.TryGetValue((id, entry.IsAddon), out var title)
            ? title
            : entry.Name;

        return new ModListEntryRowViewModel(entry, name, EntryDetail(entry, name));
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(EntriesHeader));
    }

    //
    // What one entry says about itself. The three states a list entry can be in are the whole
    // point of the line: a pinned version is fetched exactly, an unpinned one comes down at the
    // newest published, and one with no mod id at all is somebody's manual job.
    //
    private static string EntryDetail(ModListEntry entry, string name)
    {
        var parts = new List<string>();

        if (entry.IsAddon) parts.Add("addon");

        //
        // The folders on disk this entry covers.
        //
        // The name above it is the sp-mod.com listing title wherever one matched, which is often
        // nothing like what the mod calls its own folder - and the folder is what you go looking for
        // when something has to be sorted out by hand. Absent for a mod added from the catalog that
        // nobody here has installed, which is the honest answer for one.
        //
        if (InstalledAs(entry, name) is { } folders) parts.Add($"installed as {folders}");

        if (!entry.IsResolved) parts.Add("not on sp-mod.com - installed by hand");
        else if (entry.Version is { } version) parts.Add(entry.IsPinned ? $"version {version}" : $"version {version}, not pinned");
        else parts.Add("newest published version");

        return string.Join(" · ", parts);
    }

    //
    // The folders to print under the name, or null when saying so would only repeat it.
    //
    // A list captured before entries carried the listing title stores the folder as the name, and a
    // mod the catalog never matched still does - printing "installed as" under either says the same
    // thing twice, which is what Chris saw.
    //
    private static string? InstalledAs(ModListEntry entry, string name)
    {
        if (entry.Folders.Count == 0) return null;

        var folders = string.Join(", ", entry.Folders);

        // Compared against the name actually shown above it, not the stored one: a mod the catalog
        // never matched is already named after its folder, and repeating it says nothing.
        return string.Equals(folders, name.Trim(), StringComparison.OrdinalIgnoreCase) ? null : folders;
    }

    private void ClearPlan()
    {
        _preview = null;
        PlanRows.Clear();
        PlanSummary = null;
        VersionWarning = null;
        ApplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void Refresh(Guid? select = null)
    {
        var data = AppServices.ModLists.Load();
        var keep = select ?? Selected?.Id;

        Lists.Clear();

        foreach (var list in data.Lists.OrderByDescending(l => l.UpdatedAt))
            Lists.Add(new ModListRowViewModel(list, data.ActiveListId == list.Id));

        OnPropertyChanged(nameof(HasLists));

        RevertLabel = _service.PendingRevert() is { } snapshot
            ? $"Undo \"{snapshot.Name}\""
            : null;

        Selected = Lists.FirstOrDefault(l => l.Id == keep);

        // Setting Selected only rebuilds the panel when it actually changed, and a refresh after an
        // edit hands back a row for the same list - same id, new contents.
        ShowEntries();

        var active = Lists.FirstOrDefault(l => l.IsActive);
        if (active is not null) StatusMessage = $"Following \"{active.Name}\".";
    }

    // Saves what's installed right now as a new list.
    [RelayCommand]
    private async Task CaptureAsync()
    {
        var name = NewListName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Give the list a name first.";
            return;
        }

        await RunAsync(async () =>
        {
            var captured = await _service.CaptureAsync(name);

            if (captured is null)
            {
                StatusMessage = AppMessages.NoSptInstallFolder;
                return;
            }

            NewListName = string.Empty;
            Refresh(captured.Id);
            StatusMessage = $"Captured {captured.Entries.Count} enabled mod(s) as \"{captured.Name}\".";
        });
    }

    // Works out what applying the selected list would do. Nothing moves and nothing downloads.
    [RelayCommand(CanExecute = nameof(CanUseStoredList))]
    private async Task PreviewAsync()
    {
        if (Selected is not { } row) return;

        await RunAsync(async () =>
        {
            var preview = await _service.PreviewAsync(row.List);

            if (preview is null)
            {
                StatusMessage = AppMessages.NoSptInstallFolder;
                return;
            }

            ShowPlan(preview);
        });
    }

    private void ShowPlan(ModListPreview preview)
    {
        _preview = preview;
        PlanRows.Clear();

        foreach (var row in Rows(preview.Plan)) PlanRows.Add(row);

        var plan = preview.Plan;

        var counts = new List<string>();
        void Count(int n, string label) { if (n > 0) counts.Add($"{n} {label}"); }

        Count(plan.Install.Count(), "to install");
        Count(plan.Update.Count(), "to update");
        Count(plan.Enable.Count(), "to enable");
        Count(plan.Disable.Count(), "to disable");
        Count(plan.Keep.Count(), "already right");
        Count(plan.Manual.Count(), "to fetch yourself");

        PlanSummary = plan.IsNoOp && plan.Manual.Count() == 0
            ? "This install already matches the list - nothing to do."
            : string.Join(", ", counts) + ".";

        VersionWarning = preview.List.SptVersion is { } captured
            && preview.Install.SptVersion is { } current
            && !string.Equals(captured, current, StringComparison.OrdinalIgnoreCase)
                ? $"This list was made on SPT {captured} and you're running {current}. Mods pinned for one won't always work on the other."
                : null;

        ApplyCommand.NotifyCanExecuteChanged();
        StatusMessage = plan.RequiresGameClosed
            ? "Close SPT before applying - disabling a mod can't happen while it's running."
            : $"Previewed \"{preview.List.Name}\".";
    }

    private static IEnumerable<ModListActionRowViewModel> Rows(ModListPlan plan) =>
        plan.Actions
            .Select(a => new ModListActionRowViewModel(Label(a), a.Name, Detail(a), Order(a.Kind)))
            .OrderBy(r => r.Order)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

    private static string Label(ModListAction action) => action.Kind switch
    {
        ModListActionKind.Install => "Install",
        ModListActionKind.Update => action.IsDowngrade ? "Downgrade" : "Update",
        ModListActionKind.Enable => action.NeedsUpdateAfterEnable ? "Enable + update" : "Enable",
        ModListActionKind.Disable => "Disable",
        ModListActionKind.Manual => "Fetch yourself",
        _ => "Unchanged",
    };

    //
    // Prefixed with "Addon - " where it applies: two rows can otherwise read identically while
    // meaning different things, since an addon and a mod are numbered separately and an addon's
    // name rarely says what it attaches to.
    //
    private static string Detail(ModListAction action)
    {
        var detail = DetailFor(action);
        if (!action.IsAddon) return detail;

        return detail.Length == 0 ? "Addon" : $"Addon - {detail}";
    }

    private static string DetailFor(ModListAction action) => action.Kind switch
    {
        ModListActionKind.Install => action.TargetVersion is null ? "newest published" : $"version {action.TargetVersion}",
        ModListActionKind.Update or ModListActionKind.Enable when action.TargetVersion is not null
            && action.InstalledVersion is not null && action.TargetVersion != action.InstalledVersion =>
            $"{action.InstalledVersion} to {action.TargetVersion}",
        ModListActionKind.Disable => "not on this list",
        ModListActionKind.Manual => "not on sp-mod.com - install it by hand",
        _ => action.InstalledVersion is null ? string.Empty : $"version {action.InstalledVersion}",
    };

    private static int Order(ModListActionKind kind) => kind switch
    {
        ModListActionKind.Install => 0,
        ModListActionKind.Update => 1,
        ModListActionKind.Enable => 2,
        ModListActionKind.Disable => 3,
        ModListActionKind.Manual => 4,
        _ => 5,
    };

    private bool CanApply => !IsBusy && _preview is not null && !HasUnsavedChanges;

    //
    // Applies the previewed plan. The preview is reused rather than rebuilt, so what runs is what
    // was shown - a rescan in between would silently change it.
    //
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_preview is not { } preview) return;

        await RunAsync(async () =>
        {
            var result = await _service.ApplyAsync(preview, ModListPrompts.Default);

            if (result.Completed)
            {
                StatusMessage = Completed(result);
                ClearPlan();
                Refresh(preview.List.Id);
                return;
            }

            //
            // An apply that stopped part way has, at most, enabled some mods - nothing is ever
            // disabled unless every download worked. Putting those back leaves the install exactly
            // as it was found rather than in a state nobody asked for.
            //
            var undone = 0;

            if (result.Moves.Count > 0)
            {
                undone = ModDisableService.Revert(result.Moves).Moved.Count;
            }

            StatusMessage = ModListProblems.Describe(result)
                + (undone > 0 ? $" Put {undone} mod(s) back the way they were." : string.Empty)
                + FailureDetail(result);

            ClearPlan();
            Refresh(preview.List.Id);
        });
    }

    private static string Completed(ModListApplyResult result)
    {
        var parts = new List<string>();
        void Count(int n, string label) { if (n > 0) parts.Add($"{n} {label}"); }

        Count(result.Fetched.Fetched.Count, "downloaded");
        Count(result.Enabled.Moved.Count, "enabled");
        Count(result.Disabled.Moved.Count, "disabled");

        var message = parts.Count == 0 ? "Applied - nothing needed changing." : "Applied: " + string.Join(", ", parts) + ".";

        var manual = result.Manual.Count;
        if (manual > 0) message += $" {manual} mod(s) still need installing by hand.";

        var failedMoves = result.Enabled.Failed.Count + result.Disabled.Failed.Count;
        if (failedMoves > 0) message += $" {failedMoves} couldn't be moved.";

        return message;
    }

    private static string FailureDetail(ModListApplyResult result) =>
        result.Fetched.Failed.Count == 0
            ? string.Empty
            : " " + string.Join("; ", result.Fetched.Failed.Take(3).Select(f => $"{f.ModName}: {f.Reason}"));

    //
    // Puts the install back the way it was before the last list was applied, then clears the undo
    // point. Normally moves mods without downloading anything, since the snapshot only names mods
    // that were installed at the time.
    //
    [RelayCommand(CanExecute = nameof(CanRevert))]
    private async Task RevertAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _service.RevertAsync(ModListPrompts.Default);

            if (result is null)
            {
                StatusMessage = "Nothing to undo.";
                Refresh();
                return;
            }

            if (result.Completed)
            {
                StatusMessage = "Put the install back the way it was before the last apply."
                    + $" {result.Enabled.Moved.Count} enabled, {result.Disabled.Moved.Count} disabled.";
            }
            else
            {
                if (result.Moves.Count > 0) ModDisableService.Revert(result.Moves);
                StatusMessage = $"Couldn't undo. {ModListProblems.Describe(result)}";
            }

            ClearPlan();
            Refresh();
        });
    }

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
                StatusMessage = "Nothing to add from - no SPT install folder is set and the sp-mod.com catalog hasn't loaded yet.";
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
                StatusMessage = "Nothing added - the list already names those mods.";
                return;
            }

            SortEntries();
            UnsavedCount += added;

            StatusMessage = $"Added {added} mod(s) to \"{row.Name}\". Save to write it to the list"
                + " - nothing is installed or enabled until you Apply.";
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

        StatusMessage = $"Took \"{entry.Name}\" off \"{row.Name}\". Save to write it to the list"
            + " - the mod stays installed and enabled either way.";
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

        StatusMessage = $"Saved \"{row.Name}\" - {count} mod(s). Nothing on disk changed;"
            + " Apply is what installs, enables and sets mods aside.";
    }

    // Throws the unsaved edits away and shows the list as it is stored.
    [RelayCommand(CanExecute = nameof(HasUnsavedChanges))]
    private void Discard()
    {
        if (Selected is not { } row) return;

        ShowEntries();
        StatusMessage = $"Put \"{row.Name}\" back the way it was saved.";
    }

    // Keeps the panel in the order a stored list holds - by name, the order capture writes.
    private void SortEntries()
    {
        var sorted = ModListEntries.Sorted(Entries.Select(e => e.Entry));

        Entries.Clear();
        foreach (var entry in sorted) Entries.Add(Row(entry));

        Notify();
    }

    // Puts the list's contents back in view after a preview. Changes nothing either way.
    [RelayCommand]
    private void ClosePlan()
    {
        ClearPlan();
        StatusMessage = Selected is { } row ? $"Showing what \"{row.Name}\" names." : StatusMessage;
    }

    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void Rename()
    {
        if (Selected is not { } row) return;

        var name = EditName.Trim();
        if (name.Length == 0 || name == row.Name) return;

        AppServices.ModLists.Rename(row.Id, name);
        Refresh(row.Id);
        StatusMessage = $"Renamed to \"{name}\".";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Selected is not { } row) return;

        AppServices.ModLists.Delete(row.Id);
        Refresh();
        StatusMessage = $"Deleted \"{row.Name}\". Nothing on disk changed.";
    }

    // Imported and server lists are read-only; editing one starts a local copy that points back.
    [RelayCommand(CanExecute = nameof(SelectionIsImported))]
    private void Fork()
    {
        if (Selected is not { } row) return;

        var fork = AppServices.ModLists.Fork(row.Id, $"{row.Name} (mine)", DateTimeOffset.UtcNow);
        Refresh(fork.Id);
        StatusMessage = $"Made \"{fork.Name}\" - the original is untouched.";
    }

    [RelayCommand]
    private void StopFollowing()
    {
        AppServices.ModLists.SetActive(null);
        Refresh();
        StatusMessage = "No mod list is being followed. Nothing on disk changed.";
    }

    // Writes a manifest, never mod files - "install mod 2426 at version 5", not somebody's archive.
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Export()
    {
        if (Selected is not { } row) return;

        var dialog = new SaveFileDialog
        {
            Title = "Share this mod list",
            Filter = ModListFile.FileFilter,
            FileName = ModListFile.SuggestedFileName(row.List),
            AddExtension = true,
            DefaultExt = ModListFile.Extension,
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            ModListFile.Save(row.List, dialog.FileName);
            StatusMessage = $"Saved \"{row.Name}\" - send that file to anyone running this app.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Couldn't write the file - {ex.Message}";
        }
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a shared mod list",
            Filter = ModListFile.FileFilter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        var read = ModListFile.Load(dialog.FileName);

        if (!read.Succeeded)
        {
            StatusMessage = $"Couldn't read that file - {read.Error}";
            return;
        }

        var list = read.List!;
        var existing = AppServices.ModLists.Find(list.Id);

        AppServices.ModLists.Add(list);
        Refresh(list.Id);

        StatusMessage = existing is null
            ? $"Imported \"{list.Name}\" - preview it to see what applying it would do."
            : $"Updated \"{list.Name}\" from revision {existing.Revision} to {list.Revision}.";
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (IsBusy) return;

        IsBusy = true;

        try
        {
            await work();
        }
        catch (ModInstallException ex)
        {
            // Reverting an apply touches the install, so it can be refused like any other move -
            // and ModInstallException's Message is the reason name, not a sentence.
            AppLog.Error("ModLists", ex.ToString());
            StatusMessage = ModInstallProblems.Describe(ex);
        }
        catch (Exception ex)
        {
            AppLog.Error("ModLists", ex.ToString());
            StatusMessage = $"Something went wrong - {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
