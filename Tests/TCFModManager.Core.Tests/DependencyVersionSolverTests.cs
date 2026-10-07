using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-23 S1. Fixtures shaped on the CommonLib case from the design doc's §2.
public class DependencyVersionSolverTests
{
    private static InstalledMod Entry(
        string name, string guid, string? version, InstalledModTarget side = InstalledModTarget.Server,
        bool disabled = false, params ModDependencyRef[] deps) =>
        new()
        {
            Name = name,
            Guid = guid,
            Version = version,
            Target = side,
            FolderPath = $"C:/SPT/{side}/{name}",
            IsDisabled = disabled,
            Dependencies = deps,
        };

    private static SolverMod Mod(string name, int? id, params InstalledMod[] entries) => new(name, name, id, entries);

    private static ModDependencyRef Needs(string guid, string? range, bool soft = false) => new(guid, soft, range);

    private static SolverMod CommonLib(string version = "3.0.6", bool disabled = false) =>
        Mod("CommonLib", 2310, Entry("WTT-ServerCommonLib", "com.wtt.commonlib", version, disabled: disabled));

    private static readonly string[] CommonLibVersions = ["3.0.6", "3.0.5", "3.0.4", "3.0.3", "3.0.2", "3.0.0", "2.0.24", "2.0.22"];

    private static IEnumerable<string> Published(int id) => id == 2310 ? CommonLibVersions : [];

    private static SolverMod Dependent(string name, int id, string range, InstalledModTarget side = InstalledModTarget.Server, bool soft = false) =>
        Mod(name, id, Entry(name, "com.test." + name.ToLowerInvariant(), "1.0.0", side, false, Needs("com.wtt.commonlib", range, soft)));

    [Fact]
    public void Solve_AllRangesMet_NoProblem()
    {
        var report = Assert.Single(DependencyVersionSolver.Solve(
            [CommonLib(), Dependent("Msga", 1453, "~3.0.0"), Dependent("Eco", 2882, "^3.0.0")], Published));

        Assert.Equal(2, report.Requirements.Count);
        Assert.All(report.Requirements, r => Assert.Equal(RequirementStanding.Met, r.Standing));
        Assert.False(report.HasFileProblem);
        Assert.False(report.Conflict);
        Assert.False(report.InstallWide);
        Assert.Null(report.FixVersion);
    }

    [Fact]
    public void Solve_OneMissed_OfferTheNewestVersionThatMeetsEveryone()
    {
        var report = Assert.Single(DependencyVersionSolver.Solve(
            [CommonLib(), Dependent("Msga", 1453, ">=3.0.0"), Dependent("Eco", 2882, "<=3.0.4")], Published));

        var eco = Assert.Single(report.Requirements, r => r.Dependent.Name == "Eco");
        Assert.Equal(RequirementStanding.Missed, eco.Standing);
        Assert.Equal("3.0.6", eco.Found);
        Assert.False(report.Conflict);
        Assert.Equal("3.0.4", report.FixVersion);
        Assert.True(report.InstallWide);
    }

    [Fact]
    public void Solve_NoSingleVersionFits_IsAConflict()
    {
        var report = Assert.Single(DependencyVersionSolver.Solve(
            [CommonLib(), Dependent("Msga", 1453, "3.0.6"), Dependent("Eco", 2882, "3.0.3")], Published));

        Assert.True(report.Conflict);
        Assert.Null(report.FixVersion);
    }

    [Fact]
    public void Solve_WithoutPublishedVersions_ConflictIsLeftUnanswered()
    {
        var report = Assert.Single(DependencyVersionSolver.Solve(
            [CommonLib(), Dependent("Msga", 1453, "3.0.6"), Dependent("Eco", 2882, "3.0.3")]));

        Assert.True(report.HasFileProblem);
        Assert.False(report.Conflict);
        Assert.Null(report.FixVersion);
    }

    [Fact]
    public void Solve_AClientMiss_IsNotInstallWide()
    {
        var lib = Mod("Lib", 7, Entry("Lib", "com.wtt.commonlib", "2.0.0", InstalledModTarget.Client));
        var report = Assert.Single(DependencyVersionSolver.Solve([lib, Dependent("Eco", 2882, ">=3.0.0", InstalledModTarget.Client)]));

        Assert.True(report.HasFileProblem);
        Assert.False(report.InstallWide);
    }

    [Fact]
    public void Solve_AServerDependencyOnlyDisabled_IsMissingAndInstallWide()
    {
        var report = Assert.Single(DependencyVersionSolver.Solve([CommonLib(disabled: true), Dependent("Eco", 2882, "^3.0.0")]));

        Assert.Equal(RequirementStanding.Missing, Assert.Single(report.Requirements).Standing);
        Assert.True(report.InstallWide);
    }

