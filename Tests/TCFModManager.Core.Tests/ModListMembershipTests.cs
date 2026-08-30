using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModListMembershipTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static ModList List(string name, bool isSnapshot = false, params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsSnapshot = isSnapshot,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        list.Entries.AddRange(entries);
        return list;
    }

    private static ModListEntry Entry(string name, int? modId = null, string? guid = null, string[]? folders = null)
    {
        var entry = new ModListEntry { Name = name, ModId = modId, Guid = guid };
        if (folders is not null) entry.Folders.AddRange(folders);
        return entry;
    }

    private static ModListCandidate Installed(
        string name,
        int? modId = null,
        string? guid = null,
        bool disabled = false,
        string[]? folders = null) =>
        new()
        {
            Name = name,
            ModId = modId,
            Guid = guid,
            IsDisabled = disabled,
            Folders = folders ?? [name.ToLowerInvariant()],
        };

    [Fact]
    public void AModInTwoListsReportsBoth()
    {
        var installed = new[] { Installed("SAIN", 2426), Installed("Realism", 1263) };

        var names = ModListMembership.Names(
            [List("Fika night", entries: [Entry("SAIN", 2426), Entry("Realism", 1263)]),
             List("Solo", entries: [Entry("SAIN", 2426)])],
            installed);

        Assert.Equal(["Fika night", "Solo"], names[0]);
        Assert.Equal(["Fika night"], names[1]);
    }

    [Fact]
    public void AModInNoListReportsNothing()
    {
        var names = ModListMembership.Names(
            [List("Fika night", entries: [Entry("SAIN", 2426)])],
            [Installed("Realism", 1263)]);

        Assert.Empty(Assert.Single(names));
    }

    [Fact]
    public void AModTheListLeavesOutIsNotAMember()
    {
        // Realism isn't on the list, so an exclusive list would disable it - that is the opposite
        // of membership and must not read as a badge.
        var names = ModListMembership.Names(
            [List("Fika night", entries: [Entry("SAIN", 2426)])],
            [Installed("SAIN", 2426), Installed("Realism", 1263)]);

        Assert.Single(names[0]);
        Assert.Empty(names[1]);
    }

    [Fact]
    public void ADisabledModStillCountsAsAMember()
    {
        // Membership is about what a list names, not about what happens to be enabled right now -
        // that is exactly what makes the badge useful: switching to that list costs no download.
        var names = ModListMembership.Names(
            [List("Fika night", entries: [Entry("SAIN", 2426)])],
            [Installed("SAIN", 2426, disabled: true)]);

        Assert.Equal(["Fika night"], Assert.Single(names));
    }

    [Fact]
    public void MembershipUsesTheSameMatchingAsApplying()
    {
        // No mod id on either side - matched by folder name, the same way the planner would.
        var names = ModListMembership.Names(
            [List("Fika night", entries: [Entry("Server Value Modifier", folders: ["svm"])])],
            [Installed("SVM", folders: ["svm"])]);

        Assert.Equal(["Fika night"], Assert.Single(names));
    }

    [Fact]
    public void TheUndoSnapshotIsNeverShownAsAList()
    {
        var names = ModListMembership.Names(
            [List("before the last apply", isSnapshot: true, entries: [Entry("SAIN", 2426)])],
            [Installed("SAIN", 2426)]);

        Assert.Empty(Assert.Single(names));
    }

    [Fact]
    public void TwoModsWithIdenticalFieldsAreCountedSeparately()
    {
        // ModListCandidate is a record, so keying membership on the candidate itself would fold
        // these two together.
        var names = ModListMembership.Names(
            [List("Fika night", entries: [Entry("SAIN", 2426)])],
            [Installed("SAIN", 2426), Installed("SAIN", 2426)]);

        Assert.Equal(2, names.Count);
        Assert.Single(names[0]);
        Assert.Empty(names[1]);
    }

    [Fact]
    public void NoListsMeansNoMembershipAndNoWork()
    {
        var names = ModListMembership.Names([], [Installed("SAIN", 2426)]);

        Assert.Empty(Assert.Single(names));
    }
}
