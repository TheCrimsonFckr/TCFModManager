using System.Windows;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    /// <summary>Whether the last disable/enable can still be put back.</summary>
    public bool CanUndo => _lastMoves.Count > 0;

    public string UndoLabel => _lastMoveLabelKey is null
        ? Strings.Installed_Undo
        : Text(LocalizationService.Get(_lastMoveLabelKey), _lastMoveLabelArg);

    /// <summary>Flips one mod between enabled and disabled.</summary>
    [RelayCommand]
    private Task ToggleDisableAsync(InstalledModCardViewModel? mod) =>
        mod is null ? Task.CompletedTask : ApplyDisableAsync([mod], !mod.IsDisabled);

    [RelayCommand]
    private Task DisableGroupAsync(ModGroupSectionViewModel? section) =>
        section is null ? Task.CompletedTask : ApplyDisableAsync(section.Items.ToList(), disable: true);

    [RelayCommand]
    private Task EnableGroupAsync(ModGroupSectionViewModel? section) =>
        section is null ? Task.CompletedTask : ApplyDisableAsync(section.Items.ToList(), disable: false);

    /// <summary>Disables everything currently enabled in a group and enables everything currently
    /// disabled in it, as one undoable step.</summary>
    [RelayCommand]
    private async Task InvertGroupAsync(ModGroupSectionViewModel? section)
    {
        if (section is null) return;

        var toDisable = section.Items.Where(m => !m.IsDisabled).ToList();
        var toEnable = section.Items.Where(m => m.IsDisabled).ToList();

        // The disable half is the one that can break other mods, so it asks first; the enable half
        // then runs without a second prompt and its moves are merged into the same undo step.
        var label = (nameof(Strings.Installed_UndoInvertFormat), (object?)section.Name);

        var moves = await ApplyDisableAsync(toDisable, disable: true, label: label);
        if (moves is null) return;

        await ApplyDisableAsync(toEnable, disable: false, label: label, confirm: false, carryOver: moves);
    }

    //
    // Settles a mod found in both a container and its ".disabled" sibling. The user picks which
    // copy to keep; the other is moved into a hidden folder in the install rather than deleted, so
    // a wrong answer costs nothing but a drag back.
    //
    [RelayCommand]
    private async Task ResolveDuplicateAsync(InstalledModCardViewModel? mod)
    {
        if (mod is not { HasDuplicateFolders: true }) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        if (ModInstallService.RunningBlockers(installPath) is { Count: > 0 } blockers)
        {
            StatusMessage = ModInstallProblems.InstallInUse(blockers, ModInstallAction.SortOutDuplicate);
            return;
        }

        var pairs = mod.DuplicateFolders;
        var answer = AskWindow.Show(new AskRequest
        {
            Title = Text(Strings.Installed_DuplicateTwiceTitleFormat, mod.DisplayTitle),
            Lead = Text(Strings.Installed_DuplicateLeadFormat, mod.DisplayTitle),
            Sections =
            [
                new(Strings.Installed_DuplicateEnabledHeading, [.. pairs.Select(p => p.Enabled.FolderPath)]),
                new(Strings.Installed_DuplicateDisabledHeading, [.. pairs.Select(p => p.Disabled.FolderPath)]),
            ],
            Detail = [Strings.Installed_DuplicateMovedNote],
            First = Strings.Installed_KeepEnabledCopy,
            Second = Strings.Installed_KeepDisabledCopy,
        });

        if (answer == AskChoice.Cancel) return;

        var keepEnabled = answer == AskChoice.First;
        var moves = new List<ModMove>();
        var failed = new List<ModDisableFailure>();

        IsBusy = true;
        try
        {
            var timestamp = DateTimeOffset.UtcNow;
            foreach (var pair in pairs)
            {
                var outcome = ModDisableService.ResolveDuplicate(installPath, pair, keepEnabled, timestamp);
                moves.AddRange(outcome.Moved);
                failed.AddRange(outcome.Failed);
            }
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        SetLastMoves(moves, (nameof(Strings.Installed_UndoSortOutFormat), mod.DisplayTitle));

        var message = Text(
            keepEnabled
                ? Strings.Installed_DuplicateKeptEnabledFormat
                : Strings.Installed_DuplicateKeptDisabledFormat,
            mod.DisplayTitle);
        if (failed.Count > 0) message = Sentences(message, DescribeFailures(failed));

        await ScanAsync();
        StatusMessage = message;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DisableSelectedAsync() => ApplyDisableAsync(SelectedCards(), disable: true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task EnableSelectedAsync() => ApplyDisableAsync(SelectedCards(), disable: false);

    /// <summary>Puts the last disable/enable back. Fails softly per mod if anything moved on disk since.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_lastMoves.Count == 0) return;

        IsBusy = true;
        string message;
        try
        {
            var outcome = ModDisableService.Revert(_lastMoves, AppServices.SptEnvironment.InstallPath);
            var put = Strings.Installed_Undone(outcome.Moved.Count);
            message = outcome.Failed.Count == 0 ? put : Sentences(put, DescribeFailures(outcome.Failed));
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        SetLastMoves([], null);
        await ScanAsync();
        StatusMessage = message;
    }

    private List<InstalledModCardViewModel> SelectedCards() => _all.Where(m => m.IsSelected).ToList();

    //
    // The one path every disable/enable goes through: works out what else the change reaches, asks
    // about it, moves the folders, then rescans. Returns the moves it made so a caller running two
    // passes (InvertGroupAsync) can merge them into one undo step, or null when nothing happened.
    //
    private async Task<List<ModMove>?> ApplyDisableAsync(
        IReadOnlyList<InstalledModCardViewModel> cards,
        bool disable,
        (string Key, object? Arg)? label = null,
        bool confirm = true,
        List<ModMove>? carryOver = null)
    {
        var targets = cards.Where(c => c.IsDisabled != disable).ToList();
        if (targets.Count == 0)
        {
            if (carryOver is null)
                StatusMessage = disable ? Strings.Installed_NothingToDisable : Strings.Installed_NothingToEnable;

            return carryOver ?? [];
        }

        // Checked before anything is asked or moved, so a locked install is reported up front
        // rather than after the user has answered a dialog. ModDisableService guards again itself.
        if (ModInstallService.RunningBlockers(AppServices.SptEnvironment.InstallPath) is { Count: > 0 } blockers)
        {
            StatusMessage = ModInstallProblems.InstallInUse(
                blockers,
                disable ? ModInstallAction.Disable : ModInstallAction.Enable);
            return null;
        }

        var affected = AffectedCards(targets, disable);
        var addonRows = AddonRows(targets, disable, affected);
        affected.AddRange(addonRows.Select(a => (a.Card, a.Detail, IsSoft: false)));

        if (confirm && affected.Count > 0)
        {
            var rows = affected
                .Select(a => new ModDisableImpactRow(a.Card.DisplayTitle, a.Detail, a.IsSoft))
                .ToList();

            var summary = !disable && addonRows.Count == affected.Count
                ? Strings.Disable_ImpactEnableAddons(addonRows.Count)
                : null;

            switch (ModDisableConfirmationWindow.Confirm(disable, targets.Select(t => t.DisplayTitle).ToList(), rows, summary))
            {
                case ModDisableChoice.Cancel:
                    return null;
                case ModDisableChoice.ProceedWithCascade:
                    targets = targets.Concat(affected.Select(a => a.Card)).Distinct().ToList();
                    break;
            }
        }

        var entries = targets.SelectMany(c => c.Entries).Where(e => e.IsDisabled != disable).ToList();

        IsBusy = true;

        // A copy of the SPT profiles first, when they changed since the last one (OPEN-12 F4). Not
        // while SPT runs from the install: the move is refused then, and the server is mid-save.
        if (AppServices.SptEnvironment.InstallPath is { Length: > 0 } profilesOf
            && ModInstallService.RunningBlockers(profilesOf).Count == 0)
        {
            await Task.Run(() => AppServices.ProfileBackups.BackupIfChanged(profilesOf, ProfileBackups.BeforeDisable));
        }

        ModDisableOutcome outcome;
        try
        {
            outcome = ModDisableService.Apply(entries, disable, AppServices.SptEnvironment.InstallPath);
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }

        MarkAddonsDisabledWithParent(targets, disable);

        var moves = (carryOver ?? []).Concat(outcome.Moved).ToList();
        SetLastMoves(moves, label ?? DescribeTargets(targets, disable));

        var message = disable
            ? Strings.Installed_DisabledCount(targets.Count)
            : Strings.Installed_EnabledCount(targets.Count);
        if (outcome.Failed.Count > 0) message = Sentences(message, DescribeFailures(outcome.Failed));

        await ScanAsync();
        StatusMessage = message;

        return moves;
    }

    //
    // What else this change reaches: the mods that would lose a dependency when disabling, or the
    // disabled dependencies still needed when enabling. Reported as whole cards rather than
    // individual scan entries, so a client+server mod is never half-moved.
    //
    private List<(InstalledModCardViewModel Card, string Detail, bool IsSoft)> AffectedCards(
        IReadOnlyList<InstalledModCardViewModel> targets, bool disable)
    {
        var roots = targets.SelectMany(c => c.Entries).ToList();
        var links = disable ? _dependencies.DisableImpact(roots) : _dependencies.EnableRequirements(roots);

        var results = new List<(InstalledModCardViewModel, string, bool)>();
        var seen = new HashSet<InstalledModCardViewModel>(targets);

        foreach (var link in links)
        {
            var reached = disable ? link.Dependent : link.Dependency;
            if (!_cardByEntry.TryGetValue(reached, out var card) || !seen.Add(card)) continue;

            var otherName = _cardByEntry.TryGetValue(disable ? link.Dependency : link.Dependent, out var other)
                ? other.DisplayTitle
                : (disable ? link.Dependency : link.Dependent).Name;

            var detail = Text(
                disable
                    ? link.IsSoft
                        ? Strings.Installed_ImpactOptionallyUsesFormat
                        : Strings.Installed_ImpactNeedsFormat
                    : link.IsSoft
                        ? Strings.Installed_ImpactOptionallyUsedByFormat
                        : Strings.Installed_ImpactNeededByFormat,
                otherName);

            results.Add((card, detail, link.IsSoft));
        }

        return results;
    }

    //
    // OPEN-31 R3/R5: the addons of the mods being disabled that would stay enabled, or, when enabling,
    // the addons that were disabled together with them. Mods already listed are left out.
    //
    private List<(InstalledModCardViewModel Card, string Detail)> AddonRows(
        IReadOnlyList<InstalledModCardViewModel> targets,
        bool disable,
        IReadOnlyList<(InstalledModCardViewModel Card, string Detail, bool IsSoft)> already)
    {
        var parents = targets.Where(t => !t.IsAddon && t.ModId is not null).ToList();
        if (parents.Count == 0) return [];

        HashSet<int> flagged = disable
            ? []
            : AppServices.InstallManifest.Load().Mods
                .Where(r => r is { IsAddon: true, DisabledWithParent: true })
                .Select(r => r.ModId)
                .ToHashSet();

        var seen = new HashSet<InstalledModCardViewModel>(targets.Concat(already.Select(a => a.Card)));
        var rows = new List<(InstalledModCardViewModel, string)>();

        foreach (var parent in parents)
        {
            foreach (var addon in AddonCardsOf(parent))
            {
                var offered = disable
                    ? !addon.IsDisabled
                    : addon.IsDisabled && addon.ModId is { } id && flagged.Contains(id);

                if (!offered || !seen.Add(addon)) continue;

                rows.Add((addon, Text(
                    disable ? Strings.Installed_ImpactAddonForFormat : Strings.Installed_ImpactDisabledWithFormat,
                    parent.DisplayTitle)));
            }
        }

        return rows;
    }

    //
    // Records which addons went with their parent (OPEN-31 R5): those disabled in the same action as
    // their parent, picked or carried along. An addon disabled or enabled without it is cleared.
    //
    private static void MarkAddonsDisabledWithParent(IReadOnlyList<InstalledModCardViewModel> moved, bool disable)
    {
        var addons = moved.Where(c => c is { IsAddon: true, ModId: not null }).ToList();
        if (addons.Count == 0) return;

        AppServices.InstallManifest.SetDisabledForFit(addons.Select(a => a.ModId!.Value), false);

        if (!disable)
        {
            AppServices.InstallManifest.SetDisabledWithParent(addons.Select(a => a.ModId!.Value), false);
            return;
        }

        var parentIds = moved.Where(c => c is { IsAddon: false, ModId: not null }).Select(c => c.ModId!.Value).ToHashSet();
        var carried = addons.Where(a => a.ParentModId is { } p && parentIds.Contains(p)).ToList();

        AppServices.InstallManifest.SetDisabledWithParent(carried.Select(a => a.ModId!.Value), true);
        AppServices.InstallManifest.SetDisabledWithParent(addons.Except(carried).Select(a => a.ModId!.Value), false);
    }

    private void SetLastMoves(List<ModMove> moves, (string Key, object? Arg)? label)
    {
        _lastMoves = moves;
        _lastMoveLabelKey = label?.Key;
        _lastMoveLabelArg = label?.Arg;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
        UndoCommand.NotifyCanExecuteChanged();
    }

    private static (string Key, object? Arg) DescribeTargets(
        IReadOnlyList<InstalledModCardViewModel> targets, bool disable)
    {
        if (targets.Count == 1)
        {
            return (disable
                ? nameof(Strings.Installed_UndoDisableNamedFormat)
                : nameof(Strings.Installed_UndoEnableNamedFormat), targets[0].DisplayTitle);
        }

        return (disable
            ? nameof(Strings.Installed_UndoDisableCountFormat)
            : nameof(Strings.Installed_UndoEnableCountFormat), targets.Count);
    }

    private static string DescribeFailures(IReadOnlyList<ModDisableFailure> failures) =>
        failures.Count == 1
            ? Text(Strings.Installed_MoveFailedNamedFormat, failures[0].ModName, failures[0].Reason)
            : Text(
                Strings.Installed_MoveFailedCountFormat,
                failures.Count,
                string.Join(
                    Strings.Common_ClauseSeparator,
                    failures.Select(f => Text(Strings.Installed_MoveFailedItemFormat, f.ModName, f.Reason))));

    // Two complete sentences on one status line, rather than one sentence with the other dropped
    // into it - where the break goes is the translator's to decide.
    private static string Sentences(string first, string second) =>
        string.Join(Strings.Common_SentenceSeparator, first, second);
}
