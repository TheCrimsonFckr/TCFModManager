using TCFModManager.Core.Models;
using Xunit;

namespace TCFModManager.Core.Tests;

// Covers the derived half of ModFootprint - Signals, Score and Level - which is the only place the
// feature makes a judgement, and is a pure function of the counts the analyzer writes.
public class ModFootprintTests
{
    private static ModFootprint Footprint(
        int harmonyPatches = 0,
        int modulePatches = 0,
        int perFrameTypes = 0,
        long bundleBytes = 0,
        bool patcher = false,
        bool serverHalf = false,
        int assemblies = 1,
        int unreadable = 0) =>
        new()
        {
            FolderKey = @"c:\spt\bepinex\plugins\example",
            HarmonyPatchClassCount = harmonyPatches,
            ModulePatchClassCount = modulePatches,
            PerFrameTypeCount = perFrameTypes,
            PerFrameMethods = Enumerable.Range(0, perFrameTypes).Select(i => $"Type{i}.Update").ToList(),
            BundleBytes = bundleBytes,
            BundleCount = bundleBytes > 0 ? 1 : 0,
            HasPatcher = patcher,
            HasServerHalf = serverHalf,
            AssemblyCount = assemblies,
            UnreadableAssemblyCount = unreadable,
        };

    [Fact]
    public void PlainModIsLightWithNoSignals()
    {
        var footprint = Footprint();

        Assert.Equal(ModFootprintSignal.None, footprint.Signals);
        Assert.Equal(0, footprint.Score);
        Assert.Equal(ModFootprintLevel.Light, footprint.Level);
    }

    [Fact]
    public void OnePerFrameComponentAloneStaysLight()
    {
        // Nearly every client mod has an Update somewhere. If one component were enough to move a
        // mod off Light the level would say nothing about any of them.
        var footprint = Footprint(perFrameTypes: 1);

        Assert.True(footprint.HasPerFrameCode);
        Assert.True(footprint.Signals.HasFlag(ModFootprintSignal.PerFrameCode));
        Assert.False(footprint.Signals.HasFlag(ModFootprintSignal.ManyPerFrameCode));
        Assert.Equal(1, footprint.Score);
        Assert.Equal(ModFootprintLevel.Light, footprint.Level);
    }

    [Fact]
    public void PerFrameCodeSpreadAcrossComponentsCountsForMore()
    {
        var footprint = Footprint(perFrameTypes: ModFootprint.ManyPerFrameTypesThreshold);

        Assert.True(footprint.Signals.HasFlag(ModFootprintSignal.ManyPerFrameCode));
        Assert.Equal(2, footprint.Score);
        Assert.Equal(ModFootprintLevel.Moderate, footprint.Level);
    }

    [Fact]
    public void PatchCountSpansBothPatchStyles()
    {
        var footprint = Footprint(harmonyPatches: 4, modulePatches: 7);

        Assert.Equal(11, footprint.PatchClassCount);
        Assert.True(footprint.Signals.HasFlag(ModFootprintSignal.SomePatches));
    }

    [Theory]
    [InlineData(ModFootprint.SomePatchesThreshold, ModFootprintSignal.SomePatches, 1)]
    [InlineData(ModFootprint.ManyPatchesThreshold, ModFootprintSignal.ManyPatches, 2)]
    [InlineData(ModFootprint.ExtensivePatchesThreshold, ModFootprintSignal.ExtensivePatches, 3)]
    public void EachPatchBandScoresOnceAndOnlyItsOwnSignal(int patches, ModFootprintSignal expected, int score)
    {
        var footprint = Footprint(modulePatches: patches);
        var patchSignals = footprint.Signals
            & (ModFootprintSignal.SomePatches | ModFootprintSignal.ManyPatches | ModFootprintSignal.ExtensivePatches);

        Assert.Equal(expected, patchSignals);
        Assert.Equal(score, footprint.Score);
    }

    [Fact]
    public void JustBelowSomePatchesScoresNothing()
    {
        var footprint = Footprint(modulePatches: ModFootprint.SomePatchesThreshold - 1);

        Assert.Equal(ModFootprintSignal.None, footprint.Signals);
    }

