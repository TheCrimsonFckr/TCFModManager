using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModListPlannerTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    private static ModList List(ModListPolicy policy, params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Fika night",
            Policy = policy,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        list.Entries.AddRange(entries);
        return list;
    }

    private static ModList Exclusive(params ModListEntry[] entries) => List(ModListPolicy.Exclusive, entries);

    private static ModListEntry Entry(
        string name,
        int? modId = null,
        int? versionId = null,
        string? version = null,
        string? guid = null,
        string[]? folders = null)
    {
        var entry = new ModListEntry
        {
            Name = name,
            ModId = modId,
            VersionId = versionId,
            Version = version,
            Guid = guid,
        };

        if (folders is not null) entry.Folders.AddRange(folders);
        return entry;
    }

    private static ModListCandidate Installed(
        string name,
        int? modId = null,
        string? version = null,
        bool disabled = false,
        string? guid = null,
        string[]? folders = null) =>
        new()
        {
            Name = name,
            ModId = modId,
            Version = version,
            Guid = guid,
            IsDisabled = disabled,
            Folders = folders ?? [],
        };

    private static ModListAction Only(ModListPlan plan, ModListActionKind kind) =>
        Assert.Single(plan.Actions, a => a.Kind == kind);

    [Fact]
    public void AModOnTheListThatIsNotInstalledIsAnInstall()
    {
        var plan = ModListPlanner.Build(Exclusive(Entry("SAIN", 2426, 55, "3.2.0")), []);

        var action = Only(plan, ModListActionKind.Install);
        Assert.Equal("SAIN", action.Name);
        Assert.Equal(2426, action.ModId);
        Assert.Equal(55, action.VersionId);
        Assert.False(action.NeedsVersionLookup);
        Assert.True(plan.RequiresDownloads);
        Assert.False(plan.IsNoOp);
    }

    [Fact]
    public void AnUnresolvedEntryThatIsNotInstalledIsManual()
    {
        var plan = ModListPlanner.Build(Exclusive(Entry("FixPluginTypesSerialization")), []);

        Assert.Equal("FixPluginTypesSerialization", Only(plan, ModListActionKind.Manual).Name);
        Assert.False(plan.RequiresDownloads);

        // Nothing for the app to do, even though the user still has a mod to fetch by hand.
        Assert.True(plan.IsNoOp);
    }

    [Fact]
    public void AnInstallWithNoPinnedVersionIdNeedsALookup()
    {
        var plan = ModListPlanner.Build(Exclusive(Entry("SAIN", 2426, version: "3.2.0")), []);

        var action = Only(plan, ModListActionKind.Install);
        Assert.True(action.NeedsVersionLookup);
        Assert.Equal("3.2.0", action.TargetVersion);
        Assert.Same(action, Assert.Single(plan.NeedingVersionLookup));
    }

    [Fact]
    public void AModAtTheVersionTheListNamesIsKept()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.2.0")]);

        Assert.Equal("SAIN", Only(plan, ModListActionKind.Keep).Name);
        Assert.True(plan.IsNoOp);
        Assert.False(plan.RequiresGameClosed);
    }

    [Fact]
    public void ATrailingZeroDoesNotCountAsADifferentVersion()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("WTT Black Division", 1263, 55, "1.2.1")),
            [Installed("WTT Black Division", 1263, "1.2.1.0")]);

        Assert.Single(plan.Keep);
        Assert.Empty(plan.Update);
    }

    [Fact]
    public void AnUnknownInstalledVersionIsNotTreatedAsAnUpdate()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426)]);

        Assert.Single(plan.Keep);
    }

    [Fact]
    public void ADifferentInstalledVersionIsAnUpdate()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.1.0")]);

        var action = Only(plan, ModListActionKind.Update);
        Assert.Equal("3.2.0", action.TargetVersion);
        Assert.Equal("3.1.0", action.InstalledVersion);
        Assert.False(action.IsDowngrade);
        Assert.True(plan.RequiresDownloads);
    }

    [Fact]
    public void AnOlderPinnedVersionIsFlaggedAsADowngrade()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 54, "3.1.0")),
            [Installed("SAIN", 2426, "3.2.0")]);

        Assert.True(Only(plan, ModListActionKind.Update).IsDowngrade);
    }

    [Fact]
    public void ADisabledModOnTheListIsEnabled()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.2.0", disabled: true)]);

        Assert.Equal("SAIN", Only(plan, ModListActionKind.Enable).Name);
        Assert.False(plan.RequiresDownloads);
        Assert.False(plan.RequiresGameClosed);
    }

    [Fact]
    public void ADisabledModAtTheWrongVersionIsEnabledRatherThanUpdated()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.1.0", disabled: true)]);

        Assert.Single(plan.Enable);
        Assert.Empty(plan.Update);
    }

    [Fact]
    public void ADisabledModAtTheWrongVersionCarriesTheUpdateOnTheSameAction()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.1.0", disabled: true)]);

        var action = Only(plan, ModListActionKind.Enable);
        Assert.True(action.NeedsUpdateAfterEnable);
        Assert.True(action.IsFetch);
        Assert.Equal("3.2.0", action.TargetVersion);
        Assert.Equal("3.1.0", action.InstalledVersion);
        Assert.True(plan.RequiresDownloads);
    }

    [Fact]
    public void ADisabledModAtTheRightVersionIsJustAnEnable()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.2.0", disabled: true)]);

        var action = Only(plan, ModListActionKind.Enable);
        Assert.False(action.NeedsUpdateAfterEnable);
        Assert.False(action.IsFetch);
        Assert.False(action.NeedsVersionLookup);
        Assert.False(plan.RequiresDownloads);
    }

    [Fact]
    public void AnEnableThatAlsoNeedsUpdatingNeedsAVersionLookupWhenItIsNotPinned()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, version: "3.2.0")),
            [Installed("SAIN", 2426, "3.1.0", disabled: true)]);

        var action = Only(plan, ModListActionKind.Enable);
        Assert.True(action.NeedsVersionLookup);
        Assert.Same(action, Assert.Single(plan.NeedingVersionLookup));
    }

    [Fact]
    public void AnEnableToAnOlderVersionIsFlaggedAsADowngrade()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 54, "3.1.0")),
            [Installed("SAIN", 2426, "3.2.0", disabled: true)]);

        var action = Only(plan, ModListActionKind.Enable);
        Assert.True(action.NeedsUpdateAfterEnable);
        Assert.True(action.IsDowngrade);
    }

    [Fact]
    public void AnInstalledModNotOnAnExclusiveListIsDisabled()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.2.0"), Installed("Realism", 1263, "1.4.2")]);

        var action = Only(plan, ModListActionKind.Disable);
        Assert.Equal("Realism", action.Name);
        Assert.Equal(1263, action.ModId);
        Assert.Null(action.Entry);
        Assert.True(plan.RequiresGameClosed);
    }

    [Fact]
    public void AnAdditiveListNeverDisablesAnything()
    {
        var plan = ModListPlanner.Build(
            List(ModListPolicy.Additive, Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.2.0"), Installed("Realism", 1263, "1.4.2")]);

        Assert.Empty(plan.Disable);
        Assert.False(plan.RequiresGameClosed);
    }

    [Fact]
    public void AnAlreadyDisabledModIsLeftAlone()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.2.0"), Installed("Realism", 1263, "1.4.2", disabled: true)]);

        Assert.Empty(plan.Disable);
        Assert.Single(plan.Actions);
    }

    [Fact]
    public void APinnedModIsNeverAutoDisabled()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.2.0"), Installed("SVM", folders: ["ServerValueModifier"])],
            new HashSet<string> { "servervaluemodifier" });

        Assert.Empty(plan.Disable);
    }

    [Fact]
    public void APinnedModIsMatchedByDisplayNameToo()
    {
        var plan = ModListPlanner.Build(
            Exclusive(),
            [Installed("SVM")],
            new HashSet<string> { "svm" });

        Assert.Empty(plan.Disable);
    }

    [Fact]
    public void AnEntryWithNoModIdIsMatchedByPluginGuid()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("Amands Graphics", guid: "com.Amanda.Graphics")),
            [Installed("AmandsGraphics", guid: "com.amanda.graphics")]);

        Assert.Single(plan.Keep);
        Assert.Empty(plan.Disable);
    }

    [Fact]
    public void AnEntryWithNoModIdOrGuidIsMatchedByFolderName()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("Server Value Modifier", folders: ["svm"])),
            [Installed("SVM", folders: ["svm"])]);

        Assert.Single(plan.Keep);
        Assert.Empty(plan.Manual);
    }

    [Fact]
    public void AnEntryFallsBackToMatchingOnDisplayName()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SVM")),
            [Installed("svm")]);

        Assert.Single(plan.Keep);
    }

    [Fact]
    public void OneInstalledModIsOnlyClaimedByOneEntry()
    {
        var plan = ModListPlanner.Build(
            Exclusive(Entry("SAIN", 2426, 55, "3.2.0"), Entry("SAIN", 2426, 55, "3.2.0")),
            [Installed("SAIN", 2426, "3.2.0")]);

        Assert.Equal(2, plan.Keep.Count());
        Assert.Empty(plan.Disable);
    }

    [Fact]
    public void APlanCarriesTheListItCameFrom()
    {
        var list = Exclusive(Entry("SAIN", 2426, 55, "3.2.0"));

        var plan = ModListPlanner.Build(list, []);

        Assert.Equal(list.Id, plan.ListId);
        Assert.Equal("Fika night", plan.ListName);
        Assert.Equal(ModListPolicy.Exclusive, plan.Policy);
    }

    [Fact]
    public void AFullSwitchProducesEveryKindOfAction()
    {
        var plan = ModListPlanner.Build(
            Exclusive(
                Entry("SAIN", 2426, 55, "3.2.0"),
                Entry("Realism", 1263, 40, "1.4.2"),
                Entry("Amands Graphics", 900, 12, "3.0.0"),
                Entry("FixPluginTypesSerialization")),
            [
                Installed("Realism", 1263, "1.4.1"),
                Installed("Amands Graphics", 900, "3.0.0", disabled: true),
                Installed("Looting Bots", 700, "1.5.0"),
                Installed("SVM", folders: ["svm"]),
            ],
            new HashSet<string> { "svm" });

        Assert.Single(plan.Install);
        Assert.Single(plan.Update);
        Assert.Single(plan.Enable);
        Assert.False(Only(plan, ModListActionKind.Enable).NeedsUpdateAfterEnable);
        Assert.Single(plan.Manual);
        Assert.Equal("Looting Bots", Only(plan, ModListActionKind.Disable).Name);
        Assert.Empty(plan.Keep);
        Assert.True(plan.RequiresDownloads);
        Assert.True(plan.RequiresGameClosed);
        Assert.False(plan.IsNoOp);
    }
}
