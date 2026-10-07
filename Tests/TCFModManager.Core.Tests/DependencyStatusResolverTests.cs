using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class DependencyStatusResolverTests
{
    private static readonly DeclaredVersionMiss Miss = new("~3.0.3", "3.0.6", InstalledModTarget.Server);

    [Fact]
    public void Resolve_NotInstalledWhenNothingIsOnDisk() =>
        Assert.Equal(ModStatus.NotInstalled, DependencyStatusResolver.Resolve(null, "1.3.0"));

    [Fact]
    public void Resolve_InstalledWhenTheDiskVersionMatches() =>
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve("1.3.0", "1.3.0"));

    // OPEN-23 S0: the newest version sp-mod resolves is not an upper bound - it stops where sp-mod's
    // list was frozen. CommonLib 3.0.6 under Canted Aiming 2.0.0 (resolved 3.0.3) loads fine.
    [Fact]
    public void Resolve_ANewerVersionThanSpModResolvesIsInstalled() =>
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve("3.0.6", "3.0.3"));

    [Fact]
    public void Resolve_TooNewWhenSpModsRangeExcludesTheInstalledVersion() =>
        Assert.Equal(ModStatus.TooNew, DependencyStatusResolver.Resolve("3.0.6", "3.0.3", spModConstraint: "3.0.3"));

    [Fact]
    public void Resolve_InstalledWhenSpModsRangeAcceptsTheInstalledVersion() =>
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve("3.0.3", "3.0.3", spModConstraint: "~3.0.0"));

    [Fact]
    public void Resolve_SpModsRangeIsNotHeldAgainstAVersionReadOffTheFiles() =>
        Assert.Equal(ModStatus.Installed,
            DependencyStatusResolver.Resolve("3.0.6.0", "3.0.3", installedVersionFromFiles: true, spModConstraint: "3.0.3"));

    [Fact]
    public void Resolve_InstalledWhenTheScannedVersionCarriesAnExtraZero() =>
        // The scanner reports a DLL's file version as "1.3.0.0" against a published "1.3.0".
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve("1.3.0.0", "1.3.0"));

    [Fact]
    public void Resolve_UpdateAvailableWhenTheDiskVersionIsOlder() =>
        Assert.Equal(ModStatus.UpdateAvailable, DependencyStatusResolver.Resolve("1.2.0", "1.3.0"));

    [Fact]
    public void Resolve_AHotfixIsAnUpdateOverTheRecordedRelease() =>
        Assert.Equal(ModStatus.UpdateAvailable, DependencyStatusResolver.Resolve("1.3.0", "1.3.0-hotfix"));

    [Fact]
    public void Resolve_AVersionReadOffTheFilesIgnoresTheLabel() =>
        Assert.Equal(ModStatus.Installed,
            DependencyStatusResolver.Resolve("1.3.0.0", "1.3.0-hotfix", installedVersionFromFiles: true));

    [Fact]
    public void Resolve_NoCompatibleVersionWhenNothingPublishedFitsAndItIsMissing() =>
        // latest_compatible_version comes back null when no release suits the installed SPT.
        Assert.Equal(ModStatus.NoCompatibleVersion, DependencyStatusResolver.Resolve(null, null));

    [Fact]
    public void Resolve_InstalledEvenWhenNoCompatibleVersionIsPublished() =>
        // Already on disk and nothing newer to move to - not a problem to flag.
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve("1.2.0", null));

    [Theory]
    [InlineData("1.2.0", "1.3.0")]
    [InlineData("3.0.6", "3.0.6")]
    public void Resolve_AFilesMissIsAConflictWhateverSpModSays(string installed, string? required) =>
        Assert.Equal(ModStatus.Conflict, DependencyStatusResolver.Resolve(installed, required, filesMiss: Miss));

    [Fact]
    public void Resolve_AMissingDependencyIsNotInstalledEvenWithAFilesMiss() =>
        Assert.Equal(ModStatus.NotInstalled, DependencyStatusResolver.Resolve(null, "1.3.0", filesMiss: Miss));

    [Fact]
    public void Resolve_DisabledOutranksAFilesMiss() =>
        Assert.Equal(ModStatus.Disabled, DependencyStatusResolver.Resolve("3.0.6", "3.0.6", installedButDisabled: true, filesMiss: Miss));

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
    public void Resolve_AVersionReadOffTheFilesAboveSpModsIsInstalled() =>
        Assert.Equal(ModStatus.Installed,
            DependencyStatusResolver.Resolve("3.0.0.0", "2.4.1", installedVersionFromFiles: true));

    [Fact]
    public void Worst_RanksTooNewAboveAnUpdate() =>
        Assert.Equal(ModStatus.TooNew, DependencyStatusResolver.Worst([ModStatus.UpdateAvailable, ModStatus.TooNew, ModStatus.Installed]));
}
