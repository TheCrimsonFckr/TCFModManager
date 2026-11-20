using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-56 N18.
public sealed class NavigationHistoryTests
{
    private sealed record Place(string Page, int? Author = null);

    private static NavigationHistory<Place> Visited(params string[] pages)
    {
        var history = new NavigationHistory<Place>(50);
        foreach (var page in pages) history.Visit(new Place(page));
        return history;
    }

    [Fact]
    public void Back_and_forward_walk_the_places_as_a_browser_does()
    {
        var history = Visited("Browse", "Installed", "Configs");

        Assert.Equal(new Place("Installed"), history.GoBack());
        Assert.Equal(new Place("Browse"), history.GoBack());
        Assert.False(history.CanGoBack);
        Assert.Null(history.GoBack());

        Assert.Equal(new Place("Installed"), history.GoForward());
        Assert.Equal(new Place("Configs"), history.GoForward());
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void Going_somewhere_new_drops_what_was_ahead()
    {
        var history = Visited("Browse", "Installed", "Configs");
        history.GoBack(2);

        Assert.True(history.Visit(new Place("Diagnose")));

        Assert.False(history.CanGoForward);
        Assert.Equal([new Place("Browse")], history.Back);
    }

    [Fact]
    public void The_place_already_current_is_not_a_new_step()
    {
        var history = Visited("Browse", "Installed");

        Assert.False(history.Visit(new Place("Installed")));
        Assert.Equal([new Place("Browse")], history.Back);
    }

    [Fact]
    public void Two_authors_are_two_places()
    {
        var history = new NavigationHistory<Place>(50);
        history.Visit(new Place("Browse"));
        history.Visit(new Place("Author", 12));
        history.Visit(new Place("Author", 34));

        Assert.Equal(new Place("Author", 12), history.GoBack());
    }

    [Fact]
    public void The_lists_run_nearest_first_and_a_jump_lands_where_asked()
    {
        var history = Visited("Profile", "Browse", "Installed", "Configs", "Diagnose");

        Assert.Equal(["Configs", "Installed", "Browse", "Profile"], history.Back.Select(p => p.Page));

        Assert.Equal(new Place("Browse"), history.GoBack(3));
        Assert.Equal(["Installed", "Configs", "Diagnose"], history.Forward.Select(p => p.Page));
        Assert.Equal(["Profile"], history.Back.Select(p => p.Page));

        Assert.Equal(new Place("Configs"), history.GoForward(2));
        Assert.Equal(["Installed", "Browse", "Profile"], history.Back.Select(p => p.Page));
        Assert.Null(history.GoForward(2));
    }

    [Fact]
    public void Only_so_many_places_are_kept_behind()
    {
        var history = new NavigationHistory<Place>(3);
        foreach (var page in new[] { "A", "B", "C", "D", "E" }) history.Visit(new Place(page));

        Assert.Equal(["D", "C", "B"], history.Back.Select(p => p.Page));
    }
}