    //
    // The shapes below are the real counts this analyzer produced for these mods, and they are the
    // whole justification for where the thresholds sit. Anyone retuning a threshold has to keep
    // this ordering intact: the two mods the community already calls heavy come out Heavy, the
    // capable-but-ordinary ones Moderate, and the single-purpose tweaks Light.
    //
    [Theory]
    [InlineData("Project Fika", 129, 19, ModFootprintLevel.Heavy)]
    [InlineData("SAIN", 106, 3, ModFootprintLevel.Heavy)]
    [InlineData("Dynamic Maps", 14, 7, ModFootprintLevel.Moderate)]
    [InlineData("Weapon Customizer", 20, 1, ModFootprintLevel.Moderate)]
    [InlineData("Amands's Graphics", 17, 3, ModFootprintLevel.Moderate)]
    [InlineData("Simple Crosshair", 7, 1, ModFootprintLevel.Light)]
    [InlineData("Arma Zoom", 3, 1, ModFootprintLevel.Light)]
    [InlineData("CineKit", 0, 1, ModFootprintLevel.Light)]
    [InlineData("Disable Headshot Protect", 1, 0, ModFootprintLevel.Light)]
    public void RealModShapesLandWhereTheyShould(string mod, int patches, int perFrameTypes, ModFootprintLevel expected)
    {
        var footprint = Footprint(modulePatches: patches, perFrameTypes: perFrameTypes);

        Assert.True(
            footprint.Level == expected,
            $"{mod} ({patches} patch classes, {perFrameTypes} per-frame components) came out {footprint.Level}, expected {expected}");
    }

    [Fact]
    public void LargeBundlesAndPatcherTogetherReachModerate()
    {
        var footprint = Footprint(bundleBytes: ModFootprint.LargeBundleBytes, patcher: true);

        Assert.True(footprint.Signals.HasFlag(ModFootprintSignal.LargeBundles));
        Assert.True(footprint.Signals.HasFlag(ModFootprintSignal.Patcher));
        Assert.Equal(ModFootprintLevel.Moderate, footprint.Level);
    }

    [Fact]
    public void SmallBundlesRaiseNoSignal()
    {
        var footprint = Footprint(bundleBytes: ModFootprint.LargeBundleBytes - 1);

        Assert.False(footprint.Signals.HasFlag(ModFootprintSignal.LargeBundles));
    }

    [Fact]
    public void ServerHalfIsReportedButNeverScored()
    {
        // Worth telling the user about - it is where load time and RAM go - but a server mod does
        // not run in the client's frame loop, so it must never push a mod up the list.
        var footprint = Footprint(serverHalf: true);

        Assert.True(footprint.Signals.HasFlag(ModFootprintSignal.ServerHalf));
        Assert.Equal(0, footprint.Score);
        Assert.Equal(ModFootprintLevel.Light, footprint.Level);
    }

    [Fact]
    public void EveryAssemblyUnreadableIsUnknown()
    {
        var footprint = Footprint(assemblies: 2, unreadable: 2);

        Assert.Equal(ModFootprintLevel.Unknown, footprint.Level);
    }

    [Fact]
    public void UnknownWinsOverWhateverWasCounted()
    {
        // Counts from a partial read must not be presented as an answer when nothing was readable.
        var footprint = Footprint(modulePatches: 80, perFrameTypes: 9, assemblies: 1, unreadable: 1);

        Assert.Equal(ModFootprintLevel.Unknown, footprint.Level);
    }

    [Fact]
    public void SomeAssembliesUnreadableStillProducesALevel()
    {
        var footprint = Footprint(modulePatches: 30, perFrameTypes: 1, assemblies: 3, unreadable: 1);

        Assert.True(footprint.Signals.HasFlag(ModFootprintSignal.Unreadable));
        Assert.Equal(ModFootprintLevel.Moderate, footprint.Level);
    }

    [Fact]
    public void ModWithNoAssembliesIsLightRatherThanUnknown()
    {
        // A JavaScript server mod or a bundle-only replacement pack ships no managed code at all.
        // That is a real answer - it has no client code - not a failure to read one.
        var footprint = Footprint(assemblies: 0, unreadable: 0, serverHalf: true);

        Assert.Equal(ModFootprintLevel.Light, footprint.Level);
    }
}
