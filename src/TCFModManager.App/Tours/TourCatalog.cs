using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Tours;

internal sealed record TourStep(string Target, Func<string> Title, Func<string> Body, bool Beside = false);

internal sealed record Tour(string Id, Type Page, IReadOnlyList<TourStep> Steps, Func<bool>? Ready = null, string? Since = null);

internal static class TourCatalog
{
    public static IReadOnlyList<Tour> All { get; } =
    [
        new("browse", typeof(BrowsePage),
        [
            new("browse.search", () => Strings.Tour_Browse_Search_Title, () => Strings.Tour_Browse_Search_Body),
            new("browse.filters", () => Strings.Tour_Browse_Filters_Title, () => Strings.Tour_Browse_Filters_Body),
            new("browse.card", () => Strings.Tour_Browse_Card_Title, () => Strings.Tour_Browse_Card_Body),
        ],
        () => AppServices.Browse.HasLoadedResults),

        new("installed", typeof(InstalledPage),
        [
            new("installed.views", () => Strings.Tour_Installed_Views_Title, () => Strings.Tour_Installed_Views_Body),
            new("installed.filters", () => Strings.Tour_Installed_Filters_Title, () => Strings.Tour_Installed_Filters_Body),
            new("installed.multiselect", () => Strings.Tour_Installed_Multi_Title, () => Strings.Tour_Installed_Multi_Body),
            new("installed.rescan", () => Strings.Tour_Installed_Rescan_Title, () => Strings.Tour_Installed_Rescan_Body),
        ]),

        new("modlists", typeof(ModListsPage),
        [
            new("modlists.new", () => Strings.Tour_ModLists_New_Title, () => Strings.Tour_ModLists_New_Body),
            new("modlists.lists", () => Strings.Tour_ModLists_Lists_Title, () => Strings.Tour_ModLists_Lists_Body),
            new("modlists.preview", () => Strings.Tour_ModLists_Preview_Title, () => Strings.Tour_ModLists_Preview_Body),
        ]),

        new("profile", typeof(ProfilePage),
        [
            new("profile.server", () => Strings.Tour_Profile_Server_Title, () => Strings.Tour_Profile_Server_Body),
            new("profile.launcher", () => Strings.Tour_Profile_Launcher_Title, () => Strings.Tour_Profile_Launcher_Body),
            new("profile.stats", () => Strings.Tour_Profile_Stats_Title, () => Strings.Tour_Profile_Stats_Body),
            new("profile.quickplay", () => Strings.Tour_Profile_QuickPlay_Title, () => Strings.Tour_Profile_QuickPlay_Body, Beside: true),
        ]),

        new("configs", typeof(ConfigsPage),
        [
            new("configs.search", () => Strings.Tour_Configs_Search_Title, () => Strings.Tour_Configs_Search_Body),
            new("configs.list", () => Strings.Tour_Configs_List_Title, () => Strings.Tour_Configs_List_Body),
            new("configs.form", () => Strings.Tour_Configs_Form_Title, () => Strings.Tour_Configs_Form_Body),
        ]),

        new("server", typeof(ServerPage),
        [
            new("server.server", () => Strings.Tour_Profile_Server_Title, () => Strings.Tour_Profile_Server_Body),
            new("server.headless", () => Strings.Tour_Server_Headless_Title, () => Strings.Tour_Server_Headless_Body),
            new("server.tools", () => Strings.Tour_Server_Tools_Title, () => Strings.Tour_Server_Tools_Body),
        ]),

        new("dependencies", typeof(DependenciesPage),
        [
            new("dependencies.conflicts", () => Strings.Tour_Dependencies_Conflicts_Title, () => Strings.Tour_Dependencies_Conflicts_Body),
            new("dependencies.versions", () => Strings.Tour_Dependencies_Versions_Title, () => Strings.Tour_Dependencies_Versions_Body),
            new("dependencies.tree", () => Strings.Tour_Dependencies_Tree_Title, () => Strings.Tour_Dependencies_Tree_Body),
            new("dependencies.refresh", () => Strings.Tour_Dependencies_Refresh_Title, () => Strings.Tour_Dependencies_Refresh_Body),
        ]),

        new("diagnose", typeof(DiagnosePage),
        [
            new("diagnose.search", () => Strings.Tour_Diagnose_Search_Title, () => Strings.Tour_Diagnose_Search_Body),
            new("diagnose.findings", () => Strings.Tour_Diagnose_Findings_Title, () => Strings.Tour_Diagnose_Findings_Body),
            new("diagnose.purge", () => Strings.Tour_Diagnose_Purge_Title, () => Strings.Tour_Diagnose_Purge_Body),
            new("diagnose.copy", () => Strings.Tour_Diagnose_Copy_Title, () => Strings.Tour_Diagnose_Copy_Body),
            new("diagnose.refresh", () => Strings.Tour_Diagnose_Refresh_Title, () => Strings.Tour_Diagnose_Refresh_Body),
        ]),

        new("footprint", typeof(FootprintPage),
        [
            new("footprint.rescan", () => Strings.Tour_Footprint_Rescan_Title, () => Strings.Tour_Footprint_Rescan_Body),
            new("footprint.rows", () => Strings.Tour_Footprint_Rows_Title, () => Strings.Tour_Footprint_Rows_Body),
            new("footprint.reads", () => Strings.Tour_Footprint_Reads_Title, () => Strings.Tour_Footprint_Reads_Body),
        ]),

        new("servermap", typeof(ServerMapPage),
        [
            new("servermap.connection", () => Strings.Tour_ServerMap_Connection_Title, () => Strings.Tour_ServerMap_Connection_Body),
            new("servermap.list", () => Strings.Tour_ServerMap_List_Title, () => Strings.Tour_ServerMap_List_Body),
            new("servermap.machines", () => Strings.Tour_ServerMap_Machines_Title, () => Strings.Tour_ServerMap_Machines_Body),
            new("servermap.owners", () => Strings.Tour_ServerMap_Owners_Title, () => Strings.Tour_ServerMap_Owners_Body),
        ]),

        new("webpages", typeof(WebPagesPage),
        [
            new("webpages.rows", () => Strings.Tour_WebPages_Rows_Title, () => Strings.Tour_WebPages_Rows_Body),
            new("webpages.intro", () => Strings.Tour_WebPages_Intro_Title, () => Strings.Tour_WebPages_Intro_Body),
            new("webpages.rescan", () => Strings.Tour_WebPages_Rescan_Title, () => Strings.Tour_WebPages_Rescan_Body),
        ],
        () => AppServices.WebPages.HasRows),

        new("downloads", typeof(DownloadsPage),
        [
            new("downloads.queue", () => Strings.Tour_Downloads_Queue_Title, () => Strings.Tour_Downloads_Queue_Body),
            new("downloads.pause", () => Strings.Tour_Downloads_Pause_Title, () => Strings.Tour_Downloads_Pause_Body),
            new("downloads.clear", () => Strings.Tour_Downloads_Clear_Title, () => Strings.Tour_Downloads_Clear_Body),
        ]),

        new("appupdate", typeof(AppUpdatePage),
        [
            new("appupdate.version", () => Strings.Tour_AppUpdate_Version_Title, () => Strings.Tour_AppUpdate_Version_Body),
            new("appupdate.check", () => Strings.Tour_AppUpdate_Check_Title, () => Strings.Tour_AppUpdate_Check_Body),
            new("appupdate.servermap", () => Strings.Tour_AppUpdate_ServerMap_Title, () => Strings.Tour_AppUpdate_ServerMap_Body),
        ]),

        new("help", typeof(HelpPage),
        [
            new("help.search", () => Strings.Tour_Help_Search_Title, () => Strings.Tour_Help_Search_Body),
            new("help.guide", () => Strings.Tour_Help_Guide_Title, () => Strings.Tour_Help_Guide_Body),
            new("help.tour", () => Strings.Tour_Help_Tour_Title, () => Strings.Tour_Help_Tour_Body),
        ]),

        new("options", typeof(OptionsPage),
        [
            new("options.search", () => Strings.Tour_Options_Search_Title, () => Strings.Tour_Options_Search_Body),
            new("options.folder", () => Strings.Tour_Options_Folder_Title, () => Strings.Tour_Options_Folder_Body),
            new("options.tours", () => Strings.Tour_Options_Tours_Title, () => Strings.Tour_Options_Tours_Body),
        ]),

        new("whatsnew-1.31.0-browse", typeof(BrowsePage),
        [
            new("browse.search", () => Strings.Tour_New131_Search_Title, () => Strings.Tour_New131_Search_Body),
            new("nav.browse", () => Strings.Tour_New131_Keys_Title, () => Strings.Tour_New131_Keys_Body, Beside: true),
        ],
        () => AppServices.Browse.HasLoadedResults,
        Since: "1.31.0"),
    ];

    public static Tour? ForPage(Type? page) =>
        page is null ? null : All.FirstOrDefault(t => t.Page == page && t.Since is null);

    public static IEnumerable<Tour> WhatsNewFor(Type? page, string? after, string? upTo) =>
        All.Where(t => t.Page == page && t.Since is not null && IsAfter(t.Since, after) && !IsAfter(t.Since, upTo));

    private static bool IsAfter(string version, string? than)
    {
        if (!SemanticVersion.TryParse(version, out var v)) return false;
        if (!SemanticVersion.TryParse(than, out var t)) return true;

        return (v.Value.Major, v.Value.Minor, v.Value.Patch).CompareTo((t.Value.Major, t.Value.Minor, t.Value.Patch)) > 0;
    }

    public static Tour? ById(string id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
}
