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
    // different places. The folder keeps its name through the move, so a copy left behind by an
    // older install can be dragged into Data\ and simply merge.
    //
    public static string LegacyConfigsDirectory => Path.Combine(DataDir, "LegacyConfigs");

    private static string ResolveDirectory(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, name);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
