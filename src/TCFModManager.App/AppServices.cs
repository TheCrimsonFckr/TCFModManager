using TCFModManager.App.ViewModels;
using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Services;

namespace TCFModManager.App;

// 
// Shared, app-lifetime instances of the Core services.
// 
internal static class AppServices
{
    public static SpModApiClient SpModApi { get; } = new();

    // The SPT release list sp-mod.com publishes, used to resolve mod version constraints
    // to releases that actually shipped rather than the boundaries constraints are written with.
    public static SptReleaseCatalog SptCatalog { get; } = new(SpModApi);

    public static ModDownloadService Downloads { get; } = new();

    public static ModInstallManifestService InstallManifest { get; } = new();

    // Backs the Mod Groups window - which installed mods the user has sorted into which
    // self-defined group. Purely organizational; nothing else in the app reads it.
    public static ModGroupStore ModGroups { get; } = new();

    // Places (and removes) a mod's files in the SPT install.
    public static ModInstallService ModInstall { get; } = new(Downloads, InstallManifest);

    // App-lifetime download queue. Declared before Browse because BrowseViewModel's
    // constructor subscribes to DownloadQueue.ItemInstalled and needs it already constructed.
    public static DownloadQueueViewModel DownloadQueue { get; } = new();

    // Shared with MainWindow to render the mod details overlay at the window level.
    public static ModDetailsOverlayViewModel ModDetailsOverlay { get; } = new();

    // Shared with MainWindow to render the Installed page's per-mod update dialog at the window level.
    public static ModUpdateOverlayViewModel ModUpdateOverlay { get; } = new();

    // Shared between Options and Browse for the detected SPT install/version.
    public static SptEnvironmentViewModel SptEnvironment { get; } = new();

    // The full sp-mod.com catalog, fetched once and reused across the app session.
    public static ModCacheViewModel ModCache { get; } = new();

    // Shared across every Browse page navigation, not re-created per visit.
    public static BrowseViewModel Browse { get; } = new();
}
