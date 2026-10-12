using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-13: is the installed version of a mod made for the installed SPT. Versions as CommonLib's
// were on sp-mod 2026-10-12.
public class InstalledSptFitTests
{
    private static readonly List<ModVersionSummary> CommonLib =
    [
        new() { Version = "3.0.6", SptVersionConstraint = "~4.1.3", FikaCompatibility = "compatible" },
        new() { Version = "3.0.5", SptVersionConstraint = "~4.1.0" },
        new() { Version = "2.0.24", SptVersionConstraint = "~4.0.13" },
        new() { Version = "2.0.22", SptVersionConstraint = "~4.0.13" },
    ];

    [Fact]
    public void AnOlderLineOnANewerSpt_IsNotForIt_AndANewerReleaseIs()
    {
        var fit = InstalledSptFit.Judge("2.0.24", "4.1.6", CommonLib, null, null)!;

        Assert.False(fit.Fits);
        Assert.Equal(SptFitSource.Catalog, fit.Source);
        Assert.Equal("~4.0.13", fit.ForConstraint);
        Assert.Equal("3.0.6", fit.FittingVersion);
        Assert.True(fit.FittingIsNewer);
    }

    [Fact]
    public void ANewerLineOnAnOlderSpt_OnlyFitsAnOlderRelease()
    {
        var fit = InstalledSptFit.Judge("3.0.6", "4.0.13", CommonLib, true, null)!;

        Assert.False(fit.Fits);
        Assert.Equal(SptFitSource.SpMod, fit.Source);
        Assert.Equal("2.0.24", fit.FittingVersion);
        Assert.False(fit.FittingIsNewer);
    }

    [Fact]
    public void SpModsExactPatchReading_YieldsToTheReleasesOwnRange()
    {
        // BossNotifier 1.2.1 lists a bare "4.1.3"; sp-mod calls it not for 4.1.6, Browse calls it fine.
        List<ModVersionSummary> boss = [new() { Version = "1.2.1", SptVersionConstraint = "4.1.3" }];

        var fit = InstalledSptFit.Judge("1.2.1", "4.1.6", boss, true, null)!;

        Assert.True(fit.Fits);
        Assert.Equal(SptFitSource.Catalog, fit.Source);
    }

    [Fact]
    public void NothingPublishedForThisSpt_HasNoFittingVersion()
    {
        var fit = InstalledSptFit.Judge("3.0.6", "3.11.4", CommonLib, true, null)!;

        Assert.False(fit.Fits);
        Assert.Null(fit.FittingVersion);
    }

    [Fact]
    public void SpModWins_AndTheDllDisagreeingIsKept()
    {
        var fit = InstalledSptFit.Judge("3.0.6", "4.1.6", CommonLib, null, "~4.0.0")!;

        Assert.True(fit.Fits);
        Assert.True(fit.DeclaredDisagrees);
    }

    [Fact]
    public void OnlyTheDllAnswers_ReadAsAnSptRange()
    {
        Assert.True(InstalledSptFit.Judge("1.0.0", "4.0.13", null, null, "~4.0.0")!.Fits);
        Assert.False(InstalledSptFit.Judge("1.0.0", "4.1.6", null, null, "~4.0.0")!.Fits);
    }

    [Fact]
    public void NobodyAnswers_SaysNothing()
    {
        Assert.Null(InstalledSptFit.Judge("9.9.9", "4.1.6", CommonLib, null, null));
        Assert.Null(InstalledSptFit.Judge("3.0.6", null, CommonLib, null, null));
    }

    [Fact]
    public void SpModUpToDateCountsWhenTheCatalogHasNoRelease()
    {
        var fit = InstalledSptFit.Judge("1.17.0", "4.1.6", [], false, null)!;
        Assert.True(fit.Fits);
        Assert.Equal(SptFitSource.SpMod, fit.Source);
    }

    [Fact]
    public void TheInstalledVersionMatchesOnItsNumbers()
    {
        Assert.Equal("2.0.24", InstalledSptFit.Release("2.0.24.0", CommonLib)?.Version);
    }
}
