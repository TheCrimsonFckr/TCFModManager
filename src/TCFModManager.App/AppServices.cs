using TCFModManager.App.Services;
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

    // Whether a newer release of this app has been published on sp-mod.com. Shared between the
    // banner in MainWindow, the nav item's badge and the App update page, so all three read one
    // check rather than each running their own.
    public static AppUpdateViewModel AppUpdate { get; } = new();

    // Whether the mod-page gate is switched off, and the wording the install buttons use to say so.
    // Shared so one setting change updates every button at once - see ModPageGateViewModel.
    public static ModPageGateViewModel ModPageGate { get; } = new();

    // Every mod list this install holds, and which one it is currently following.
    public static ModListStore ModLists { get; } = new();

    // Turns a mod list into an installed set and back - the scan and the downloads Core can't do
    // for itself. Declared after DownloadQueue, which it enqueues onto.
    public static ModListService ModListWorkflow { get; } = new();
}
