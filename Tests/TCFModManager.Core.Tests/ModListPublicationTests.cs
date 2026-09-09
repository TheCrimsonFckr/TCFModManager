using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Publishing an edited list has to move its revision, because the revision is the only thing a
// receiver has to tell a newer copy from the one it holds - and a revision otherwise counts an
// apply, which editing the list you serve is not.
//
// Chris hit this from the other side: "you should be able to manually refresh the list from the
// server to the client or headless without needing a revision". The button is the manual answer;
// this is the automatic one, for every client that will never think to press it.
//
public class ModListPublicationTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

    private static ModListEntry Entry(
        string name,
        int? modId = null,
        int? versionId = null,
        string? version = null,
        ModListEntryScope scope = ModListEntryScope.Everyone) =>
        new()
        {
            Name = name,
            ModId = modId,
            VersionId = versionId,
            Version = version,
            Scope = scope,
            Folders = [name.ToLowerInvariant()],
        };

    private static ModList List(Guid id, int revision, params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = id,
            Name = "Zero to Hero",
            Revision = revision,
            Origin = ModListOrigin.Local,
            Policy = ModListPolicy.Exclusive,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        list.Entries.AddRange(entries);
        return list;
    }

    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public void NothingPublishedYetIsNotAChange()
    {
        Assert.False(ModListPublication.NeedsNewRevision(List(Id, 1, Entry("SAIN", 791)), null));
    }

    // Republishing the same list twice is something an operator does while fiddling with a server.
    // A number that moves when nothing changed teaches people to ignore it.
    [Fact]
    public void RepublishingTheSameContentsDoesNotBump()
    {
        var list = List(Id, 4, Entry("SAIN", 791, 13477, "4.4.3"));
        var published = List(Id, 4, Entry("SAIN", 791, 13477, "4.4.3"));

        Assert.False(ModListPublication.NeedsNewRevision(list, published));
    }

    // The case this exists for: Chris moved Project Fika to Client + Headless and republished.
    [Fact]
    public void AChangedScopeIsAChange()
    {
        var published = List(Id, 1, Entry("Project Fika", 2326, scope: ModListEntryScope.Client));

        var edited = List(Id, 1, Entry("Project Fika", 2326,
            scope: ModListEntryScope.Client | ModListEntryScope.Headless));

        Assert.True(ModListPublication.NeedsNewRevision(edited, published));
    }

    [Fact]
    public void AChangedVersionPinIsAChange()
    {
        var published = List(Id, 1, Entry("SAIN", 791, 13477, "4.4.3"));
        var edited = List(Id, 1, Entry("SAIN", 791, 13999, "4.5.0"));

        Assert.True(ModListPublication.NeedsNewRevision(edited, published));
    }

    [Fact]
    public void AnAddedOrRemovedModIsAChange()
    {
        var published = List(Id, 1, Entry("SAIN", 791));

        Assert.True(ModListPublication.NeedsNewRevision(
            List(Id, 1, Entry("SAIN", 791), Entry("Waypoints", 827)), published));

        Assert.True(ModListPublication.NeedsNewRevision(List(Id, 1), published));
    }

    // A rename is what the client shows, so it travels.
    [Fact]
    public void ARenameIsAChange()
    {
        var published = List(Id, 1, Entry("SAIN", 791));

        var renamed = List(Id, 1, Entry("SAIN", 791));
        renamed.Name = "Zero to Hero Client + Headless";

        Assert.True(ModListPublication.NeedsNewRevision(renamed, published));
    }

    // The order entries are stored in is the page's business, not the receiver's.
    [Fact]
    public void ReorderingIsNotAChange()
    {
        var published = List(Id, 2, Entry("SAIN", 791), Entry("Waypoints", 827));
        var resorted = List(Id, 2, Entry("Waypoints", 827), Entry("SAIN", 791));

        Assert.False(ModListPublication.NeedsNewRevision(resorted, published));
    }

    //
    // Publishing a DIFFERENT list over the top of one. Its own number has nothing to say about the
    // list it replaces, and a receiver compares revisions per list.
    //
    [Fact]
    public void ADifferentListInTheFolderIsNotAReasonToBump()
    {
        var published = List(Guid.NewGuid(), 9, Entry("SAIN", 791));

        Assert.False(ModListPublication.NeedsNewRevision(List(Id, 1, Entry("Waypoints", 827)), published));
    }

    //
    // The revision itself is the thing being decided, so it is not part of the comparison. Otherwise
    // the first bump would make every later publish look like a change.
    //
    [Fact]
    public void TheRevisionIsNotPartOfTheComparison()
    {
        Assert.True(ModListPublication.ContentsMatch(
            List(Id, 1, Entry("SAIN", 791)),
            List(Id, 7, Entry("SAIN", 791))));
    }
}
