namespace TCFModManager.Core.Services;

// 
// Resolves this app's own Data and Staging folders, both located next to the running exe
// (AppContext.BaseDirectory) rather than %LocalAppData%.
// 
public static class AppPaths
{
    private static readonly string DataDir = ResolveDirectory("Data");
    private static readonly string StagingDir = ResolveDirectory("Staging");

    public static string DataDirectory => DataDir;

    // Default destination for manually-fetched mod archives (see
    // DownloadsViewModel.DestinationFolder) - a "Staging" subfolder next to the exe. Can be
    // overridden per-download via the Downloads page's Browse... button.
    public static string StagingDirectory => StagingDir;

    //
    // Where a removed mod's config files are moved when the user chooses to keep them (see
    // ModConfigFiles). Created on first use rather than at startup, so it only exists once
    // something has actually been kept.
    //
    // Under Data\ rather than beside the exe, alongside the Configs page's own backups - both are
    // the app's copies of somebody's config files and there is no reason for them to live in
    // different places. The folder keeps its name through the move, so an older install's copy is
    // carried across by MigrateLegacyConfigsFolder below without anything being renamed.
    //
    public static string LegacyConfigsDirectory => Path.Combine(DataDir, "LegacyConfigs");

    // Where LegacyConfigsDirectory pointed before v1.5.0 - beside the exe rather than under Data\.
    private static string PreV150LegacyConfigsDirectory => Path.Combine(AppContext.BaseDirectory, "LegacyConfigs");

    //
    // TEMPORARY, ADDED IN v1.5.0 - DELETE WHEN THE APP LEAVES BETA, along with its call in
    // App.OnStartup and PreV150LegacyConfigsDirectory above.
    //
    // LegacyConfigs moved from beside the exe into Data\ in v1.5.0. An install updating from v1.4.x
    // can have config files kept from a removed mod sitting in the old folder, and nothing in the
    // app ever reads that folder back, so they would quietly stop being where the app says they are.
    // This moves them once; every launch after that finds nothing and does nothing.
    //
    // It stays for the rest of the beta rather than for one release: someone can sit on a v1.4.x
    // build for months and update straight into whatever is current, and the whole cost of keeping
    // it is one Directory.Exists that returns false.
    //
    // Called explicitly at startup rather than done lazily inside the property, so it is one method
    // and one call site to remove rather than something tangled into how a path resolves.
    //
    public static void MigrateLegacyConfigsFolder()
    {
        var old = PreV150LegacyConfigsDirectory;
        if (!Directory.Exists(old)) return;

        //
        // Both present means the folder was moved by hand already, or an earlier attempt half
        // finished. Merging two trees risks overwriting a kept config to save someone one drag, so
        // the old one is left exactly where it is and the log says so.
        //
        if (Directory.Exists(LegacyConfigsDirectory))
        {
            AppLog.Info("Paths", $"left {old} alone - {LegacyConfigsDirectory} already exists");
            return;
        }

        try
        {
            Directory.Move(old, LegacyConfigsDirectory);
            AppLog.Info("Paths", $"moved {old} into {LegacyConfigsDirectory}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing is lost - the old folder is still there, just not where new ones go.
            AppLog.Warn("Paths", $"couldn't move {old} into Data: {ex.Message}");
        }
    }

    private static string ResolveDirectory(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, name);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
