using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    //
    // True when at least one selected mod could actually be updated. Deliberately stricter than
    // HasSelection: offering an enabled Update button that then reports "nothing to update" is
    // worse than a greyed-out one, and the reasons a mod is excluded are explained when it runs.
    //
    private bool HasUpdatableSelection() => SelectedCards().Any(IsUpdatable);

    private static bool IsUpdatable(InstalledModCardViewModel card) =>
        card is { UpdateAvailable: true, IsDisabled: false, IsAddon: false, ModId: not null }
        && card.LatestPublishedVersion is not null;

    //
    // Updates every selected mod that has one waiting, in a single pass.
    //
    // The prompts are asked ONCE for the whole batch rather than once per mod - the same reasoning
    // as applying a mod list: per-mod modals over a long selection train people into turning the
    // gate off globally, which is strictly worse for them than one demanding prompt.
    //
    [RelayCommand(CanExecute = nameof(HasUpdatableSelection))]
    private void UpdateSelected() => UpdateCards(SelectedCards());

    // OPEN-12 A2: the Update button on one card or List row, and the right-click menu's Update - the
    // same path as Update selected, for one mod.
    [RelayCommand]
    private void UpdateOne(InstalledModCardViewModel? mod)
    {
        if (mod is not null) UpdateCards([mod]);
    }

    private void UpdateCards(List<InstalledModCardViewModel> selected)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var targets = selected.Where(IsUpdatable).ToList();

        if (targets.Count == 0)
        {
            StatusMessage = DescribeNothingToUpdate(selected);
            return;
        }

        // Resolved against the cached catalog before anything is asked, so a mod the catalog can no
        // longer identify is reported now rather than failing halfway through the batch.
        var resolved = new List<(InstalledModCardViewModel Card, Mod Mod, string Version)>();
        var unmatched = new List<string>();

        foreach (var card in targets)
        {
            var match = AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == card.ModId);
            // The card's own target: below a release sp-mod holds back, it is the newest one that
            // breaks nothing (OPEN-12 F11).
            var version = match is null
                ? null
                : card.UpdateVersion ?? ModCardViewModel.PickDisplayVersion(match, AppServices.SptEnvironment.InstalledVersion)?.Version;

            if (match is null || version is null) unmatched.Add(card.DisplayTitle);
            else resolved.Add((card, match, version));
        }

        if (resolved.Count == 0)
        {
            StatusMessage = Text(Strings.Installed_UpdateNotInCatalogFormat, TextLists.Join(unmatched));
            return;
        }

        //
        // One warning covering every hand-installed mod in the batch, for the same reason the
        // single-mod path warns at all: there is no record of which files the current version
        // placed, so the new one goes on top of it.
        //
        // Follows Monitor mode's setting. A download places nothing, so the warning below is moot.
        var downloadOnly = AppServices.ModPageGate.DownloadOnlyFor(alternate: false);

        var handInstalled = resolved.Where(r => !r.Card.IsAppManaged).Select(r => r.Card.DisplayTitle).ToList();
        if (!downloadOnly && handInstalled.Count > 0 && !Confirm(
                Strings.Installed_UpdateHandInstalledTitle(handInstalled.Count),
                Text(Strings.Installed_UpdateHandInstalledBodyFormat, TextLists.Join(handInstalled))))
        {
            StatusMessage = Strings.Installed_UpdateCancelled;
            return;
        }

        //
        // Any update whose version doesn't support the installed SPT - PickDisplayVersion falls back
        // to the newest when none does - is asked about once for the batch. No leaves those out and
        // updates the rest.
        //
        var incompatible = resolved
            .Select(r => (r.Card, Version: r.Mod.Versions?.FirstOrDefault(v => v.Version == r.Version)))
            .Where(r => SptCompatibility.IsIncompatible(r.Version?.SptVersionConstraint))
            .ToList();

        var leftOut = string.Empty;
        if (incompatible.Count > 0 && !SptCompatibility.ConfirmAnyway(
                [.. incompatible.Select(r => SptCompatibility.Line(r.Card.DisplayTitle, r.Version!.Version, r.Version.SptVersionConstraint))],
                batch: true))
        {
            var dropped = incompatible.Select(r => r.Card).ToHashSet();
            resolved.RemoveAll(r => dropped.Contains(r.Card));
            leftOut = Text(
                Strings.Install_IncompatibleLeftOutFormat,
                AppServices.SptEnvironment.InstalledVersion,
                TextLists.Join([.. incompatible.Select(r => r.Card.DisplayTitle)]));

            if (resolved.Count == 0)
            {
                StatusMessage = leftOut;
                return;
            }
        }

        // The same gate a single install goes through, asked once for the batch. ConfirmAll honours
        // the Options switch that turns the gate off.
        var links = resolved.Select(r => new ModPageLink(r.Card.DisplayTitle, r.Mod.DetailUrl)).ToList();
        if (!ReadModPageConfirmationWindow.ConfirmAll(links))
        {
            StatusMessage = Strings.Installed_UpdateCancelledUnread;
            return;
        }

        foreach (var (_, mod, version) in resolved)
        {
            AppServices.DownloadQueue.Enqueue(
                InstallTarget.For(mod), version, installPath, () => AppServices.SpModApi.FindModVersionAsync(mod.Id.ToString(), version),
                downloadOnly: downloadOnly);
        }

        var queued = Strings.Installed_UpdateQueued(resolved.Count);
        var summary = queued + DescribeSkipped(selected, unmatched);
        StatusMessage = leftOut.Length == 0 ? summary : Sentences(summary, leftOut);

        AppLog.Info("Installed", $"queued {resolved.Count} update(s) from a selection of {selected.Count}");
    }

    //
    // Why a selection that looked updatable produced nothing. Each reason is separate because they
    // need different things done about them - enabling a mod, opening its parent, or a rescan.
    //
    private static string DescribeNothingToUpdate(IReadOnlyList<InstalledModCardViewModel> selected)
    {
        if (selected.Count == 0) return Strings.Installed_NothingSelected;

        var disabled = selected.Count(c => c is { UpdateAvailable: true, IsDisabled: true });
        var addons = selected.Count(c => c is { UpdateAvailable: true, IsAddon: true });

        var reasons = new List<string>();

        if (disabled > 0) reasons.Add(Strings.Installed_ReasonDisabled(disabled));
        if (addons > 0) reasons.Add(Strings.Installed_ReasonAddon(addons));

        return reasons.Count == 0
            ? Strings.Installed_NoUpdatesAvailable
            : Text(
                Strings.Installed_NoUpdatesReasonsFormat,
                string.Join(Strings.Common_ListSeparator, reasons));
    }

    private static string DescribeSkipped(
        IReadOnlyList<InstalledModCardViewModel> selected,
        IReadOnlyList<string> unmatched)
    {
        var parts = new List<string>();

        var disabled = selected.Count(c => c is { UpdateAvailable: true, IsDisabled: true });
        var addons = selected.Count(c => c is { UpdateAvailable: true, IsAddon: true });
        var noUpdate = selected.Count(c => c.UpdateAvailable != true);

        if (noUpdate > 0) parts.Add(Strings.Installed_SkippedUpToDate(noUpdate));
        if (disabled > 0) parts.Add(Strings.Installed_SkippedDisabled(disabled));
        if (addons > 0) parts.Add(Strings.Installed_SkippedAddon(addons));
        if (unmatched.Count > 0) parts.Add(Strings.Installed_SkippedNotFound(unmatched.Count));

        return parts.Count == 0
            ? string.Empty
            : Text(Strings.Installed_SkippedFormat, string.Join(Strings.Common_ListSeparator, parts));
    }
}
