using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class DependencyStatusResolverTests
{
    private static DependencyNode Node(bool conflict = false) => new() { Id = 902, Name = "BigBrain", Conflict = conflict };

    [Fact]
    public void Resolve_NotInstalledWhenNothingIsOnDisk() =>
        Assert.Equal(ModStatus.NotInstalled, DependencyStatusResolver.Resolve(Node(), null, "1.3.0"));

    [Fact]
    public void Resolve_InstalledWhenTheDiskVersionMatches() =>
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve(Node(), "1.3.0", "1.3.0"));

    // The required version is the newest that satisfies the constraint and this SPT, not a minimum -
    // one above it doesn't fit (OPEN-20 X2). Was Installed until 2026-10-05.
    [Fact]
    public void Resolve_TooNewWhenTheDiskVersionIsNewerThanRequired() =>
        Assert.Equal(ModStatus.TooNew, DependencyStatusResolver.Resolve(Node(), "1.4.0", "1.3.0"));

    [Fact]
    public void Resolve_InstalledWhenTheScannedVersionCarriesAnExtraZero() =>
        // The scanner reports a DLL's file version as "1.3.0.0" against a published "1.3.0".
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve(Node(), "1.3.0.0", "1.3.0"));

    [Fact]
    public void Resolve_UpdateAvailableWhenTheDiskVersionIsOlder() =>
        Assert.Equal(ModStatus.UpdateAvailable, DependencyStatusResolver.Resolve(Node(), "1.2.0", "1.3.0"));

    [Fact]
    public void Resolve_AHotfixIsAnUpdateOverTheRecordedRelease() =>
        Assert.Equal(ModStatus.UpdateAvailable, DependencyStatusResolver.Resolve(Node(), "1.3.0", "1.3.0-hotfix"));

    [Fact]
    public void Resolve_AVersionReadOffTheFilesIgnoresTheLabel() =>
        Assert.Equal(ModStatus.Installed,
            DependencyStatusResolver.Resolve(Node(), "1.3.0.0", "1.3.0-hotfix", installedVersionFromFiles: true));

    [Fact]
    public void Resolve_NoCompatibleVersionWhenNothingPublishedFitsAndItIsMissing() =>
        // latest_compatible_version comes back null when no release suits the installed SPT.
        Assert.Equal(ModStatus.NoCompatibleVersion, DependencyStatusResolver.Resolve(Node(), null, null));

    [Fact]
    public void Resolve_InstalledEvenWhenNoCompatibleVersionIsPublished() =>
        // Already on disk and nothing newer to move to - not a problem to flag.
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve(Node(), "1.2.0", null));

    [Theory]
    [InlineData(null, "1.3.0")]
    [InlineData("1.2.0", "1.3.0")]
    [InlineData("1.3.0", "1.3.0")]
    public void Resolve_ConflictOutranksWhateverIsOnDisk(string? installed, string? required) =>
        Assert.Equal(ModStatus.Conflict, DependencyStatusResolver.Resolve(Node(conflict: true), installed, required));

    [Fact]
    public void Worst_IsInstalledForAnEmptyTree() =>
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Worst([]));

    [Fact]
    public void Worst_PicksTheMostSevere() =>
        Assert.Equal(
            ModStatus.NotInstalled,
            DependencyStatusResolver.Worst([ModStatus.Installed, ModStatus.UpdateAvailable, ModStatus.NotInstalled]));

    [Fact]
    public void Worst_RanksConflictAboveEverythingElse() =>
        Assert.Equal(
            ModStatus.Conflict,
            DependencyStatusResolver.Worst([ModStatus.NotInstalled, ModStatus.Conflict, ModStatus.UpdateAvailable]));

    [Fact]
    public void Worst_IsInstalledWhenEverythingIsSatisfied() =>
        Assert.Equal(
            ModStatus.Installed,
            DependencyStatusResolver.Worst([ModStatus.Installed, ModStatus.Installed]));

    [Fact]
    public void Severity_OrdersMissingAboveOutdated() =>
        Assert.True(
            DependencyStatusResolver.Severity(ModStatus.NotInstalled)
            < DependencyStatusResolver.Severity(ModStatus.UpdateAvailable));

    [Fact]
    public void Resolve_AVersionReadOffTheFiles_IsOnlyTooNewAcrossAMajorVersion()
    {
        Assert.Equal(ModStatus.Installed,
            DependencyStatusResolver.Resolve(Node(), "2.5.0.0", "2.4.1", installedVersionFromFiles: true));
        Assert.Equal(ModStatus.TooNew,
            DependencyStatusResolver.Resolve(Node(), "3.0.0.0", "2.4.1", installedVersionFromFiles: true));
    }

    [Fact]
    public void Worst_RanksTooNewAboveAnUpdate() =>
        Assert.Equal(ModStatus.TooNew, DependencyStatusResolver.Worst([ModStatus.UpdateAvailable, ModStatus.TooNew, ModStatus.Installed]));
}
