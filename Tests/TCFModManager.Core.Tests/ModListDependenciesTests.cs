using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-22: the mods a list's entries need that the list doesn't name, offered on its preview.
public class ModListDependenciesTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

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

    private static ModListEntry Entry(string name, int modId, string version) =>
        new() { Name = name, ModId = modId, VersionId = modId * 10, Version = version };

    private static ModListCandidate Installed(string name, int modId, string version, bool disabled = false) =>
        new() { Name = name, ModId = modId, Version = version, IsDisabled = disabled, Folders = [name] };

    private static SpModDependency Dep(int id, string name, string? version, params string[] neededBy) =>
        new(id, name, version, version is null ? null : id * 10, neededBy, false, false);

    private static SpModDependencies Answer(params SpModDependency[] missing) => new("4.1.6", missing, []);

    [Fact]
    public void MissingDisabledAndSweptDependenciesAreOffered_OthersAreNot()
    {
        var list = Exclusive(Entry("WTT Armory", 1, "2.0.0"));
        var installed = new List<ModListCandidate>
        {
            Installed("WTT Armory", 1, "2.0.0"),
            Installed("CommonLib", 2, "3.0.6", disabled: true),
            Installed("Shared", 3, "1.0.0"),
        };

        var planWithout = ModListPlanner.Build(list, installed);
        var (offers, noVersion) = ModListDependencies.Offer(
            Answer(Dep(2, "CommonLib", "3.0.6", "WTT Armory"), Dep(3, "Shared", "1.0.0", "WTT Armory"),
                Dep(4, "Missing", "1.2.0", "WTT Armory"), Dep(5, "Nothing for 4.1", null, "WTT Armory")),
            installed, planWithout);

        Assert.Equal([ModListDependencyNeed.Enable, ModListDependencyNeed.KeepEnabled, ModListDependencyNeed.Install],
            offers.Select(o => o.Need));
        Assert.Equal("Nothing for 4.1", Assert.Single(noVersion).Name);
    }

    [Fact]
    public void AnInstalledDependencyTheListLeavesAloneIsNotOffered()
    {
        var list = new ModList { Id = Guid.NewGuid(), Name = "Additive", Policy = ModListPolicy.Additive, CreatedAt = Timestamp, UpdatedAt = Timestamp };
        list.Entries.Add(Entry("WTT Armory", 1, "2.0.0"));
        var installed = new List<ModListCandidate> { Installed("WTT Armory", 1, "2.0.0"), Installed("CommonLib", 2, "3.0.3") };

        var (offers, _) = ModListDependencies.Offer(Answer(Dep(2, "CommonLib", "3.0.6", "WTT Armory")), installed,
            ModListPlanner.Build(list, installed));

        Assert.Empty(offers);
    }

    [Fact]
    public void TickedDependenciesAreInstalledEnabledAndSparedFromTheSweep()
    {
        var list = Exclusive(Entry("WTT Armory", 1, "2.0.0"));
        var installed = new List<ModListCandidate>
        {
            Installed("WTT Armory", 1, "2.0.0"),
            Installed("CommonLib", 2, "3.0.3", disabled: true),
            Installed("Shared", 3, "1.0.0"),
            Installed("Unrelated", 9, "1.0.0"),
        };

        var plan = ModListPlanner.Build(list, installed, dependencies:
        [
            new ModListDependencyPick(Entry("CommonLib", 2, "3.0.6"), ["WTT Armory"]),
            new ModListDependencyPick(Entry("Shared", 3, "1.0.0"), ["WTT Armory"]),
            new ModListDependencyPick(Entry("Missing", 4, "1.2.0"), ["WTT Armory"]),
        ]);

        var enable = Assert.Single(plan.Enable);
        Assert.True(enable.IsDependency);
        Assert.False(enable.NeedsUpdateAfterEnable);
        Assert.Equal(["WTT Armory"], enable.NeededBy);

        Assert.Contains(plan.Keep, a => a.IsDependency && a.Name == "Shared");
        Assert.Equal("Missing", Assert.Single(plan.Install).Name);
        Assert.Equal("Unrelated", Assert.Single(plan.Disable).Name);
        Assert.Empty(plan.Update);
    }

    [Fact]
    public void AddingToTheListSkipsWhatIsAlreadyOnIt()
    {
        var list = Exclusive(Entry("WTT Armory", 1, "2.0.0"), Entry("CommonLib", 2, "3.0.6"));

        var entries = ModListDependencies.WithAdded(list,
        [
            new ModListDependencyPick(Entry("CommonLib", 2, "3.0.6"), ["WTT Armory"]),
            new ModListDependencyPick(Entry("Shared", 3, "1.0.0"), ["WTT Armory"]),
        ]);

        Assert.Equal(["CommonLib", "Shared", "WTT Armory"], entries.Select(e => e.Name).Order());
    }
}
