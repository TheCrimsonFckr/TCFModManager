using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    // Whether this page has scanned at least once - see the UpdatesFound subscription below.
    private bool _hasScanned;

    public bool HasScanned => _hasScanned;

    // InstallFingerprint as of the last scan - see RefreshOnReturnAsync.
    private string? _fingerprint;

    //
    // A return visit to the page. Rescanning replaces every card and rebuilds the view, which is
    // the expensive part, so it is skipped when nothing the page reads has changed since the last
    // scan. The Rescan button still always rescans.
    //
    public async Task RefreshOnReturnAsync()
    {
        if (ScanCommand.IsRunning) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (_fingerprint is { } last && !string.IsNullOrWhiteSpace(installPath))
        {
            var catalog = AppServices.ModCache.AllMods;
            var addons = AppServices.Addons.AllAddons;
            var sptVersion = AppServices.SptEnvironment.InstalledVersion;

            var now = await Task.Run(() => InstallFingerprint.Compute(installPath, catalog, addons, sptVersion));
            if (now == last) return;

            AppLog.Debug("Installed", "install changed since the last scan - rescanning");
        }

        if (ScanCommand.IsRunning) return;
        await ScanCommand.ExecuteAsync(null);
    }

    //
    // Rescans once a queued install has landed, so a mod installed or updated from Browse, a card
    // or a mod list shows as it now is without pressing Rescan. A mod and its dependencies install
    // one after another, so the scan waits for the run to go quiet and then happens once; an install
    // that lands while a scan is running gets a scan of its own afterwards rather than being missed.
    // Download-only (Monitor) items never raise ItemInstalled - nothing in the install changed.
    //
    private bool _installRescanPending;

    private bool _installRescanRunning;

    private static readonly TimeSpan InstallRescanQuiet = TimeSpan.FromMilliseconds(750);

    private async Task RescanAfterInstallAsync()
    {
        // Before the first scan there is nothing stale; that scan will see the new mod.
        if (!_hasScanned) return;

        _installRescanPending = true;
        if (_installRescanRunning) return;

        _installRescanRunning = true;
        try
        {
            while (_installRescanPending)
            {
                _installRescanPending = false;
                await Task.Delay(InstallRescanQuiet);
                if (_installRescanPending) continue;

                while (ScanCommand.IsRunning) await Task.Delay(200);

                AppLog.Debug("Installed", "a queued install finished - rescanning");
                await ScanCommand.ExecuteAsync(null);
            }
        }
        finally
        {
            _installRescanRunning = false;
        }
    }

    //
    // Tags each card with the mod lists that name it. Worked out through ModListMembership, which
    // runs the real planner, so a badge can never disagree with what applying that list would do.
    //
    // Runs per scan rather than being watched: the Installed page rescans when it is navigated to,
    // so editing a list on the Mod lists page and coming back here shows the change.
    //
    private void ApplyListMembership(IReadOnlyList<InstalledModCardViewModel> cards)
    {
        var lists = AppServices.ModLists.Load().Lists;
        if (lists.Count == 0)
        {
            foreach (var card in cards) card.Lists = [];
            return;
        }

        var names = ModListMembership.Names(lists, ModListCandidates.From(cards));

        for (var i = 0; i < cards.Count; i++) cards[i].Lists = names[i];
    }

    private static void ApplyPins(IReadOnlyList<InstalledModCardViewModel> cards)
    {
        var pins = AppServices.ModLists.GetPins();

        foreach (var card in cards)
            card.IsPinned = pins.Count > 0 && ModListPlanner.IsPinned(ModListCandidates.From(card), pins);
    }

    [RelayCommand]
    private void TogglePin(InstalledModCardViewModel? mod)
    {
        if (mod is null) return;

        var pin = !mod.IsPinned;
        AppServices.ModLists.SetPinned(ModListPlanner.PinKeys(ModListCandidates.From(mod)), pin);
        mod.IsPinned = pin;

        StatusMessage = Text(
            pin ? Strings.Installed_PinnedFormat : Strings.Installed_UnpinnedFormat,
            mod.DisplayTitle);
        AppLog.Info("Installed", $"{(pin ? "pinned" : "unpinned")} {mod.Name} against mod list disables");
    }

    //
    // Re-reads the install and rebuilds every card. Deliberately leaves you on the page you were
    // on: a scan changes what the list holds, not which part of it you were looking at.
    //
    // This used to take a resetPage flag, true for the Rescan button and the page's Loaded handler
    // and false for everything else. That is what sent you back to page 1 on closing the mod
    // dialog - Loaded fires again every time the page is re-attached, not just the first time, and
    // any scan that went through the flag-true path clobbered the page. Resetting to page 1
    // belongs to a *filter* change, where the list you are paging through is genuinely different,
    // and AutoApplyFilter already does exactly that.
    //
    [RelayCommand]
    private async Task ScanAsync()
    {
        _hasScanned = true;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _all = [];
            _fingerprint = null;
            ApplyFilter();
            RefreshActiveView(CurrentPage);
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        IsBusy = true;
        try
        {
            // Reuses whatever's already cached; triggers the one-time catalog fetch if Browse
            // hasn't been visited yet this session. Offline with no copy saved yet, the mods in the
            // SPT folder are still listed - scanning them needs no network - just without what
            // sp-mod knows about them.
            var catalogUnavailable = false;
            try
            {
                await AppServices.ModCache.EnsureLoadedAsync();
                await AppServices.Addons.EnsureLoadedAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Installed", $"catalog unavailable, listing the SPT folder only: {ex.GetType().Name}: {ex.Message}");
                catalogUnavailable = true;
            }

            // Addon records written before slugs were stored (OPEN-31 R4).
            if (AppServices.Addons.AllAddons.Count > 0)
            {
                var slugs = AppServices.Addons.AllAddons
                    .Where(a => !string.IsNullOrWhiteSpace(a.Slug))
                    .GroupBy(a => a.Id)
                    .ToDictionary(g => g.Key, g => g.First().Slug);

                var filled = AppServices.InstallManifest.FillAddonSlugs(id => slugs.GetValueOrDefault(id));
                if (filled > 0) AppLog.Info("Installed", $"filled in the slug of {filled} addon record(s)");
            }

            // What this app itself installed, and which folders it placed - identifies those mods
            // exactly instead of inferring them from folder names.
            var installRecords = AppServices.InstallManifest.Load().Mods;

            // Read once here rather than inside the background work, so a catalog refresh landing
            // mid-scan can't swap the list out from under it.
            var catalog = AppServices.ModCache.AllMods;
            var addons = AppServices.Addons.AllAddons;
            var sptVersion = AppServices.SptEnvironment.InstalledVersion;

            // The whole scan-and-match pass runs off the UI thread. Matching a large install
            // against a full catalog is the slower half of the two, and doing it inline is what
            // made navigating to this page hang.
            var (scanned, cards, dependencies, downloads, conflicts, fingerprint) = await Task.Run(() =>
            {
                var found = InstalledScan.Run(installPath, catalog, installRecords);

                var built = InstalledModCardViewModel.BuildFrom(found, catalog, sptVersion, installRecords, addons)
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Built off the same scan the cards came from, so every link points at an entry
                // some card owns.
                var dependencyGraph = ModDependencyGraph.Build(found);
                var pendingDownloads = MatchDownloads(found, installPath, installRecords);
                var foundConflicts = ModConflicts.Find(built);

                // Taken last, after MatchDownloads, which can itself update the download ledger.
                var stamp = InstallFingerprint.Compute(installPath, catalog, addons, sptVersion);

                return (found, built, dependencyGraph, pendingDownloads, foundConflicts, stamp);
            });

            _fingerprint = fingerprint;

            // What was open in each view, keyed the same way group assignments are, so the sets
            // survive every card object being replaced. Captured before _all is reassigned, and
            // kept apart because the two views expand independently.
            var openCards = OpenKeys(_all, m => m.IsCardExpanded);
            var openRows = OpenKeys(_all, m => m.IsRowExpanded);

            // The ticks too, so the rescan after an install doesn't untick the rest of a selection.
            var ticked = OpenKeys(_all, m => m.IsSelected);

            _all = cards;
            RefreshUndo();

            if (openCards.Count > 0 || openRows.Count > 0 || ticked.Count > 0)
                foreach (var card in cards)
                {
                    var key = ModGroupStore.KeyFor(card.Name);
                    card.IsCardExpanded = openCards.Contains(key);
                    card.IsRowExpanded = openRows.Contains(key);
                    card.IsSelected = ticked.Contains(key);
                }

            ApplyListMembership(cards);
            ApplyPins(cards);
            ApplyPendingDownloads(cards, downloads);
            ApplyLeftovers(cards);
            ModConflicts.Apply(cards, conflicts);
            DependencyVersions.Apply(DependencyVersions.Solve(cards, AppServices.SptEnvironment.InstalledVersion));
            ConflictCountLabel = conflicts.Count == 0 ? null : Strings.Installed_ConflictCount(conflicts.Count, conflicts.Count);
            var notForSpt = cards.Count(c => !c.IsDisabled && c.IsNotForThisSpt);
            NotForSptLabel = notForSpt == 0 ? null
                : Strings.Installed_NotForSptCount(notForSpt, notForSpt, AppServices.SptEnvironment.InstalledVersion);
            _notForSptKeys = cards.Where(c => !c.IsDisabled && c.IsNotForThisSpt).Select(c => c.SptWarningKey).ToList();
            _dismissedSptKeys = cards.Where(c => c.HasSptWarning && c.IsSptWarningDismissed).Select(c => c.SptWarningKey).ToList();
            HasDismissedSptWarnings = _dismissedSptKeys.Count > 0;
            if (conflicts.Count > 0)
            {
                AppLog.Info("Conflicts", string.Join("; ", conflicts.Select(c =>
                    $"{c.Kind} {c.Identifier}: {string.Join(", ", c.Members.Select(m => m.Entry.Name).Distinct())}")));
            }
            ApplyBadgeVisibility();
            _dependencies = dependencies;

            // Set before the cards are subscribed to below, so filling it in doesn't read as a
            // card changing under the page.
            foreach (var card in _all)
                card.HasDependencies = card.Entries.Any(e => dependencies.DependenciesOf(e).Count > 0);

            RebuildCategoryOptions();

            _cardByEntry = [];
            foreach (var card in _all)
            {
                card.PropertyChanged += OnCardPropertyChanged;
                foreach (var entry in card.Entries) _cardByEntry[entry] = card;
            }

            SeedAddonGroups();
            RefreshGroups();

            // A rescan replaces every card; the ticks were carried over by key above, the anchor is not.
            _selectionAnchor = null;
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedCountLabel));
            DisableSelectedCommand.NotifyCanExecuteChanged();
            EnableSelectedCommand.NotifyCanExecuteChanged();
            UpdateSelectedCommand.NotifyCanExecuteChanged();
            RemoveSelectedCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(AllSelected));
            OnPropertyChanged(nameof(SelectAllLabel));

            var unmatched = _all.Where(m => m.ModId is null).Select(m => m.Name).ToList();
            AppLog.Info("Installed",
                $"scanned {scanned.Count} folder(s) -> {_all.Count} card(s); " +
                $"{_all.Count(m => m.IsAppManaged)} app-managed, {_all.Count(m => m.IsDisabled)} disabled, " +
                $"{unmatched.Count} unmatched; on page {CurrentPage} of {TotalPages}");
            if (unmatched.Count > 0) AppLog.Debug("Installed", $"unmatched: {string.Join(", ", unmatched)}");

            var mixed = _all.Where(m => m.IsMixedState).Select(m => m.Name).ToList();
            if (mixed.Count > 0)
                AppLog.Warn("Installed", $"present in both an enabled and a disabled folder: {string.Join(", ", mixed)}");

            ApplyFilter();
            RefreshActiveView(CurrentPage);

            StatusMessage = _all.Count == 0
                ? Text(Strings.Installed_NoModsFoundFormat, installPath)
                : DescribeCounts();

            if (catalogUnavailable)
                StatusMessage = string.Join(Strings.Common_SentenceSeparator, StatusMessage, Strings.Installed_CatalogUnavailable);
        }
        finally
        {
            IsBusy = false;
        }

        await OfferDownloadConfirmationsAsync();

        // Asked after the cards are on screen, so the page never waits on sp-mod. A changed answer
        // rescans (the Changed subscription above); the same answer does nothing.
        _ = RefreshHeldBackAsync();
    }

    // OPEN-12 F11: which of the installed mods' updates sp-mod holds back - see HeldBackUpdates.
    private Task RefreshHeldBackAsync()
    {
        var installed = _all
            .Where(c => c is { IsAddon: false, ModId: > 0 } && !string.IsNullOrWhiteSpace(c.InstalledVersion))
            .Select(c => (c.ModId!.Value, c.InstalledVersion!))
            .DistinctBy(c => c.Item1)
            .ToList();

        return AppServices.HeldBack.RefreshAsync(installed, AppServices.SptEnvironment.InstalledVersion);
    }

    //
    // Monitor mode, §7: every pending download checked against this scan. Runs on the scan's
    // background thread, since the size check is one stat per file the archive would place.
    //
    // A download this app has since installed itself - its record is app-managed at the same
    // version - is settled here without asking: there is nothing for the user to confirm.
    //
    private static List<PendingDownload> MatchDownloads(
        IReadOnlyList<InstalledMod> scanned, string installPath, IReadOnlyList<InstalledModRecord> records)
    {
        var pending = AppServices.DownloadLedger.Pending();
        if (pending.Count == 0) return [];

        var folders = DownloadMatcher.FolderNames(scanned);
        var results = new List<PendingDownload>();

        foreach (var download in pending)
        {
            var record = records.FirstOrDefault(r => r.ModId == download.ModId && r.IsAddon == download.IsAddon);
            if (record is { IsAppManaged: true, Incomplete: false }
                && string.Equals(record.Version, download.Version, StringComparison.Ordinal))
            {
                AppServices.DownloadLedger.SetState(
                    download.ModId, download.IsAddon, download.Version, DownloadState.Confirmed);
                continue;
            }

            var match = DownloadMatcher.Check(download, installPath, folders);
            if (match.Kind != DownloadMatchKind.Absent) results.Add(new PendingDownload(download, match));
        }

        AppLog.Debug("Monitor",
            $"{pending.Count} pending download(s): " +
            $"{results.Count(r => r.Match.Kind == DownloadMatchKind.Installed)} look installed, " +
            $"{results.Count(r => r.Match.Kind == DownloadMatchKind.Partial)} partly");

        return results;
    }

    //
    // Ties each matched download to its card: by listing id first, and by folder when the card
    // resolved to a different listing (or none) - the folder is what the download named.
    //
    private static void ApplyPendingDownloads(
        IReadOnlyList<InstalledModCardViewModel> cards, IReadOnlyList<PendingDownload> downloads)
    {
        foreach (var card in cards) card.PendingDownload = null;

        foreach (var download in downloads)
        {
            var d = download.Download;
            var expected = d.ExpectedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var card = cards.FirstOrDefault(c => c.ModId == d.ModId && c.IsAddon == d.IsAddon)
                ?? cards.FirstOrDefault(c => c.Entries.Any(e => !e.IsDisabled && expected.Overlaps(
                    DownloadMatcher.FolderNames([e]))));

            if (card is not null) card.PendingDownload = download;
        }
    }

    //
    // R2's default: one prompt per scan listing every download that now looks installed, each with
    // a tick box. Ticked ones are confirmed, unticked ones dismissed for that version; "Not now"
    // leaves them all pending and doesn't ask again this session. The quiet setting skips the
    // prompt, and the card's own Confirm install button does the same job one mod at a time.
    //
    private async Task OfferDownloadConfirmationsAsync()
    {
        if (new SettingsService().Load().Monitor.DownloadConfirmation != DownloadConfirmation.Ask) return;

        var offered = _all
            .Where(c => c.CanConfirmDownload)
            .Select(c => c.PendingDownload!)
            .Where(p => !_deferredDownloads.Contains(Key(p.Download)))
            .GroupBy(p => Key(p.Download))
            .Select(g => g.First())
            .ToList();

        if (offered.Count == 0) return;

        var answer = DownloadConfirmWindow.Ask(offered.Select(p => p.Download).ToList());

        if (answer is null)
        {
            foreach (var p in offered) _deferredDownloads.Add(Key(p.Download));
            AppLog.Info("Monitor", $"confirm prompt deferred for {offered.Count} download(s)");
            return;
        }

        foreach (var p in offered)
        {
            var d = p.Download;

            if (answer.Contains(d))
            {
                AppServices.InstallManifest.ConfirmDownload(d, AppServices.SptEnvironment.InstallPath);
                AppServices.DownloadLedger.SetState(d.ModId, d.IsAddon, d.Version, DownloadState.Confirmed);
            }
            else
            {
                AppServices.DownloadLedger.SetState(d.ModId, d.IsAddon, d.Version, DownloadState.Dismissed);
            }
        }

        AppLog.Info("Monitor", $"confirmed {answer.Count} of {offered.Count} download(s) from the prompt");

        await ScanAsync();

        if (answer.Count > 0) StatusMessage = Strings.Installed_DownloadsConfirmed(answer.Count);
    }

    private static (int, bool, string) Key(DownloadedModRecord d) => (d.ModId, d.IsAddon, d.Version);

    // The card's own Confirm install - the quiet setting's way in, and always available on a card
    // whose download looks installed.
    [RelayCommand]
    private async Task ConfirmDownloadAsync(InstalledModCardViewModel? mod)
    {
        if (mod?.PendingDownload is not { Match.Kind: DownloadMatchKind.Installed } pending) return;

        var d = pending.Download;
        AppServices.InstallManifest.ConfirmDownload(d, AppServices.SptEnvironment.InstallPath);
        AppServices.DownloadLedger.SetState(d.ModId, d.IsAddon, d.Version, DownloadState.Confirmed);

        AppLog.Info("Monitor", $"confirmed {d.Name} {d.Version} from its card");

        await ScanAsync();

        StatusMessage = Text(Strings.Installed_DownloadConfirmedFormat, d.Name, d.Version);
    }
}
