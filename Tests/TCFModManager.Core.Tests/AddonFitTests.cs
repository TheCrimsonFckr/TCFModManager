using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class AddonFitTests
{
    [Fact]
    public void AParentRequirementThatFits_Fits() =>
        Assert.True(AddonFit.Check("^2.0.0", "2.1.1", "4.1.6"));

    [Fact]
    public void AParentRequirementThatDoesntFit_DoesntFit() =>
        Assert.False(AddonFit.Check("^3.0.0", "2.1.1", "4.1.6"));

    [Fact]
    public void AnSptRangeInTheParentBox_Fits_AsSpModReadsIt()
    {
        // ORBIT - Fika Addon 1.1.0, live on sp-mod 2026-10-17: needs ORBIT "~4.1.6", ORBIT is 2.1.1,
        // and sp-mod's page lists it as compatible with ORBIT 2.1.0 and 2.1.1.
        Assert.True(AddonFit.Check("~4.1.6", "2.1.1", "4.1.6"));
    }

    [Fact]
    public void WithNoSptVersion_TheRequirementIsReadAsWritten() =>
        Assert.False(AddonFit.Check("~4.1.6", "2.1.1", null));

    [Fact]
    public void AnUnreadableRequirement_StaysUnreadable() =>
        Assert.Null(AddonFit.Check("not a range", "2.1.1", "4.1.6"));

    [Fact]
    public void Accepts_TakesAFitAndAnSptRangeInTheParentBox_ButNotAnUnreadableRequirement()
    {
        Assert.True(AddonFit.Accepts("^2.0.0", "2.1.1", "4.1.6"));
        Assert.True(AddonFit.Accepts("~4.1.6", "2.1.1", "4.1.6"));
        Assert.False(AddonFit.Accepts("not a range", "2.1.1", "4.1.6"));
        Assert.False(AddonFit.Accepts(null, "2.1.1", "4.1.6"));
        Assert.False(AddonFit.Accepts("^3.0.0", "2.1.1", "4.1.6"));
    }

    [Fact]
    public void NewestForParent_TakesAVersionWithAnSptRangeInTheParentBox()
    {
        AddonVersion[] versions =
        [
            new() { Id = 302, Version = "1.1.0", ModVersionConstraint = "~4.1.6", PublishedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) },
            new() { Id = 259, Version = "1.0.0", ModVersionConstraint = "~2.0", PublishedAt = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero) },
        ];

        Assert.Equal("1.1.0", NewestFittingVersion.ForParent(versions, "2.1.1", "4.1.6")?.Version);
        Assert.Equal("1.0.0", NewestFittingVersion.ForParent(versions, "2.0.4")?.Version);
        Assert.Null(NewestFittingVersion.ForParent(versions, "2.1.1"));
    }
}
