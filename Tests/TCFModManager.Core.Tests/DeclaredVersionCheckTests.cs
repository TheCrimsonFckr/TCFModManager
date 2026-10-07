using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-23 S0: a dependent's own declared range against the dependency's declared version.
public class DeclaredVersionCheckTests
{
    private static InstalledMod Mod(
        string name, string guid, string? version, InstalledModTarget target = InstalledModTarget.Server,
        bool disabled = false, params ModDependencyRef[] dependencies) =>
        new()
        {
            Name = name,
            Guid = guid,
            Version = version,
            Target = target,
            FolderPath = Path.Combine("C:", "SPT", name),
            IsDisabled = disabled,
            Dependencies = dependencies,
        };

    private static readonly InstalledMod CommonLib306 = Mod("WTT-ServerCommonLib", "com.wtt.commonlib", "3.0.6");

    private static InstalledMod Dependent(string range, InstalledModTarget target = InstalledModTarget.Server, bool soft = false, bool disabled = false) =>
        Mod("Eco", "com.wtt.ecoattachmentemporium", "3.0.0", target, disabled, new ModDependencyRef("com.wtt.commonlib", soft, range));

    [Fact]
    public void FindMiss_ReportsARangeTheInstalledVersionFails()
    {
        var miss = DeclaredVersionCheck.FindMiss([Dependent("~3.0.3")], [CommonLib306]);

        Assert.Null(miss);

        miss = DeclaredVersionCheck.FindMiss([Dependent("3.0.3")], [CommonLib306]);
        Assert.NotNull(miss);
        Assert.Equal("3.0.3", miss!.Range);
        Assert.Equal("3.0.6", miss.Found);
    }

    [Theory]
    [InlineData("^3.0.0")]
    [InlineData(">=3.0.0")]
    [InlineData("~3.0.0")]
    public void FindMiss_NullWhenTheRangeIsMet(string range) =>
        Assert.Null(DeclaredVersionCheck.FindMiss([Dependent(range)], [CommonLib306]));

    [Fact]
    public void FindMiss_ACaretRangeStopsAtTheNextMajor() =>
        // WTT-ContentBackport 4.0.x declares ^2.0.22 - CommonLib 3.x is refused.
        Assert.NotNull(DeclaredVersionCheck.FindMiss([Dependent("^2.0.22")], [CommonLib306]));

    [Fact]
    public void FindMiss_OnlyComparesTheSameSide() =>
        // A client [BepInDependency] is met by a plugin, never by the server half.
        Assert.Null(DeclaredVersionCheck.FindMiss([Dependent(">=4.0.0", InstalledModTarget.Client)], [CommonLib306]));

    [Fact]
    public void FindMiss_IgnoresSoftDependencies() =>
        Assert.Null(DeclaredVersionCheck.FindMiss([Dependent("3.0.3", soft: true)], [CommonLib306]));

    [Fact]
    public void FindMiss_IgnoresADisabledDependent() =>
        Assert.Null(DeclaredVersionCheck.FindMiss([Dependent("3.0.3", disabled: true)], [CommonLib306]));

    [Fact]
    public void FindMiss_NullWhenNoRangeIsDeclared() =>
        Assert.Null(DeclaredVersionCheck.FindMiss(
            [Mod("Eco", "eco", "1.0.0", dependencies: new ModDependencyRef("com.wtt.commonlib", false))], [CommonLib306]));

    [Fact]
    public void FindMiss_NullWhenTheDependencyVersionIsUnreadable() =>
        Assert.Null(DeclaredVersionCheck.FindMiss([Dependent("3.0.3")], [Mod("WTT-ServerCommonLib", "com.wtt.commonlib", null)]));

    [Fact]
    public void FindMiss_MatchesAnyGuidTheDependencyRegisters()
    {
        var library = new InstalledMod
        {
            Name = "Toolkit",
            Guid = "com.author.toolkit",
            Guids = ["com.author.toolkit", "com.author.toolkit.api"],
            Version = "1.0.0",
            Target = InstalledModTarget.Client,
            FolderPath = "C:/SPT/BepInEx/plugins/Toolkit",
        };
        var consumer = Mod("Consumer", "c", "1.0.0", InstalledModTarget.Client,
            dependencies: new ModDependencyRef("com.author.toolkit.api", false, ">=2.0.0"));

        Assert.NotNull(DeclaredVersionCheck.FindMiss([consumer], [library]));
    }
}
