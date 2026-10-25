using System.Windows;
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
        card is { UpdateAvailable: true, IsDisabled: false, ModId: not null }
        && card.LatestPublishedVersion is not null;

    //
    // Updates every selected mod that has one waiting, in a single pass.
    //
    // The prompts are asked ONCE for the whole batch rather than once per mod - the same reasoning
    // as applying a mod list: per-mod modals over a long selection train people into turning the
    // gate off globally, which is strictly worse for them than one demanding prompt.
    //
    [RelayCommand(CanExecute = nameof(HasUpdatableSelection))]
    private Task UpdateSelectedAsync() => UpdateCardsAsync(SelectedCards());

    // OPEN-12 A2: the Update button on one card or List row, and the right-click menu's Update - the
    // same path as Update selected, for one mod.
    [RelayCommand]
    private Task UpdateOneAsync(InstalledModCardViewModel? mod) =>
        mod is null ? Task.CompletedTask : UpdateCardsAsync([mod]);

    private sealed record UpdateTarget(
        InstalledModCardViewModel Card,
        InstallTarget Target,
        string Version,
        string? DetailUrl,
        Func<Task<ModVersion?>> Resolve,
        long? TotalBytes = null,
        int? QueueBehindModId = null);

    private async Task UpdateCardsAsync(List<InstalledModCardViewModel> selected)
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

        foreach (var card in targets.Where(c => !c.IsAddon))
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
        }

        var updates = resolved
            .Select(r => new UpdateTarget(
                r.Card,
                InstallTarget.For(r.Mod),
                r.Version,
                r.Mod.DetailUrl,
                () => AppServices.SpModApi.FindModVersionAsync(r.Mod.Id.ToString(), r.Version)))
            .ToList();

        //
        // OPEN-32 D2: an addon whose mod is updated in the same batch takes the newest version that
        // fits the version its mod is going to, and queues behind it.
        //
        var modTargets = resolved.ToDictionary(r => r.Mod.Id, r => r.Version);
        var noFitIds = new HashSet<int>();

        foreach (var card in targets.Where(c => c.IsAddon))
        {
            var addon = AppServices.Addons.ById(card.ModId!.Value);
            var withMod = card.ParentModId is { } parentId && modTargets.ContainsKey(parentId);

            var version = withMod
                ? NewestAddonUpdate(addon, card.InstalledVersion, modTargets[card.ParentModId!.Value])
                : addon?.Versions?.FirstOrDefault(v => string.Equals(
                    v.Version, card.UpdateVersion ?? card.LatestPublishedVersion, StringComparison.OrdinalIgnoreCase));

            if (addon is null || (version is null && !withMod) || version is { Link: null })
            {
                unmatched.Add(card.DisplayTitle);
                continue;
            }

            if (version is null)
            {
                noFitIds.Add(card.ModId!.Value);
                continue;
            }

            var download = new ModVersion
            {
                Id = version.Id,
                Version = version.Version,
                Description = version.Description,
                Link = version.Link,
                ContentLength = version.ContentLength,
            };

            updates.Add(new UpdateTarget(
                card,
                InstallTarget.For(addon),
                version.Version!,
                addon.DetailUrl,
                () => Task.FromResult<ModVersion?>(download),
                version.ContentLength,
                withMod ? card.ParentModId : null));
        }

        //
        // OPEN-32 D7: every installed addon of a mod being updated, against the version that mod is
        // going to - asked about once for the whole batch. Addons already updating with it are left out.
        //
        var addonsInBatch = updates.Where(u => u.Target.IsAddon).Select(u => u.Target.Id).ToHashSet();
        var addonCards = new Dictionary<int, InstalledModCardViewModel>();
        var impacts = new List<AddonImpactItem>();

        if (resolved.Count > 0)
        {
            StatusMessage = Strings.AddonImpact_Checking;
            var check = new AddonImpactCheck();

            foreach (var (card, mod, version) in resolved)
            {
                var cards = AddonCardsOf(card).Where(c => c.ModId is { } id && !addonsInBatch.Contains(id)).ToList();
                foreach (var addonCard in cards) addonCards[addonCard.ModId!.Value] = addonCard;

                impacts.AddRange(await check.AssessAsync(AddonImpactCheck.From(cards), mod.Id, mod.Name ?? card.DisplayTitle, version));
            }

            StatusMessage = null;
        }

        // Follows Monitor mode's setting. A download places nothing, so the warning below is moot.
        var downloadOnly = AppServices.ModPageGate.DownloadOnlyFor(alternate: false);

        switch (AddonImpactCheck.Ask(impacts, willInstall: !downloadOnly))
        {
            case AddonImpactAnswer.Cancel:
                StatusMessage = Strings.AddonImpact_Cancelled;
                return;
            case AddonImpactAnswer.UpdateBoth:
                foreach (var impact in impacts)
                {
                    if (impact.Addon.Listing is not { } listing || AddonImpactCheck.Download(impact) is not { } download) continue;

                    updates.Add(new UpdateTarget(
                        addonCards[impact.Addon.AddonId],
                        InstallTarget.For(listing),
                        download.Version ?? string.Empty,
                        listing.DetailUrl,
                        () => Task.FromResult<ModVersion?>(download),
                        download.ContentLength,
                        impact.ModId));
                }

                break;
        }

        var disableOverridden = AddonImpactCheck.AskOverridden(impacts, willInstall: !downloadOnly);

        var noFit = noFitIds.Count(id => !impacts.Any(i => i.IsAffected && i.Addon.AddonId == id));

        if (updates.Count == 0)
        {
            var nothing = unmatched.Count > 0
                ? Text(Strings.Installed_UpdateNotInCatalogFormat, TextLists.Join(unmatched))
                : DescribeSkipped(selected, unmatched, noFit).Trim();
            StatusMessage = (leftOut.Length, nothing.Length) switch
            {
                (0, _) => nothing,
                (_, 0) => leftOut,
                _ => Sentences(leftOut, nothing),
            };
            return;
        }

        //
        // One warning covering every hand-installed mod in the batch, for the same reason the
        // single-mod path warns at all: there is no record of which files the current version
        // placed, so the new one goes on top of it.
        //
        var handInstalled = updates.Where(u => !u.Card.IsAppManaged).Select(u => u.Card.DisplayTitle).ToList();
        if (!downloadOnly && handInstalled.Count > 0 && !Confirm(
                Strings.Installed_UpdateHandInstalledTitle(handInstalled.Count),
                Text(Strings.Installed_UpdateHandInstalledBodyFormat, TextLists.Join(handInstalled))))
        {
            StatusMessage = Strings.Installed_UpdateCancelled;
            return;
        }

        // The same gate a single install goes through, asked once for the batch. ConfirmAll honours
        // the Options switch that turns the gate off.
        var links = updates.Select(u => new ModPageLink(u.Card.DisplayTitle, u.DetailUrl)).ToList();
        if (!ReadModPageConfirmationWindow.ConfirmAll(links))
        {
            StatusMessage = Strings.Installed_UpdateCancelledUnread;
            return;
        }

        if (!downloadOnly) AppServices.AddonFollowUps.DisableAfterUpdate(impacts, disableOverridden);

        var queuedMods = new Dictionary<int, DownloadQueueItemViewModel>();

        foreach (var update in updates)
        {
            var item = AppServices.DownloadQueue.Enqueue(
                update.Target, update.Version, installPath, update.Resolve,
                dependencyOf: update.QueueBehindModId is { } modId ? queuedMods[modId] : null,
                totalBytes: update.TotalBytes,
                downloadOnly: downloadOnly);

            if (!update.Target.IsAddon) queuedMods[update.Target.Id] = item;
        }

        var queued = Strings.Installed_UpdateQueued(updates.Count);
        var summary = queued + DescribeSkipped(selected, unmatched, noFit);
        StatusMessage = leftOut.Length == 0 ? summary : Sentences(summary, leftOut);

        AppLog.Info("Installed", $"queued {updates.Count} update(s) from a selection of {selected.Count}");
    }

    //
    // OPEN-32 D11, after an install's rescan: disables the addons left with no fitting version by the
    // update that just landed, then offers back any disabled that way that fit their mod again.
    //
    private async Task FollowUpAddonFitAsync()
    {
        var due = AppServices.AddonFollowUps.TakeDue(
            modId => _all.FirstOrDefault(c => c is { IsAddon: false } && c.ModId == modId)?.InstalledVersion);
        foreach (var kept in due.Where(d => d.Keep)) AppServices.InstallManifest.SetFitOverride(kept.AddonId, kept.ModVersion);
        var dueIds = due.Where(d => !d.Keep).Select(d => d.AddonId).ToHashSet();

        var toDisable = _all.Where(c => c is { IsAddon: true, IsDisabled: false, ModId: { } id } && dueIds.Contains(id)).ToList();
        if (toDisable.Count > 0 && await ApplyDisableAsync(toDisable, disable: true, confirm: false) is not null)
        {
            AppServices.InstallManifest.SetDisabledForFit(toDisable.Select(c => c.ModId!.Value), true);
            StatusMessage = Text(Strings.AddonFit_DisabledFormat, TextLists.Join([.. toDisable.Select(c => c.DisplayTitle)]));
        }

        var flagged = AppServices.InstallManifest.Load().Mods
            .Where(r => r is { IsAddon: true, DisabledForFit: true })
            .Select(r => r.ModId)
            .ToHashSet();
        if (flagged.Count == 0) return;

        var fitting = _all.Where(c => c is { IsAddon: true, IsDisabled: true, FitsParent: true, ModId: { } id } && flagged.Contains(id)).ToList();
        if (fitting.Count == 0) return;

        var lines = fitting.Select(c => Text(
            Strings.AddonImpact_NoVersionLineFormat, c.DisplayTitle, c.InstalledVersion, c.ParentModName, c.ParentInstalledVersion));

        var answer = MessageBox.Show(
            Strings.AddonFit_ReEnableList + "\n" + string.Join("\n", lines) + "\n\n" + Strings.AddonFit_ReEnableQuestion,
            Strings.AddonFit_ReEnableTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        AppServices.InstallManifest.SetDisabledForFit(fitting.Select(c => c.ModId!.Value), false);
        if (answer == MessageBoxResult.Yes) await ApplyDisableAsync(fitting, disable: false, confirm: false);
    }

    // As the addon card picks its update (BuildAddonCard), against the version its mod is going to.
    private static AddonVersionSummary? NewestAddonUpdate(Addon? addon, string? installedVersion, string modVersion)
    {
        var newest = (addon?.Versions ?? [])
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault(v => AddonFit.Accepts(v.ModVersionConstraint, modVersion, AppServices.SptEnvironment.InstalledVersion));

        return newest is not null && ModVersionComparer.IsUpdateAvailable(installedVersion, newest.Version) == true
            ? newest
            : null;
    }

    //
    // Why a selection that looked updatable produced nothing. Each reason is separate because they
    // need different things done about them - enabling a mod, opening its parent, or a rescan.
    //
    private static string DescribeNothingToUpdate(IReadOnlyList<InstalledModCardViewModel> selected)
    {
        if (selected.Count == 0) return Strings.Installed_NothingSelected;

        var disabled = selected.Count(c => c is { UpdateAvailable: true, IsDisabled: true });

        var reasons = new List<string>();

        if (disabled > 0) reasons.Add(Strings.Installed_ReasonDisabled(disabled));

        return reasons.Count == 0
            ? Strings.Installed_NoUpdatesAvailable
            : Text(
                Strings.Installed_NoUpdatesReasonsFormat,
                string.Join(Strings.Common_ListSeparator, reasons));
    }

    private static string DescribeSkipped(
        IReadOnlyList<InstalledModCardViewModel> selected,
        IReadOnlyList<string> unmatched,
        int addonsWithNoFit)
    {
        var parts = new List<string>();

        var disabled = selected.Count(c => c is { UpdateAvailable: true, IsDisabled: true });
        var noUpdate = selected.Count(c => c.UpdateAvailable != true);

        if (noUpdate > 0) parts.Add(Strings.Installed_SkippedUpToDate(noUpdate));
        if (disabled > 0) parts.Add(Strings.Installed_SkippedDisabled(disabled));
        if (addonsWithNoFit > 0) parts.Add(Strings.Installed_SkippedAddonNoFit(addonsWithNoFit));
        if (unmatched.Count > 0) parts.Add(Strings.Installed_SkippedNotFound(unmatched.Count));

        return parts.Count == 0
            ? string.Empty
            : Text(Strings.Installed_SkippedFormat, string.Join(Strings.Common_ListSeparator, parts));
    }
}
