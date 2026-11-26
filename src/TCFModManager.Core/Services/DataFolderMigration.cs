namespace TCFModManager.Core.Services;

//
// TEMPORARY, ADDED IN v1.32.0 - DELETE WHEN THE APP LEAVES BETA, along with its call in
// App.OnStartup, AppPaths.DataRename and the oldName half of AppPaths.ResolveDirectory.
//
// OPEN-74: the app's own folders and files took camelCase names. An older build's names are
// renamed once, before anything reads them.
//
public static class DataFolderMigration
{
    public enum Outcome
    {
        Nothing,
        Renamed,
        BothKept,
        Failed,
    }

    public static readonly IReadOnlyList<(string Old, string New)> BesideExe =
    [
        ("Staging", "staging"),
    ];

    public static readonly IReadOnlyList<(string Old, string New)> Folders =
    [
        ("ConfigBaselines", "configBaselines"),
        ("GameLogs", "gameLogs"),
        ("LegacyConfigs", "legacyConfigs"),
        ("ProfileBackups", "profileBackups"),
        ("WebView2", "webView2"),
        ("config-backups", "configBackups"),
        ("install-journal", "installJournal"),
    ];

    public static readonly IReadOnlyList<(string Old, string New)> Files =
    [
        ("accepted_dependency_warnings.json", "acceptedDependencyWarnings.json"),
        ("addon_cache.json", "addonCache.json"),
        ("broken_mod_result.json", "brokenModResult.json"),
        ("broken_mod_search.json", "brokenModSearch.json"),
        ("config_updates.json", "configUpdates.json"),
        ("dependency_flags.json", "dependencyFlags.json"),
        ("dismissed_spt_warnings.json", "dismissedSptWarnings.json"),
        ("download-queue.json", "downloadQueue.json"),
        ("headless_advice.json", "headlessAdvice.json"),
        ("headless_advice_kept.json", "headlessAdviceKept.json"),
        ("installed-mods.json", "installedMods.json"),
        ("mod-links.json", "modLinks.json"),
        ("mod_cache.json", "modCache.json"),
        ("mod_configs.json", "modConfigs.json"),
        ("mod_footprints.json", "modFootprints.json"),
        ("mod_groups.json", "modGroups.json"),
        ("mod_lists.json", "modLists.json"),
        ("mod_notes.json", "modNotes.json"),
        ("profile-mod-ids.json", "profileModIds.json"),
        ("server_last_up.json", "serverLastUp.json"),
        ("spt_versions.json", "sptVersions.json"),
        ("update_notifications.json", "updateNotifications.json"),
    ];

    private const string BackupsFolder = "backups";

    public static void Run() => Run(AppContext.BaseDirectory, AppPaths.DataDirectory, AppPaths.DataRename);

    public static void Run(string appFolder, string dataFolder, Outcome dataRename = Outcome.Nothing)
    {
        Log(dataRename, appFolder, "Data", "data");

        foreach (var (old, @new) in BesideExe) Log(Rename(appFolder, old, @new), appFolder, old, @new);
        foreach (var (old, @new) in Folders) Log(Rename(dataFolder, old, @new), dataFolder, old, @new);

        var backups = Path.Combine(dataFolder, BackupsFolder);
        foreach (var (old, @new) in Files)
        {
            Log(Rename(dataFolder, old, @new), dataFolder, old, @new);
            Log(Rename(backups, old, @new), backups, old, @new);
        }
    }

    // Renames <parent>\<oldName> to <parent>\<newName>, file or folder. Never logs: AppPaths calls
    // it while AppLog's own folder is still being worked out.
    public static Outcome Rename(string parent, string oldName, string newName)
    {
        try
        {
            if (!Directory.Exists(parent)) return Outcome.Nothing;

            var names = new DirectoryInfo(parent).EnumerateFileSystemInfos().Select(e => e.Name).ToList();
            var hasOld = names.Contains(oldName, StringComparer.Ordinal);
            var hasNew = names.Contains(newName, StringComparer.Ordinal);

            if (!hasOld) return Outcome.Nothing;
            if (hasNew) return Outcome.BothKept;

            var from = Path.Combine(parent, oldName);
            var to = Path.Combine(parent, newName);
            if (Directory.Exists(from)) Directory.Move(from, to);
            else File.Move(from, to);
            return Outcome.Renamed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Outcome.Failed;
        }
    }

    private static void Log(Outcome outcome, string parent, string oldName, string newName)
    {
        switch (outcome)
        {
            case Outcome.Renamed:
                AppLog.Info("Paths", $"renamed {Path.Combine(parent, oldName)} to {newName}");
                break;
            case Outcome.BothKept:
                AppLog.Warn("Paths", $"left {Path.Combine(parent, oldName)} alone - {newName} already exists beside it");
                break;
            case Outcome.Failed:
                AppLog.Warn("Paths", $"couldn't rename {Path.Combine(parent, oldName)} to {newName}");
                break;
        }
    }
}