    [Fact]
    public void Solve_AServerDependencyNotInstalled_IsReportedByIdentifier()
    {
        var report = Assert.Single(DependencyVersionSolver.Solve([Dependent("Eco", 2882, "^3.0.0")]));

        Assert.Null(report.Dependency);
        Assert.Equal("com.wtt.commonlib", report.Identifier);
        Assert.True(report.InstallWide);
    }

    [Fact]
    public void Solve_AClientDependencyNothingAnswers_IsLeftToTheSpModTree() =>
        // [BepInDependency("com.SPT.core")] - SPT's own plugins are never scanned.
        Assert.Empty(DependencyVersionSolver.Solve([Dependent("Eco", 2882, ">=4.0.0", InstalledModTarget.Client)]));

    [Fact]
    public void Solve_AnOptionalMiss_IsListedButNotAProblem()
    {
        var report = Assert.Single(DependencyVersionSolver.Solve([CommonLib(), Dependent("Eco", 2882, "3.0.3", soft: true)], Published));

        var req = Assert.Single(report.Requirements);
        Assert.True(req.IsOptional);
        Assert.Equal(RequirementStanding.Missed, req.Standing);
        Assert.False(report.HasFileProblem);
        Assert.False(report.InstallWide);
        Assert.Null(report.FixVersion);
    }

    [Fact]
    public void Solve_ADisabledDependentAsksForNothing() =>
        Assert.Empty(DependencyVersionSolver.Solve(
            [CommonLib(), Mod("Eco", 2882, Entry("Eco", "eco", "1.0.0", disabled: true, deps: Needs("com.wtt.commonlib", "3.0.3")))]));

    [Fact]
    public void Solve_AModsTwoHalvesNeedingEachOther_AreNotReported()
    {
        var both = Mod("Toolkit", 5,
            Entry("Toolkit.Server", "com.author.toolkit", "1.0.0"),
            Entry("Toolkit.Client", "com.author.toolkit", "1.0.0", InstalledModTarget.Client, false, Needs("com.author.toolkit", ">=1.0.0")));

        Assert.Empty(DependencyVersionSolver.Solve([both]));
    }

    [Fact]
    public void Solve_SpModRange_IsAWarningNeverAConflict()
    {
        var report = Assert.Single(DependencyVersionSolver.Solve(
            [CommonLib(), Mod("Eco", 2882, Entry("Eco", "eco", "3.0.0"))], Published,
            [new SpModRequirement(2882, 2310, "3.0.3")]));

        var req = Assert.Single(report.Requirements);
        Assert.Equal(RequirementSource.SpMod, req.Source);
        Assert.Equal(RequirementStanding.Missed, req.Standing);
        Assert.True(report.HasSpModWarning);
        Assert.False(report.HasFileProblem);
        Assert.False(report.Conflict);
        Assert.False(report.InstallWide);
    }

    [Fact]
    public void Solve_InstallWideProblemsSortFirst()
    {
        var other = Mod("Other", 9, Entry("Other", "com.other", "1.0.0", InstalledModTarget.Client));
        var reports = DependencyVersionSolver.Solve(
            [other, Mod("A", 1, Entry("A", "a", "1.0.0", InstalledModTarget.Client, false, Needs("com.other", ">=1.0.0"))),
             CommonLib(), Dependent("Eco", 2882, "3.0.3")], Published);

        Assert.Equal("CommonLib", reports[0].Dependency?.Name);
    }

    [Fact]
    public void WouldBreak_UpdatingTheDependencyPastADependentsRange()
    {
        var broken = DependencyVersionSolver.WouldBreak(
            [CommonLib("3.0.3"), Dependent("Eco", 2882, "<=3.0.4"), Dependent("Msga", 1453, ">=3.0.0")], 2310, null, "3.0.6");

        var eco = Assert.Single(broken);
        Assert.Equal("Eco", eco.Dependent.Name);
        Assert.Equal("<=3.0.4", eco.Range);
    }

    [Fact]
    public void WouldBreak_NothingWhenEveryRangeAcceptsTheNewVersion() =>
        Assert.Empty(DependencyVersionSolver.WouldBreak([CommonLib("3.0.3"), Dependent("Eco", 2882, "~3.0.0")], 2310, null, "3.0.6"));

    [Fact]
    public void WouldBreak_LeavesOutWhatIsAlreadyBroken() =>
        Assert.Empty(DependencyVersionSolver.WouldBreak([CommonLib("3.0.6"), Dependent("Eco", 2882, "3.0.3")], 2310, null, "3.0.5"));

    [Fact]
    public void WouldBreak_ANewInstallIsMatchedByGuid() =>
        Assert.Single(DependencyVersionSolver.WouldBreak([Dependent("Eco", 2882, "^2.0.0")], 2310, "com.wtt.commonlib", "3.0.6"));

    [Fact]
    public void WouldBreak_IgnoresOptionalDependencies() =>
        Assert.Empty(DependencyVersionSolver.WouldBreak([CommonLib("3.0.3"), Dependent("Eco", 2882, "3.0.3", soft: true)], 2310, null, "3.0.6"));
}
