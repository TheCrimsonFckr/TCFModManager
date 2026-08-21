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

    // Where a removed mod's config files are moved when the user chooses to keep them (see
    // ModConfigFiles). Created on first use rather than at startup, so it only exists once
    // something has actually been kept.
    public static string LegacyConfigsDirectory => Path.Combine(AppContext.BaseDirectory, "LegacyConfigs");

    private static string ResolveDirectory(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Copies <paramref name="fileName"/> from the legacy %LocalAppData%\TCFModManagement\
    // location into the Data folder if it doesn't already exist there. No-op once migrated; safe
    // to call on every startup.
    public static void MigrateLegacyFile(string fileName)
    {
        var newPath = Path.Combine(DataDirectory, fileName);
        if (File.Exists(newPath)) return;

        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TCFModManagement",
            fileName);
        if (File.Exists(legacyPath))
        {
            File.Copy(legacyPath, newPath);
        }
    }
}
