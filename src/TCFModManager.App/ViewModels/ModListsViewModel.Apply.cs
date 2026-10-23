using System.IO;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class ModListsViewModel
{
    //
    // Pins or unpins the mod on a Disable or Pinned row, then plans the same list again so the row
    // and the summary say what an apply would now do.
    //
    [RelayCommand]
    private async Task TogglePinAsync(ModListActionRowViewModel? row)
    {
        if (row is not { Action.Installed: { } installed } || _preview is not { } preview) return;
        if (!row.CanPin && !row.CanUnpin) return;

        await RunAsync(async () =>
        {
            AppServices.ModLists.SetPinned(ModListPlanner.PinKeys(installed), pinned: row.CanPin);

            RefreshPinLookup();
            for (var i = 0; i < Entries.Count; i++) Entries[i] = Row(Entries[i].Entry);

            // Dependencies already asked about are kept - planned again, not asked again.
            var replanned = preview.Dependencies is { } check
                ? ModListService.Replan(preview, check)
                : await _service.PreviewAsync(preview.List);

            if (replanned is null)
            {
                ClearPlan();
                StatusMessage = AppMessages.NoSptInstallFolder;
                return;
            }

            ShowPlan(replanned);

            StatusMessage = row.CanPin
                ? Text(Strings.ModLists_PinnedFormat, row.Name)
                : Text(Strings.ModLists_UnpinnedFormat, row.Name);
        });
    }

    private bool CanApply => !IsBusy && _preview is not null && !HasUnsavedChanges && _applyBlockers.Count == 0;

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

            // SPT started between the last check and the click: nothing was changed, so the plan
            // stays up with Apply greyed out until it is closed again.
            if (result.Stopped == ModListStop.InstallInUse && result.Moves.Count == 0)
            {
                SetApplyBlockers(result.Running);
                StatusMessage = PlanStatus(preview);
                return;
            }

            if (result.Completed)
            {
                StatusMessage = Completed(result, ModListDownloadMode.For(preview.List));
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
                undone = ModDisableService.Revert(result.Moves, AppServices.SptEnvironment.InstallPath).Moved.Count;
            }

            var putBack = undone switch
            {
                0 => string.Empty,
                _ => " " + Strings.ModLists_PutBack(undone),
            };

            StatusMessage = ModListProblems.Describe(result) + putBack + FailureDetail(result);

            ClearPlan();
            Refresh(preview.List.Id);
        });
    }

    private static string Completed(ModListApplyResult result, ModListDownloadMode mode)
    {
        var parts = new List<string>();
        void Count(int n, string format) { if (n > 0) parts.Add(Text(format, n)); }

        Count(result.Fetched.Fetched.Count, Strings.ModLists_CountDownloadedFormat);
        Count(result.Enabled.Moved.Count, Strings.ModLists_CountEnabledFormat);
        Count(result.Disabled.Moved.Count, Strings.ModLists_CountDisabledFormat);

        var message = parts.Count == 0
            ? Strings.ModLists_AppliedNothing
            : Text(Strings.ModLists_AppliedFormat, string.Join(Strings.Common_ListSeparator, parts));

        // Whole sentences appended to a whole sentence, rather than clauses glued to one.
        if (mode.DownloadOnly && result.Fetched.Fetched.Count > 0)
        {
            var folder = DownloadFolders.Resolve(new SettingsService().Load().Monitor.DownloadFolder);
            if (mode.Subfolder is { } subfolder) folder = Path.Combine(folder, DownloadFolders.SafeName(subfolder));

            message += " " + Text(Strings.ModLists_AppliedDownloadOnlyFormat, folder);
        }

        var manual = result.Manual.Count;
        if (manual > 0)
        {
            message += " " + Strings.ModLists_AppliedManual(manual);
        }

        var failedMoves = result.Enabled.Failed.Count + result.Disabled.Failed.Count;
        if (failedMoves > 0)
        {
            message += " " + Strings.ModLists_AppliedMovesFailed(failedMoves);
        }

        return message;
    }

    private static string FailureDetail(ModListApplyResult result) =>
        result.Fetched.Failed.Count == 0
            ? string.Empty
            : " " + string.Join(
                Strings.Common_ClauseSeparator,
                result.Fetched.Failed.Take(3).Select(f => Text(Strings.ModLists_FetchFailureFormat, f.ModName, f.Reason)));

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
                StatusMessage = Strings.ModLists_NothingToUndo;
                Refresh();
                return;
            }

            if (result.Completed)
            {
                StatusMessage = Text(
                    Strings.ModLists_UndoneFormat,
                    result.Enabled.Moved.Count,
                    result.Disabled.Moved.Count);
            }
            else
            {
                if (result.Moves.Count > 0) ModDisableService.Revert(result.Moves, AppServices.SptEnvironment.InstallPath);
                StatusMessage = Text(Strings.ModLists_UndoFailedFormat, ModListProblems.Describe(result));
            }

            ClearPlan();
            Refresh();
        });
    }
}
