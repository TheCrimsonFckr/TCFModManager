using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-12 A4, R7: which new mods from followed authors are announced.
public class NewModAnnouncerTests
{
    private static readonly DateTimeOffset Followed = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly FollowedAuthor Alice = new() { Id = 7, Name = "Alice", FollowedAt = Followed };

    private static Mod ModBy(int id, int ownerId, DateTimeOffset published, int? coAuthor = null) => new()
    {
        Id = id,
        Name = "Mod " + id,
        Owner = new Owner { Id = ownerId, Name = "Owner " + ownerId },
        AdditionalAuthors = coAuthor is { } c ? [new Owner { Id = c, Name = "Alice" }] : null,
        PublishedAt = published,
    };

    [Fact]
    public void A_followed_authors_mod_published_since_following_is_news_once()
    {
        var mod = ModBy(1, 7, Followed.AddDays(1));

        var first = NewModAnnouncer.Pick([mod], [Alice], []);
        Assert.Equal([1], first.New.Select(n => n.ModId));
        Assert.Equal("Owner 7", first.New[0].AuthorName);

        var again = NewModAnnouncer.Pick([mod], [Alice], first.Announced);
        Assert.Empty(again.New);
    }

    [Fact]
    public void Their_back_catalogue_is_not_news()
    {
        var result = NewModAnnouncer.Pick([ModBy(2, 7, Followed.AddDays(-3))], [Alice], []);

        Assert.Empty(result.New);
    }

    [Fact]
    public void A_co_authored_mod_counts_and_others_do_not()
    {
        var result = NewModAnnouncer.Pick(
            [ModBy(3, 99, Followed.AddHours(1), coAuthor: 7), ModBy(4, 99, Followed.AddHours(1))],
            [Alice], []);

        Assert.Equal([3], result.New.Select(n => n.ModId));
    }

    [Fact]
    public void Nothing_is_news_with_nobody_followed()
    {
        Assert.Empty(NewModAnnouncer.Pick([ModBy(5, 7, Followed.AddDays(1))], [], []).New);
    }

    [Fact]
    public void Only_the_newest_ids_are_remembered()
    {
        var announced = Enumerable.Range(1000, NewModAnnouncer.Remembered).ToList();

        var result = NewModAnnouncer.Pick([ModBy(6, 7, Followed.AddDays(1))], [Alice], announced);

        Assert.Equal(NewModAnnouncer.Remembered, result.Announced.Count);
        Assert.Equal(6, result.Announced[^1]);
        Assert.DoesNotContain(1000, result.Announced);
    }
}
