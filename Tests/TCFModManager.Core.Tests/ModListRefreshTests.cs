using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Re-reading installed versions into a list that already exists.
//
// Before this, correcting a pin meant taking the mod off the list and adding it back, which
// re-captures the version installed now. That works and is ridiculous at seventy entries - but it
// also explains what the refresh must NOT do: a re-capture rebuilds an entry from scratch, and the
// entry carries decisions a rebuild would throw away. Most of these tests are about what survives.
//
public class ModListRefreshTests
{
    private static ModListEntry Entry(
        string name,
        int? modId = null,
        string? version = null,
        int? versionId = null,
        ModListEntryScope scope = ModListEntryScope.Everyone,
        string? guid = null,
        params string[] folders) =>
        new()
        {
            Name = name,
            ModId = modId,
            Version = version,
            VersionId = versionId,
            Scope = scope,
            Guid = guid,
            Folders = [.. folders],
        };

    private static ModListCandidate Installed(
        string name,
        int? modId = null,
        string? version = null,
        string? guid = null,
        ModListEntryScope scope = ModListEntryScope.Everyone,
        params string[] folders) =>
        new()
        {
            Name = name,
            ModId = modId,
            Version = version,
            Guid = guid,
            Scope = scope,
            Folders = [.. folders],
        };

    [Fact]
    public void PicksUpTheVersionInstalledNow()
    {
        var result = ModListRefresh.Build(
            [Entry("Some Mod", modId: 31, version: "1.4.2")],
            [Installed("Some Mod", modId: 31, version: "1.5.0")]);

        Assert.Equal("1.5.0", result.Entries[0].Version);

        var changed = Assert.Single(result.Changed);
        Assert.Equal("Some Mod", changed.Name);
        Assert.Equal("1.4.2", changed.From);
        Assert.Equal("1.5.0", changed.To);
    }

    [Fact]
    public void ReportsNothingWhenTheListAlreadyMatches()
    {
        var result = ModListRefresh.Build(
            [Entry("Some Mod", modId: 31, version: "1.5.0")],
            [Installed("Some Mod", modId: 31, version: "1.5.0")]);

        Assert.Empty(result.Changed);
    }

    //
    // THE ONE THAT MATTERS MOST. Scope is a decision somebody made - it is what keeps fika-server
    // and the Server Map mod itself off a client's plan. Re-inferring it from where files live would
    // undo every one of those the first time an operator refreshed their published list.
    //
    [Fact]
    public void ScopeSurvivesEvenWhenTheInstallDisagrees()
    {
        var result = ModListRefresh.Build(
            [Entry("Server Thing", modId: 31, version: "1.0.0", scope: ModListEntryScope.Server)],
            [Installed("Server Thing", modId: 31, version: "2.0.0", scope: ModListEntryScope.Everyone)]);

        Assert.Equal(ModListEntryScope.Server, result.Entries[0].EffectiveScope);
        Assert.Equal("2.0.0", result.Entries[0].Version);
    }

    //
    // A list can name mods this machine does not have, on purpose - that is most of what a server's
    // list is to a player who has not applied it yet.
    //
    [Fact]
    public void EntriesThatAreNotInstalledHereAreLeftExactlyAsWritten()
    {
        var entry = Entry("Not Here", modId: 99, version: "1.0.0");

        var result = ModListRefresh.Build([entry], [Installed("Something Else", modId: 31, version: "2.0.0")]);

        Assert.Same(entry, result.Entries[0]);
        Assert.Empty(result.Changed);
        Assert.Equal(1, result.NotInstalled);
    }

    //
    // The stored name is the join key an unresolved entry has and nothing else. Rewriting it to
    // whatever this machine calls the folder would break the match on somebody else's install.
    //
    [Fact]
    public void TheStoredNameIsNotRewritten()
    {
        var result = ModListRefresh.Build(
            [Entry("The name the list stored", modId: 31, version: "1.0.0")],
            [Installed("What this machine calls it", modId: 31, version: "2.0.0")]);

        Assert.Equal("The name the list stored", result.Entries[0].Name);
    }

    [Fact]
    public void AMissingGuidIsFilledInFromTheInstall()
    {
        var result = ModListRefresh.Build(
            [Entry("Some Mod", modId: 31, version: "1.0.0")],
            [Installed("Some Mod", modId: 31, version: "2.0.0", guid: "com.example.mod")]);

        Assert.Equal("com.example.mod", result.Entries[0].Guid);
    }

    // Whoever made the list wrote it; a scan is not grounds to overrule it.
    [Fact]
    public void AGuidAlreadyOnTheEntryIsNotReplaced()
    {
        var result = ModListRefresh.Build(
            [Entry("Some Mod", modId: 31, version: "1.0.0", guid: "com.list.original")],
            [Installed("Some Mod", modId: 31, version: "2.0.0", guid: "com.scanner.found")]);

        Assert.Equal("com.list.original", result.Entries[0].Guid);
    }

    // A mod can rename its folder between versions, and a stale folder name is a match that quietly
    // stops working.
    [Fact]
    public void FoldersFollowTheInstall()
    {
        var result = ModListRefresh.Build(
            [Entry("Some Mod", modId: 31, version: "1.0.0", folders: "oldfolder")],
            [Installed("Some Mod", modId: 31, version: "2.0.0", folders: "NewFolder")]);

        Assert.Equal(["newfolder"], result.Entries[0].Folders);
    }

    // But never to nothing: a scan that reported no folders is not the same as a mod having none.
    [Fact]
    public void FoldersAreNotLostToAScanThatFoundNone()
    {
        var result = ModListRefresh.Build(
            [Entry("Some Mod", modId: 31, version: "1.0.0", folders: "keepme")],
            [Installed("Some Mod", modId: 31, version: "2.0.0")]);

        Assert.Equal(["keepme"], result.Entries[0].Folders);
    }

    //
    // Matching is ModListMatch's, so an entry with no mod id still finds its mod by folder - which
    // is the only join a hand-made or unresolved entry has.
    //
    [Fact]
    public void AnUnresolvedEntryStillMatchesOnItsFolder()
    {
        var result = ModListRefresh.Build(
            [Entry("some-folder-name", version: "1.0.0", folders: "some-folder-name")],
            [Installed("Proper Listing Name", modId: 31, version: "2.0.0", folders: "some-folder-name")]);

        Assert.Equal("2.0.0", result.Entries[0].Version);
        Assert.Equal(31, result.Entries[0].ModId);
    }

    [Fact]
    public void TheOrderOfTheListIsUnchanged()
    {
        var result = ModListRefresh.Build(
            [Entry("Zebra", modId: 1, version: "1.0"), Entry("Apple", modId: 2, version: "1.0")],
            [Installed("Zebra", modId: 1, version: "2.0"), Installed("Apple", modId: 2, version: "2.0")]);

        Assert.Equal("Zebra", result.Entries[0].Name);
        Assert.Equal("Apple", result.Entries[1].Name);
    }
}
