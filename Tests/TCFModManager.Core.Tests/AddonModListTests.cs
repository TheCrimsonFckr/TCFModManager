using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Mod lists carrying addons. The thing being guarded throughout is that an addon id and a mod id
// are separate sequences on sp-mod.com, so every comparison has to carry IsAddon with the number -
// otherwise a list naming addon 116 installs, updates or disables whatever mod 116 happens to be.
//
public class AddonModListTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    private static ModListCandidate Candidate(
        string name,
        int? modId = null,
        bool isAddon = false,
        string? version = null,
        bool disabled = false,
        string? guid = null,
        string[]? folders = null,
        bool canBeDisabled = true) =>
        new()
        {
            Name = name,
            ModId = modId,
            IsAddon = isAddon,
            Version = version,
            Guid = guid,
            IsDisabled = disabled,
            CanBeDisabled = canBeDisabled,
            Folders = folders ?? [],
        };

    private static ModListEntry Entry(
        string name, int? modId = null, bool isAddon = false, int? versionId = null, string? version = null)
        => new() { Name = name, ModId = modId, IsAddon = isAddon, VersionId = versionId, Version = version };

    private static ModList Exclusive(params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Fika night",
            Policy = ModListPolicy.Exclusive,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        list.Entries.AddRange(entries);
        return list;
    }

    // ---- capture -------------------------------------------------------------------------------

    [Fact]
    public void Capture_RecordsAnAddonAsAnAddonAndPinsItsVersionFromTheAddonCache()
    {
        ModListCapture.AddonVersionLookup addonVersions = id => id == 115
            ? [new AddonVersionSummary { Id = 252, Version = "1.0.1", ModVersionConstraint = "^1.5.0" }]
            : null;

        var list = ModListCapture.Build(
            "Fika night",
            [Candidate("RaidReviewOverlay", modId: 115, isAddon: true, version: "1.0.1")],
            Timestamp,
            addonVersions: addonVersions);

        var entry = Assert.Single(list.Entries);
        Assert.True(entry.IsAddon);
        Assert.Equal(115, entry.ModId);
        Assert.Equal(252, entry.VersionId);
        Assert.True(entry.IsPinned);
    }

    [Fact]
    public void Capture_KeepsAModAndAnAddonThatShareAnId()
    {
        var list = ModListCapture.Build(
            "Fika night",
            [
                Candidate("Some Mod", modId: 116, version: "2.0.0"),
                Candidate("Some Addon", modId: 116, isAddon: true, version: "1.0.1"),
            ],
            Timestamp);

        Assert.Equal(2, list.Entries.Count);
        Assert.Single(list.Entries, e => e is { ModId: 116, IsAddon: false });
        Assert.Single(list.Entries, e => e is { ModId: 116, IsAddon: true });
    }

    [Fact]
    public void Capture_DoesNotPinAnAddonVersionFromTheModVersionLookup()
    {
        // The mod lookup answering for id 115 must not be allowed to pin addon 115's version.
        ModListCapture.VersionLookup modVersions = _ =>
            [new ModVersionSummary { Id = 9999, Version = "1.0.1" }];

        var list = ModListCapture.Build(
            "Fika night",
            [Candidate("RaidReviewOverlay", modId: 115, isAddon: true, version: "1.0.1")],
            Timestamp,
            modVersions);

        var entry = Assert.Single(list.Entries);
        Assert.Null(entry.VersionId);
        Assert.Equal("1.0.1", entry.Version);
    }

    // ---- planner -------------------------------------------------------------------------------

    [Fact]
    public void Planner_MatchesAnAddonEntryToTheInstalledAddon()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("RaidReviewOverlay", modId: 115, isAddon: true, version: "1.0.1")),
            [Candidate("RaidReviewOverlay", modId: 115, isAddon: true, version: "1.0.1")]);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(ModListActionKind.Keep, action.Kind);
        Assert.True(action.IsAddon);
    }

    [Fact]
    public void Planner_DoesNotLetAnAddonEntryClaimAModWithTheSameId()
    {
        // Addon 116 on the list, mod 116 installed. Without the IsAddon half of the key this came
        // out as one Keep - the list would have looked satisfied by an unrelated mod.
        var plan = ModListPlanner.Build(
            Exclusive(Entry("Some Addon", modId: 116, isAddon: true, version: "1.0.1")),
            [Candidate("Some Mod", modId: 116, version: "2.0.0")]);

        Assert.Equal(2, plan.Actions.Count);

        var install = Assert.Single(plan.Actions, a => a.Kind == ModListActionKind.Install);
        Assert.True(install.IsAddon);
        Assert.Equal(116, install.ModId);

        var disable = Assert.Single(plan.Actions, a => a.Kind == ModListActionKind.Disable);
        Assert.False(disable.IsAddon);
        Assert.Equal("Some Mod", disable.Name);
    }

    [Fact]
    public void Planner_DisablesAnInstalledAddonAnExclusiveListDoesNotName()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("Some Mod", modId: 2441, version: "2.0.0")),
            [
                Candidate("Some Mod", modId: 2441, version: "2.0.0"),
                Candidate("RaidReviewOverlay", modId: 115, isAddon: true, version: "1.0.1"),
            ]);

        var disable = Assert.Single(plan.Actions, a => a.Kind == ModListActionKind.Disable);
        Assert.True(disable.IsAddon);
        Assert.Equal(115, disable.ModId);
    }

    [Fact]
    public void Planner_UpdatesAnAddonAtTheWrongVersion()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("RaidReviewOverlay", modId: 115, isAddon: true, versionId: 252, version: "1.0.1")),
            [Candidate("RaidReviewOverlay", modId: 115, isAddon: true, version: "1.0.0")]);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(ModListActionKind.Update, action.Kind);
        Assert.True(action.IsAddon);
        Assert.True(action.IsFetch);
        Assert.Equal(252, action.VersionId);
    }

    [Fact]
    public void Planner_NeverDisablesAnAddonThatHasNoFolderOfItsOwn()
    {
        // The common shape: the addon's files sit inside its parent mod's folder, so there is
        // nothing of its own to move. A Disable action here would report success and do nothing.
        var plan = ModListPlanner.Build(
            Exclusive(Entry("Some Mod", modId: 2441, version: "2.0.0")),
            [
                Candidate("Some Mod", modId: 2441, version: "2.0.0"),
                Candidate("Black Div spawn notifier", modId: 34, isAddon: true, version: "1.1.0", canBeDisabled: false),
            ]);

        Assert.Empty(plan.Disable);
        Assert.Single(plan.Actions);
    }

    [Fact]
    public void Planner_StillInstallsAndUpdatesAnAddonThatHasNoFolderOfItsOwn()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("Black Div spawn notifier", modId: 34, isAddon: true, versionId: 207, version: "1.1.0")),
            [Candidate("Black Div spawn notifier", modId: 34, isAddon: true, version: "1.0.1", canBeDisabled: false)]);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(ModListActionKind.Update, action.Kind);
        Assert.True(action.IsFetch);
    }

    // ---- membership ----------------------------------------------------------------------------

    [Fact]
    public void Membership_BadgesAnInstalledAddonWithTheListsThatNameIt()
    {
        var list = Exclusive(
            Entry("Some Mod", modId: 2441, version: "2.0.0"),
            Entry("RaidReviewOverlay", modId: 115, isAddon: true, version: "1.0.1"));

        var installed = new[]
        {
            Candidate("Some Mod", modId: 2441, version: "2.0.0"),
            Candidate("RaidReviewOverlay", modId: 115, isAddon: true, version: "1.0.1"),
        };

        var names = ModListMembership.Names([list], installed);

        Assert.Equal(["Fika night"], names[1]);
    }

    // ---- share file ----------------------------------------------------------------------------

    [Fact]
    public void ShareFile_StampsSchema2OnlyWhenTheListContainsAnAddon()
    {
        // An addon-free list stays readable by an app that predates addon support; a list that
        // names one does not, because that app would read the addon id as a mod id.
        var modsOnly = Exclusive(Entry("Some Mod", modId: 2441, version: "2.0.0"));
        var withAddon = Exclusive(Entry("RaidReviewOverlay", modId: 115, isAddon: true, version: "1.0.1"));

        Assert.Equal(ModListFile.BaseSchemaVersion, ModListFile.SchemaVersionFor(modsOnly));
        Assert.Equal(ModListFile.AddonSchemaVersion, ModListFile.SchemaVersionFor(withAddon));

        Assert.Contains("\"SchemaVersion\": 1", ModListFile.Write(modsOnly));
        Assert.Contains("\"SchemaVersion\": 2", ModListFile.Write(withAddon));
    }

    [Fact]
    public void ShareFile_RoundTripsAnAddonEntry()
    {
        var exported = ModListFile.Write(
            Exclusive(Entry("RaidReviewOverlay", modId: 115, isAddon: true, versionId: 252, version: "1.0.1")));

        var imported = ModListFile.Read(exported);

        Assert.True(imported.Succeeded);
        var entry = Assert.Single(imported.List!.Entries);
        Assert.True(entry.IsAddon);
        Assert.Equal(115, entry.ModId);
        Assert.Equal(252, entry.VersionId);
    }
}
