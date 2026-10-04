using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class SpModListReviewTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Friday = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static SpModListPage Page(params SpModListItem[] items) => new()
    {
        ListId = 4242,
        Source = new Uri("https://sp-mod.com/list/4242/fika-night"),
        Name = "Fika night",
        Author = "someone",
        SptVersion = "4.0.13",
        Pass = SpModListReadPass.Structural,
        Items = items,
        Notices = [],
    };

    private static SpModListItem Mod(int id, string version = "1.0.0") => new(SpModListItemKind.Mod, id, $"Mod {id}", version);

    private static SpModListItem Addon(int id, int parent, bool parentOnList = true, string? parentName = null, string? parentVersion = null) =>
        new(SpModListItemKind.Addon, id, $"Addon {id}", "2.0.0", parent, parentOnList, ParentName: parentName, ParentVersion: parentVersion);

    private static ModListEntry E(int id, string version, bool addon = false) =>
        new() { Name = (addon ? "Addon " : "Mod ") + id, ModId = id, IsAddon = addon, Version = version };

    private static SpModRetarget Retarget(params SpModRetargetRow[] rows) => new("4.1.6", rows);

    private static SpModRetargetRow Changed(ModListEntry from, string to) =>
        new(from, new ModListEntry { Name = from.Name, ModId = from.ModId, IsAddon = from.IsAddon, Version = to, VersionId = 77 },
            SpModRetargetOutcome.Changed);

    private static SpModDependency Dep(int id, string? version = "1.0.0", bool parent = false, params string[] neededBy) =>
        new(id, $"Dep {id}", version, version is null ? null : id * 10, neededBy.Length == 0 ? ["Mod 1"] : neededBy, parent, false);

    private static SpModDependencies Deps(params SpModDependency[] deps) => new("4.1.6", deps, []);

    // ---- sections and defaults ----

    [Fact]
    public void APlainPageIsAllTickedInPageSections()
    {
        var review = SpModListReview.Build(Page(Mod(1), Mod(2), Addon(5, parent: 2)));

        Assert.Equal([SpModReviewSection.Mods, SpModReviewSection.Mods, SpModReviewSection.Addons], review.Rows.Select(r => r.Section));
        Assert.All(review.Rows, r => Assert.True(r.Ticked));
        Assert.Equal("Mod 2", review.In(SpModReviewSection.Addons).Single().ParentName);
        Assert.All(review.Rows, r => Assert.Equal(SpModReviewChange.None, r.Change));
    }

    [Fact]
    public void ADetachedParentIsOfferedFromThePageAlone()
    {
        var review = SpModListReview.Build(Page(Addon(135, parent: 2706, parentOnList: false, "ORBIT 2.0", "2.1.1")));

        var parent = Assert.Single(review.In(SpModReviewSection.MissingParents));
        Assert.Equal("mod/2706", parent.Ref);
        Assert.Equal(("ORBIT 2.0", "2.1.1"), (parent.Entry.Name, parent.Entry.Version));
        Assert.True(parent.Ticked);
        Assert.False(parent.OnPage);
        Assert.Equal(["Addon 135"], parent.NeededBy);
        Assert.Equal("ORBIT 2.0", review.In(SpModReviewSection.Addons).Single().ParentName);
    }

    [Fact]
    public void TheDependencyAnswerGivesAMissingParentItsVersion()
    {
        var review = SpModListReview.Build(
            Page(Addon(135, parent: 2706, parentOnList: false, "ORBIT 2.0", "2.1.1")),
            dependencies: Deps(Dep(2706, "2.1.0", parent: true)));

        var parent = Assert.Single(review.In(SpModReviewSection.MissingParents));
        Assert.Equal("2.1.0", parent.Entry.Version);
        Assert.Equal(27060, parent.Entry.VersionId);
        Assert.Empty(review.In(SpModReviewSection.Dependencies));
    }

    [Fact]
    public void DependenciesAreTickedAndUnaddableOnesAreOnlyReported()
    {
        var review = SpModListReview.Build(Page(Mod(1)), dependencies: Deps(Dep(50), Dep(51, version: null)));

        var dep = Assert.Single(review.In(SpModReviewSection.Dependencies));
        Assert.Equal(50, dep.Entry.ModId);
        Assert.True(dep.Ticked);
        Assert.Equal(51, Assert.Single(review.Unaddable).ModId);
    }

    [Fact]
    public void ADependencyAlreadyOnThePageIsNotOfferedTwice()
    {
        var review = SpModListReview.Build(Page(Mod(1), Mod(50)), dependencies: Deps(Dep(50)));

        Assert.Single(review.Rows, r => r.Ref == "mod/50");
        Assert.Empty(review.In(SpModReviewSection.Dependencies));
    }

    [Fact]
    public void ARetargetChangesVersionsAndSetsNoVersionAside()
    {
        var review = SpModListReview.Build(
            Page(Mod(1), Mod(2)),
            retarget: Retarget(Changed(E(1, "1.0.0"), "1.1.0"), new SpModRetargetRow(E(2, "1.0.0"), E(2, "1.0.0"), SpModRetargetOutcome.NoVersion)));

        var one = Assert.Single(review.Rows, r => r.Ref == "mod/1");
        Assert.Equal(("1.1.0", "1.0.0"), (one.Entry.Version, one.FromVersion));
        Assert.True(one.Ticked);

        var two = Assert.Single(review.In(SpModReviewSection.NoVersion));
        Assert.Equal("mod/2", two.Ref);
        Assert.False(two.Ticked);
        Assert.True(review.Retargeted);
    }

    [Fact]
    public void AFailedRetargetIsIgnored()
    {
        var failed = new SpModRetarget("4.1.6", [], SpModResolveFailure.Api, "offline");

        var review = SpModListReview.Build(Page(Mod(1)), retarget: failed);

        Assert.False(review.Retargeted);
        Assert.Equal("1.0.0", Assert.Single(review.Rows).Entry.Version);
    }

    [Fact]
    public void MissingParentsComeFirst()
    {
        var review = SpModListReview.Build(
            Page(Mod(1), Addon(135, parent: 2706, parentOnList: false, "ORBIT", "2.1.1")),
            dependencies: Deps(Dep(50)));

        Assert.Equal(
            [SpModReviewSection.MissingParents, SpModReviewSection.Mods, SpModReviewSection.Addons, SpModReviewSection.Dependencies],
            review.Rows.Select(r => r.Section));
    }

    // ---- into a list ----

    [Fact]
    public void TheTicksMakeTheList()
    {
        var review = SpModListReview.Build(
            Page(Mod(1), Mod(2), Addon(135, parent: 2706, parentOnList: false, "ORBIT", "2.1.1")),
            retarget: Retarget(Changed(E(1, "1.0.0"), "1.1.0")),
            dependencies: Deps(Dep(50), Dep(60)));

        review.Rows.Single(r => r.Ref == "mod/2").Ticked = false;
        review.Rows.Single(r => r.Ref == "mod/60").Ticked = false;

        var list = review.ToUpdate("x", Monday).List;

        Assert.Equal(["mod/1", "addon/135", "mod/2706", "mod/50"], list.Entries.Select(e => SpModListSource.RefFor(e)));
        Assert.Equal("1.1.0", list.Entries[0].Version);
        Assert.Equal("4.1.6", list.SptVersion);

        var source = list.SpModSource!;
        Assert.True(source.Retargeted);
        Assert.True(source.DependenciesAdded);
        Assert.Equal(["mod/2", "mod/60"], source.Excluded);
        Assert.Equal(["mod/2706", "mod/50"], source.Added);
    }

    [Fact]
    public void WithoutARetargetThePagesTargetStays()
    {
        var list = SpModListReview.Build(Page(Mod(1))).ToUpdate("x", Monday).List;

        Assert.Equal("4.0.13", list.SptVersion);
        Assert.False(list.SpModSource!.Retargeted);
        Assert.False(list.SpModSource.DependenciesAdded);
    }

    [Fact]
    public void AnUntickedNoVersionRowStaysOutAndATickedOneKeepsThePageVersion()
    {
        var retarget = Retarget(
            new SpModRetargetRow(E(1, "1.0.0"), E(1, "1.0.0"), SpModRetargetOutcome.NoVersion),
            new SpModRetargetRow(E(2, "1.0.0"), E(2, "1.0.0"), SpModRetargetOutcome.NoVersion));
        var review = SpModListReview.Build(Page(Mod(1), Mod(2)), retarget: retarget);

        review.Rows.Single(r => r.Ref == "mod/2").Ticked = true;
        var list = review.ToUpdate("x", Monday).List;

        Assert.Equal(["mod/2"], list.Entries.Select(e => SpModListSource.RefFor(e)));
        Assert.Equal("1.0.0", list.Entries[0].Version);
    }

    // ---- rebuilds and refreshes ----

    [Fact]
    public void ARebuildKeepsTheUsersTicks()
    {
        var first = SpModListReview.Build(Page(Mod(1), Mod(2)), dependencies: Deps(Dep(50)));
        first.Rows.Single(r => r.Ref == "mod/2").Ticked = false;
        first.Rows.Single(r => r.Ref == "mod/50").Ticked = false;

        var again = SpModListReview.Build(Page(Mod(1), Mod(2)), dependencies: Deps(Dep(50)), previous: first.ToChoices());

        Assert.False(again.Rows.Single(r => r.Ref == "mod/2").Ticked);
        Assert.False(again.Rows.Single(r => r.Ref == "mod/50").Ticked);
        Assert.True(again.Rows.Single(r => r.Ref == "mod/1").Ticked);
    }

    [Fact]
    public void ARefreshMarksWhatChangedAndListsWhatWentAway()
    {
        var stored = SpModListReview.Build(Page(Mod(1), Mod(2), Mod(3))).ToUpdate("x", Monday).List;

        var review = SpModListReview.Build(Page(Mod(1), Mod(2, "1.5.0"), Mod(4)), stored);

        Assert.True(review.IsRefresh);
        Assert.Equal(SpModReviewChange.None, review.Rows.Single(r => r.Ref == "mod/1").Change);
        var two = review.Rows.Single(r => r.Ref == "mod/2");
        Assert.Equal((SpModReviewChange.VersionChanged, "1.0.0"), (two.Change, two.StoredVersion));
        Assert.Equal(SpModReviewChange.New, review.Rows.Single(r => r.Ref == "mod/4").Change);
        Assert.Equal([3], review.Removed.Select(e => e.ModId));

        var update = review.ToUpdate("x", Friday);
        Assert.True(update.HasChanges);
        Assert.Equal(2, update.List.Revision);
    }

    [Fact]
    public void ARefreshRemembersWhatWasUntickedAndAdded()
    {
        var first = SpModListReview.Build(Page(Mod(1), Mod(2)), dependencies: Deps(Dep(50), Dep(60)));
        first.Rows.Single(r => r.Ref == "mod/2").Ticked = false;
        first.Rows.Single(r => r.Ref == "mod/60").Ticked = false;
        var stored = first.ToUpdate("x", Monday).List;

        // This read didn't check dependencies: the one added last time stays, the one declined stays out.
        var review = SpModListReview.Build(Page(Mod(1), Mod(2)), stored);

        Assert.False(review.Rows.Single(r => r.Ref == "mod/2").Ticked);
        Assert.True(review.Rows.Single(r => r.Ref == "mod/50").Ticked);
        Assert.DoesNotContain(review.Rows, r => r.Ref == "mod/60");
        Assert.Empty(review.Removed);
        Assert.False(review.ToUpdate("x", Friday).HasChanges);
    }

    [Fact]
    public void AnItemLeftOutLastTimeIsNotNew()
    {
        var first = SpModListReview.Build(Page(Mod(1), Mod(2)));
        first.Rows.Single(r => r.Ref == "mod/2").Ticked = false;
        var stored = first.ToUpdate("x", Monday).List;

        var review = SpModListReview.Build(Page(Mod(1), Mod(2)), stored);

        Assert.Equal(SpModReviewChange.None, review.Rows.Single(r => r.Ref == "mod/2").Change);
    }

    [Fact]
    public void ARefreshThatRetargetsAgainIsUnchangedWhenTheVersionsAreTheSame()
    {
        var retarget = Retarget(Changed(E(1, "1.0.0"), "1.1.0"));
        var stored = SpModListReview.Build(Page(Mod(1)), retarget: retarget).ToUpdate("x", Monday).List;

        var review = SpModListReview.Build(Page(Mod(1)), stored, retarget);

        Assert.Equal(SpModReviewChange.None, Assert.Single(review.Rows).Change);
        Assert.False(review.ToUpdate("x", Friday).HasChanges);
    }

    [Fact]
    public void ADependencyAddedEarlierThatTheRefreshResolvesAgainIsNotNew()
    {
        var stored = SpModListReview.Build(Page(Mod(1)), dependencies: Deps(Dep(50))).ToUpdate("x", Monday).List;

        var review = SpModListReview.Build(Page(Mod(1)), stored, dependencies: Deps(Dep(50)));

        Assert.Equal(SpModReviewChange.None, review.Rows.Single(r => r.Ref == "mod/50").Change);
        Assert.False(review.ToUpdate("x", Friday).HasChanges);
    }
}
