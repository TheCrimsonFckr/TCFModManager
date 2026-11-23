using TCFModManager.App.Localization;
using TCFModManager.App.Views;

namespace TCFModManager.App.Tours;

internal sealed record TourStep(string Target, Func<string> Title, Func<string> Body);

internal sealed record Tour(string Id, Type Page, IReadOnlyList<TourStep> Steps, Func<bool>? Ready = null);

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
    ];

    public static Tour? ForPage(Type? page) => page is null ? null : All.FirstOrDefault(t => t.Page == page);

    public static Tour? ById(string id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
}
