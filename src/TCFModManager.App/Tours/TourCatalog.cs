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
