using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-12 F11: what a held-back update still leaves on offer.
public class HeldBackVersionsTests
{
    private static readonly DateTimeOffset Day = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static ModVersionSummary V(string version, int day, string spt = "~4.0.0") => new()
    {
        Version = version,
        SptVersionConstraint = spt,
        PublishedAt = Day.AddDays(day),
    };

    private static readonly List<ModVersionSummary> CommonLib =
    [
        V("2.0.24", 0),
        V("2.0.31", 5),
        V("2.1.0", 8),
        V("3.0.6", 10),
        V("2.0.40", 12, spt: "~3.11.0"),
    ];

    private static HeldBackUpdate Held(params (string Name, string? Constraint)[] blockers) =>
        new(1500, "3.0.6", "dependency_conflict", [.. blockers.Select((b, i) => new HeldBackBlocker(1600 + i, b.Name, b.Constraint))]);

    [Fact]
    public void Each_blocker_gets_the_newest_version_it_accepts_on_this_SPT()
    {
        var resolved = HeldBackVersions.Resolve(Held(("Black and Blue", "~2.0.24")), CommonLib, "4.0.13");

        // 2.0.40 fits the range but not SPT 4.0; 3.0.6 is the held release itself.
        Assert.Equal(["2.0.31"], resolved.PerBlocker);
        Assert.Equal("2.0.31", resolved.Safe);
    }

    [Fact]
    public void Safe_is_the_newest_every_blocker_accepts()
    {
        var resolved = HeldBackVersions.Resolve(
            Held(("Black and Blue", "~2.0.24"), ("Other", "^2.0.0")), CommonLib, "4.0.13");

        Assert.Equal(["2.0.31", "2.1.0"], resolved.PerBlocker);
        Assert.Equal("2.0.31", resolved.Safe);
    }

    [Fact]
    public void A_blocker_with_no_range_promises_nothing()
    {
        var resolved = HeldBackVersions.Resolve(
            Held(("Black and Blue", "~2.0.24"), ("Unknown", null)), CommonLib, "4.0.13");

        Assert.Equal(["2.0.31", null], resolved.PerBlocker);
        Assert.Null(resolved.Safe);
    }

    [Fact]
    public void A_chain_conflict_with_no_blockers_promises_nothing()
    {
        var resolved = HeldBackVersions.Resolve(
            new HeldBackUpdate(1500, "3.0.6", HeldBackUpdate.ChainReason, []), CommonLib, "4.0.13");

        Assert.Empty(resolved.PerBlocker);
        Assert.Null(resolved.Safe);
    }
}
