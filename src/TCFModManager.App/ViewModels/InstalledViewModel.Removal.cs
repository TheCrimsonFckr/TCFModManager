using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    /// <summary>Removes a mod from the install: the manifest-precise ModInstallService.UninstallAsync path
    /// for anything IsAppManaged, or RemoveHandInstalled (move the mod's whole folder into holding) otherwise. Both paths
    /// confirm first.</summary>
    [RelayCommand]
    private async Task RemoveAsync(InstalledModCardViewModel? mod)
    {
        if (mod is null) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        // A disabled mod's install record still points at the folders it was installed into, which
        // it no longer occupies - so removal would delete nothing and report success. Enabling it
        // first puts those paths back where the record expects them.
        if (mod.IsDisabled)
        {
            StatusMessage = Text(Strings.Installed_RemoveDisabledFirstFormat, mod.DisplayTitle);
            return;
        }

        if (AskRemoveAddons(mod) is not { } addons) return;
        var batch = addons.Count > 0 ? RemovalBatch.For(mod.Name) : null;

        if (mod.IsAppManaged && mod.ModId is { } modId)
        {
            var manifest = AppServices.InstallManifest.Load();
            var record = manifest.Find(modId, mod.IsAddon);
            if (record is null)
            {
                StatusMessage = Text(Strings.Installed_RemoveNoRecordFormat, mod.Name);
                return;
            }

            //
            // Counted with the mod's own entry in hand, so a mod that keeps settings somewhere
            // unconventional - or whose presets this app never installed - is counted the same way the
            // removal itself will treat them.
            //
            var options = new ModConfigOptionsStore().Effective();

            var configs = ModConfigFiles.InRecord(record, options)
                .Concat(ModConfigFiles.UserDataOnDisk(installPath, record, options))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            //
            // A record made before v1.19.0 doesn't say which install it came from (D17). When the app
            // doesn't sit inside this install either, nothing proves it belongs here - so the install
            // is named before anything happens.
            //
            var body = record.InstallPath is null && !InstallStamp.AppIsInside(installPath)
                ? Sentences(Strings.Installed_RemoveAppManagedBody, Text(Strings.Installed_RemoveUnstampedFormat, installPath))
                : Strings.Installed_RemoveAppManagedBody;

            if (ConfirmRemoval(mod.Name, WithProfileWarning(mod, body), configs.Count)
                is not { } configAction)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var removedAddons = await RemoveAddonsAsync(addons, installPath, batch);
                var result = await AppServices.ModInstall.UninstallAsync(installPath, record, configAction, batch: batch);
                StatusMessage = WithRemovedAddons(
                    DescribeRemoval(mod.Name, result.FailedFiles.Count, result.ConfigsKept, result.ConfigsFolder, result), removedAddons);
                ModRemoved?.Invoke(this, EventArgs.Empty);
            }
            catch (ModInstallException ex)
            {
                // SPT or its server is running - the message names what to close.
                StatusMessage = ModInstallProblems.Describe(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }
        else
        {
            var paths = new[] { mod.ClientFolderPath, mod.ServerFolderPath }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (paths.Count == 0)
            {
                StatusMessage = Text(Strings.Installed_RemoveNoFolderFormat, mod.Name);
                return;
            }

            //
            // Every folder is checked before the user is asked and before anything moves, so a mod
            // with one acceptable folder and one refused one is left whole rather than half removed.
            //
            foreach (var path in paths)
            {
                if (InstallPathGuard.CheckModFolder(installPath, path!) is { } refusal)
                {
                    StatusMessage = ModInstallProblems.RemovalRefused(path, refusal);
                    return;
                }
            }

            var configs = ModInstallService.FindLegacyConfigs(installPath, paths!);
            if (ConfirmRemoval(
                    mod.Name,
                    WithProfileWarning(mod, Text(Strings.Installed_RemoveLegacyBodyFormat, string.Join("\n", HandRemovalShown(mod, paths.Select(p => p!))))),
                    configs.Count) is not { } configAction)
            {
                return;
            }

            IsBusy = true;
            try
            {
                // Checked before the configs move, so SPT running can't leave them moved out of a mod
                // whose folder then stays put.
                ModInstallService.EnsureInstallNotInUse(ModInstallAction.Remove, installPath);

                var kept = configAction == ConfigAction.Keep && configs.Count > 0
                    ? ModInstallService.KeepLegacyConfigs(installPath, configs, mod.Name)
                    : new KeptConfigs(0, null);

                var removedAddons = await RemoveAddonsAsync(addons, installPath, batch);
                var held = RemoveByHand(mod, [.. paths.Select(p => p!)], installPath, kept.Folder, batch);

                // A manually-confirmed version record would otherwise dangle, pointing at a mod
                // that's no longer on disk.
                if (mod.IsManualOverride && mod.ModId is { } overriddenModId)
                    AppServices.InstallManifest.ClearManualVersion(overriddenModId, mod.IsAddon);

                StatusMessage = WithRemovedAddons(
                    DescribeRemoval(mod.Name, failedFiles: 0, kept.Count, kept.Folder, held: held is not null), removedAddons);
                ModRemoved?.Invoke(this, EventArgs.Empty);
            }
            catch (ModInstallException ex)
            {
                StatusMessage = ModInstallProblems.Describe(ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusMessage = Text(Strings.Installed_RemoveFailedFormat, mod.Name, ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        await ScanAsync();
    }

    //
    // Removes every ticked mod after ONE question - the same reasoning as Update selected: a
    // question per mod over a long selection trains people to stop reading. Their config files are
    // always kept (LegacyConfigs), the safe half of the single removal's choice, and every removal can
    // be undone from the holding folder as usual. Mods sp-mod marks as changing the profile are named
    // in the question (OPEN-12 F17). A disabled mod is left out: its record points at folders it no
    // longer occupies, the same reason a single removal asks to enable it first.
    //
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveSelectedAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var selected = SelectedCards();
        var targets = selected.Where(m => !m.IsDisabled).ToList();
        var leftDisabled = selected.Where(m => m.IsDisabled).Select(m => m.DisplayTitle).ToList();

        if (targets.Count == 0)
        {
            StatusMessage = Text(Strings.Installed_RemoveSelectedAllDisabledFormat, TextLists.Join(leftDisabled));
            return;
        }

        var unselectedAddons = targets
            .SelectMany(AddonCardsOf)
            .Where(a => !a.IsDisabled && !selected.Contains(a))
            .Distinct()
            .ToList();

        if (unselectedAddons.Count > 0)
        {
            var answer = MessageBox.Show(
                Text(Strings.Installed_RemoveSelectedAddonsFormat, string.Join(Strings.Common_ListSeparator, unselectedAddons.Select(a => a.DisplayTitle))),
                Strings.Installed_RemoveSelectedTitle,
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            if (answer is not (MessageBoxResult.Yes or MessageBoxResult.No))
            {
                StatusMessage = Strings.Installed_RemoveSelectedCancelled;
                return;
            }

            if (answer == MessageBoxResult.Yes) targets.AddRange(unselectedAddons);
        }

        var body = new List<string> { Text(Strings.Installed_RemoveSelectedBodyFormat, string.Join("\n", targets.Select(t => t.DisplayTitle)), AppPaths.LegacyConfigsDirectory) };

        var profileMods = targets.Where(ChangesProfile).Select(t => t.DisplayTitle).ToList();
        if (profileMods.Count > 0) body.Insert(0, Text(Strings.Installed_RemoveSelectedProfileFormat, string.Join("\n", profileMods)));

        if (leftDisabled.Count > 0) body.Add(Text(Strings.Installed_RemoveSelectedLeavesDisabledFormat, TextLists.Join(leftDisabled)));

        body.Add(HeldSentence());

        if (!Confirm(Strings.Installed_RemoveSelectedTitle, string.Join("\n\n", body)))
        {
            StatusMessage = Strings.Installed_RemoveSelectedCancelled;
            return;
        }

        var removed = 0;
        var failed = new List<string>();

        IsBusy = true;
        try
        {
            var manifest = AppServices.InstallManifest.Load();

            foreach (var (mod, batch) in RemovalOrder(targets))
            {
                try
                {
                    if (await RemoveOneQuietlyAsync(mod, installPath, manifest, batch)) removed++;
                    else failed.Add(mod.DisplayTitle);
                }
                catch (ModInstallException ex)
                {
                    AppLog.Warn("Remove", $"{mod.Name}: {ModInstallProblems.Describe(ex)}");
                    failed.Add(mod.DisplayTitle);

                    // SPT started part way through: nothing after this would go either.
                    if (ex.Reason == ModInstallFailure.InstallInUse)
                    {
                        failed.AddRange(RemovalOrder(targets).Select(o => o.Card).SkipWhile(t => t != mod).Skip(1).Select(t => t.DisplayTitle));
                        break;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Warn("Remove", $"{mod.Name}: {ex.Message}");
                    failed.Add(mod.DisplayTitle);
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (removed > 0) ModRemoved?.Invoke(this, EventArgs.Empty);

        await ScanAsync();

        var message = Text(Strings.Installed_RemovedSelectedFormat, removed, targets.Count);
        if (failed.Count > 0) message = Sentences(message, Text(Strings.Installed_RemoveSelectedFailedFormat, TextLists.Join(failed)));
        StatusMessage = message;
    }

    //
    // One mod of a Remove selected, with no question of its own: the app-managed path through its
    // record, or a hand-installed mod's folders moved whole into the holding folder - configs kept
    // either way. False when there was nothing it could act on (no record, no folder, a refused
    // folder); throws what the single removal would have shown.
    //
    private async Task<bool> RemoveOneQuietlyAsync(
        InstalledModCardViewModel mod, string installPath, ModInstallManifest manifest, RemovalBatch? batch = null)
    {
        if (mod.IsAppManaged && mod.ModId is { } modId)
        {
            if (manifest.Find(modId, mod.IsAddon) is not { } record)
            {
                AppLog.Warn("Remove", $"{mod.Name}: no install record");
                return false;
            }

            var result = await AppServices.ModInstall.UninstallAsync(installPath, record, ConfigAction.Keep, batch: batch);
            return result.FailedFiles.Count == 0;
        }

        var paths = new[] { mod.ClientFolderPath, mod.ServerFolderPath }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();
        if (paths.Count == 0) return false;

        foreach (var path in paths)
        {
            if (InstallPathGuard.CheckModFolder(installPath, path) is { } refusal)
            {
                AppLog.Warn("Remove", $"{mod.Name}: {path} refused ({refusal})");
                return false;
            }
        }

        ModInstallService.EnsureInstallNotInUse(ModInstallAction.Remove, installPath);

        var configs = ModInstallService.FindLegacyConfigs(installPath, paths);
        var kept = configs.Count > 0
            ? ModInstallService.KeepLegacyConfigs(installPath, configs, mod.Name)
            : new KeptConfigs(0, null);

        RemoveByHand(mod, paths, installPath, kept.Folder, batch);

        if (mod.IsManualOverride && mod.ModId is { } overriddenModId)
            AppServices.InstallManifest.ClearManualVersion(overriddenModId, mod.IsAddon);

        return true;
    }

    //
    // The order a Remove selected goes in (OPEN-31 R2): each mod after its own addons, all in one
    // batch so one Undo puts them back together; anything else alone, in the order given.
    //
    private static List<(InstalledModCardViewModel Card, RemovalBatch? Batch)> RemovalOrder(
        IReadOnlyList<InstalledModCardViewModel> targets)
    {
        var order = new List<(InstalledModCardViewModel, RemovalBatch?)>();
        var placed = new HashSet<InstalledModCardViewModel>();

        foreach (var parent in targets.Where(t => t is { IsAddon: false, ModId: not null }))
        {
            var addons = targets.Where(t => t.IsAddon && t.ParentModId == parent.ModId && !placed.Contains(t)).ToList();
            if (addons.Count == 0) continue;

            var batch = RemovalBatch.For(parent.Name);
            foreach (var addon in addons) order.Add((addon, batch));
            order.Add((parent, batch));

            placed.UnionWith(addons);
            placed.Add(parent);
        }

        order.AddRange(targets.Where(t => !placed.Contains(t)).Select(t => (t, (RemovalBatch?)null)));
        return order;
    }

    //
    // A hand-installed mod sharing a folder with others (OPEN-28 R6) gives up only its own files;
    // any folder of its own goes whole, as before. Kept configs travel with the first removal only.
    //
    private static string? RemoveByHand(
        InstalledModCardViewModel mod, IReadOnlyList<string> paths, string installPath, string? configsFolder,
        RemovalBatch? batch = null)
    {
        var shares = mod.Entries.Where(e => e.IsShare).ToList();
        if (shares.Count == 0)
            return AppServices.ModInstall.RemoveHandInstalled(paths, installPath, mod.Name, configsFolder, batch);

        string? held = null;

        foreach (var share in shares)
        {
            held = AppServices.ModInstall.RemoveHandInstalledShare(share, installPath, mod.Name, configsFolder, batch) ?? held;
            configsFolder = null;
        }

        var rest = paths.Where(p => !shares.Any(s => IsSamePath(s.FolderPath, p))).ToList();
        if (rest.Count > 0)
            held = AppServices.ModInstall.RemoveHandInstalled(rest, installPath, mod.Name, configsFolder, batch) ?? held;

        return held;
    }

    // The addon cards whose parent is this mod's card (OPEN-31).
    private List<InstalledModCardViewModel> AddonCardsOf(InstalledModCardViewModel mod) =>
        mod is { IsAddon: false, ModId: { } id }
            ? [.. _all.Where(c => c.IsAddon && c.ParentModId == id).OrderBy(c => c.DisplayTitle, StringComparer.OrdinalIgnoreCase)]
            : [];

    //
    // OPEN-31 R2: asks whether a mod's installed addons go with it, Yes by default. Returns the
    // install records of the addons to remove (empty when there are none or the answer is No), or
    // null when the user cancelled. A disabled addon is named but stays.
    //
    private List<InstalledModRecord>? AskRemoveAddons(InstalledModCardViewModel mod)
    {
        var cards = AddonCardsOf(mod);
        var live = cards.Where(c => !c.IsDisabled).ToList();
        if (live.Count == 0) return [];

        var off = cards.Where(c => c.IsDisabled).ToList();
        var parts = new List<string>
        {
            Text(Strings.Installed_RemoveAddonsListFormat, mod.DisplayTitle, Names(live)),
            Text(Strings.Installed_RemoveAddonsQuestionFormat, mod.DisplayTitle),
        };
        if (off.Count > 0) parts.Add(Text(Strings.Installed_RemoveAddonsDisabledFormat, Names(off)));

        var answer = MessageBox.Show(
            string.Join("\n\n", parts),
            Text(Strings.Installed_RemoveTitleFormat, mod.DisplayTitle),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (answer is not (MessageBoxResult.Yes or MessageBoxResult.No)) return null;
        if (answer == MessageBoxResult.No) return [];

        var manifest = AppServices.InstallManifest.Load();
        return
        [
            .. live
                .Select(c => c.ModId is { } id ? manifest.Find(id, isAddon: true) : null)
                .OfType<InstalledModRecord>()
                .Where(r => r.IsAppManaged),
        ];

        static string Names(IEnumerable<InstalledModCardViewModel> cards) =>
            string.Join(Strings.Common_ListSeparator, cards.Select(c => c.DisplayTitle));
    }

    // Removes a mod's addons ahead of the mod itself, in the same batch. Returns the names removed.
    private static async Task<List<string>> RemoveAddonsAsync(
        IReadOnlyList<InstalledModRecord> addons, string installPath, RemovalBatch? batch)
    {
        var removed = new List<string>();

        foreach (var addon in addons)
        {
            await AppServices.ModInstall.UninstallAsync(installPath, addon, ConfigAction.Keep, batch: batch);
            removed.Add(addon.Name);
        }

        return removed;
    }

    private static string WithRemovedAddons(string status, IReadOnlyList<string> removed) =>
        removed.Count == 0
            ? status
            : Sentences(status, Text(Strings.Installed_RemovedAddonsFormat, string.Join(Strings.Common_ListSeparator, removed)));

    // What a hand removal will take, as the confirmation lists it: a shared folder's own files rather
    // than the folder they sit in.
    private static IEnumerable<string> HandRemovalShown(InstalledModCardViewModel mod, IEnumerable<string> paths)
    {
        var shares = mod.Entries.Where(e => e.IsShare).ToList();

        return shares
            .SelectMany(s => s.OwnedFiles!.Select(f => Path.Combine(s.FolderPath, f.Replace('/', Path.DirectorySeparatorChar))))
            .Concat(paths.Where(p => !shares.Any(s => IsSamePath(s.FolderPath, p))));
    }

    private static bool IsSamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    //
    // OPEN-12 F17: a mod whose sp-mod page carries the profile notice ("may make permanent changes to
    // your profile, and may not be removable without starting a new profile" - traders, SVM, Skills
    // Extended) says so first in its removal question. Read from the cached catalog; an addon (no such
    // field) or a mod not in the catalog is never flagged.
    //
    private static string WithProfileWarning(InstalledModCardViewModel mod, string message) =>
        ChangesProfile(mod)
            ? string.Join("\n\n", Text(Strings.Installed_RemoveProfileWarningFormat, mod.DisplayTitle), message)
            : message;

    private static bool ChangesProfile(InstalledModCardViewModel mod) =>
        !mod.IsAddon
        && mod.ModId is { } id
        && AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id) is { ShowsProfileBindingNotice: true };

    private static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <summary>Confirms a removal and, when the mod has config files of its own, asks what should happen to
    /// them in the same prompt. Returns null when the user backed out.</summary>
    private static ConfigAction? ConfirmRemoval(string modName, string message, int configCount)
    {
        // Most people reaching for Remove are troubleshooting, where disabling does the job without
        // deleting anything - worth saying at the point they're about to delete. Then how long the
        // removed files are kept, which is what decides whether it can be undone.
        message = $"{message}\n\n{HeldSentence()}\n\n{Strings.Installed_RemoveDisableHint}";

        var title = Text(Strings.Installed_RemoveTitleFormat, modName);

        if (configCount == 0) return Confirm(title, message) ? ConfigAction.Keep : null;

        var configs = Strings.Installed_RemoveConfigs(configCount, modName, configCount);

        var choices = Text(Strings.Installed_RemoveConfigsChoicesFormat, AppPaths.LegacyConfigsDirectory);

        var answer = MessageBox.Show(
            $"{message}\n\n{configs}\n\n{choices}",
            title,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return answer switch
        {
            MessageBoxResult.Yes => ConfigAction.Keep,
            MessageBoxResult.No => ConfigAction.Delete,
            _ => null,
        };
    }

    //
    // What a removal did, in order of what the user most needs to know: whether it went, what it left
    // in place and why, what it put back, where configs went, and that it can be undone.
    //
    private static string DescribeRemoval(
        string modName, int failedFiles, int configsKept, string? configsFolder, UninstallResult? result = null, bool held = false)
    {
        var parts = new List<string>
        {
            failedFiles == 0
                ? Text(Strings.Installed_RemovedFormat, modName)
                : Strings.Installed_RemovedFailed(failedFiles, modName, failedFiles),
        };

        if (result is not null)
        {
            if (result.KeptChanged.Count > 0)
                parts.Add(Strings.Installed_RemovedKeptChanged(result.KeptChanged.Count, result.KeptChanged.Count));
            if (result.KeptOwned.Count > 0)
                parts.Add(Strings.Installed_RemovedKeptOwned(result.KeptOwned.Count, result.KeptOwned.Count));
            if (result.RefusedFiles is { Count: > 0 } refused)
                parts.Add(Strings.Installed_RemovedRefused(refused.Count, refused.Count));
            if (result.OriginalsRestored > 0)
                parts.Add(Strings.Installed_RemovedRestored(result.OriginalsRestored, result.OriginalsRestored));

            // A folder that had to stay for files the mod didn't install - otherwise it looks like a
            // mod still installed, with nothing saying why.
            foreach (var left in result.FoldersLeft)
                parts.Add(Strings.Installed_RemovedFolderLeft(left.Files, left.Files, left.Folder.Replace('/', '\\')));
        }

        if (configsKept > 0 && configsFolder is not null)
            parts.Add(Strings.Installed_RemovedConfigsKept(configsKept, configsKept, configsFolder));

        if (held || result?.HoldingFolder is not null)
            parts.Add(Strings.Installed_RemovedUndoHint);

        return string.Join(Strings.Common_SentenceSeparator, parts);
    }

    //
    // The sentence the removal confirmation ends with: how long removed files are kept, from the
    // Keep removed mods setting (D27).
    //
    internal static string HeldSentence() => new SettingsService().Load().RemovedModsRetention switch
    {
        RemovedModsRetention.DeleteStraightAway => Strings.Installed_RemoveNotHeld,
        RemovedModsRetention.UntilCleared => Strings.Installed_RemoveHeldUntilCleared,
        var days => Text(Strings.Installed_RemoveHeldFormat, OptionsViewModel.RetentionLabel(days)),
    };
}
