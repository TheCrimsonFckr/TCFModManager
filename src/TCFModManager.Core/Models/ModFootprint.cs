using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

//
// One reason a mod's footprint reads the way it does. Kept as flags rather than sentences because
// Core does not own user-facing prose - the App turns each flag into the wording it shows.
//
[Flags]
public enum ModFootprintSignal
{
    None = 0,

    // The mod ships at least one type that runs code every frame of its own accord.
    PerFrameCode = 1,

    // Per-frame code spread across enough separate components to be a system rather than a widget.
    ManyPerFrameCode = 2,

    // Some patch classes - present but modest.
    SomePatches = 4,

    // Enough patch classes that the mod is reaching into a lot of the game, whatever it patches.
    ManyPatches = 8,

    // Patching on the scale of a total overhaul.
    ExtensivePatches = 16,

    // Asset bundles large enough to matter for memory rather than frame time.
    LargeBundles = 32,

    // Ships a BepInEx patcher, which runs in the preloader before the game's assemblies load.
    Patcher = 64,

    // Ships a server half under user\mods as well as a client half.
    ServerHalf = 128,

    // At least one assembly could not be read, so the patch and per-frame counts are a floor.
    Unreadable = 256,
}

//
// How much of the game's runtime a mod is POSITIONED to touch, judged from what it ships on disk.
//
// This is a static reading, never a measurement: a mod flagged Heavy has more opportunity to cost
// frame time than one flagged Light, and nothing here says it actually does. The App's wording has
// to keep that distinction - see ModFootprintAnalyzer for what is and isn't knowable this way.
//
public enum ModFootprintLevel
{
    // Nothing could be read - every assembly failed, or the mod ships none.
    Unknown,
    Light,
    Moderate,
    Heavy,
}

//
// What one installed mod ships, as counted by ModFootprintAnalyzer. Everything on it is a fact
// about files on disk; Level and Signals are the only derived values and both are pure functions
// of the rest, so they are recomputed rather than stored and can never disagree with the counts.
//
public sealed record ModFootprint
{
    // Bundles below this don't move the needle; above it they are worth naming for memory.
    public const long LargeBundleBytes = 50L * 1024 * 1024;

    //
    // Patch-class counts at which a mod reads as touching some of the game, a lot of it, and
    // essentially all of it. Calibrated against real mods rather than picked: the mods everyone
    // already calls heavy sit around 100-130 patch classes, the capable-but-ordinary ones around
    // 14-20, and a single-purpose tweak at 1-7. The gap between those bands is where these sit.
    //
    public const int ExtensivePatchesThreshold = 60;
    public const int ManyPatchesThreshold = 25;
    public const int SomePatchesThreshold = 10;

    // Separate components running per-frame code, above which the mod is a system rather than a
    // widget - a crosshair has one, a co-op layer has twenty.
    public const int ManyPerFrameTypesThreshold = 5;

    // Score at or above which the level steps up.
    public const int HeavyScore = 4;
    public const int ModerateScore = 2;

    //
    // The mod folder this describes, lowercased - the cache key. Lowercased on the way in rather
    // than compared case-insensitively later, so the JSON file has one entry per folder however
    // the path was spelled when it was analysed.
    //
    public required string FolderKey { get; init; }

    public long TotalBytes { get; init; }
    public int FileCount { get; init; }

    // Managed assemblies found, and how many of those could not be read at all.
    public int AssemblyCount { get; init; }
    public int UnreadableAssemblyCount { get; init; }

    public int BundleCount { get; init; }
    public long BundleBytes { get; init; }

    public bool HasPatcher { get; init; }
    public bool HasServerHalf { get; init; }

    // Types carrying at least one [HarmonyPatch] attribute, and types deriving from SPT's
    // ModulePatch. Counted per type rather than per attribute: a patch class routinely stacks
    // several attributes to name one target, and counting those would say more about how the
    // author writes attributes than about how much the mod patches.
    public int HarmonyPatchClassCount { get; init; }
    public int ModulePatchClassCount { get; init; }

    //
    // "Type.Method" for every per-frame method the mod declares on a type that reaches
    // MonoBehaviour. Kept as names rather than a count because the names are the useful part -
    // "HudUpdater.Update" tells the user where to look, "3" does not.
    //
    public IReadOnlyList<string> PerFrameMethods { get; init; } = [];

    //
    // How many distinct types those methods came from. Stored rather than derived from the list
    // above because the list is truncated for the benefit of the file, and the count must not be.
    //
    // THIS COUNTS WHAT A MOD DECLARES, NOT WHAT RUNS. A type with an Update only runs one if an
    // instance of it is created, attached to an active object and enabled - all runtime facts, none
    // of them visible in the files. Any wording built on this has to say "declares".
    //
    public int PerFrameTypeCount { get; init; }

    //
    // How that total breaks down by what the callback actually is. All of these are already inside
    // PerFrameTypeCount - they do not add to it, they explain it - and a type can appear in more
    // than one, because a component may declare both an Update and an OnGUI.
    //
    // They exist because the kinds are different claims and must not be described alike:
    // FrameUpdate really is once a frame; Physics runs on the physics timestep and is decoupled
    // from frame rate; Gui runs more than once a frame; ImageEffect is GPU work; CameraCallback is
    // CPU work around a camera's render.
    //
    public int FrameUpdateTypeCount { get; init; }
    public int PhysicsTypeCount { get; init; }
    public int GuiTypeCount { get; init; }
    public int ImageEffectTypeCount { get; init; }
    public int CameraCallbackTypeCount { get; init; }

    public DateTimeOffset AnalysedAt { get; init; }

    //
    // A cheap content stamp - file count, total bytes and newest write time. Compared against a
    // fresh stamp of the folder to decide whether this entry still describes what is on disk,
    // which costs a directory walk rather than a full re-read of every assembly.
    //
    public string Stamp { get; init; } = "";

    [JsonIgnore]
    public int PatchClassCount => HarmonyPatchClassCount + ModulePatchClassCount;

    [JsonIgnore]
    public bool HasPerFrameCode => PerFrameMethods.Count > 0;

    [JsonIgnore]
    public ModFootprintSignal Signals
    {
        get
        {
            var signals = ModFootprintSignal.None;

            if (HasPerFrameCode) signals |= ModFootprintSignal.PerFrameCode;
            if (PerFrameTypeCount >= ManyPerFrameTypesThreshold) signals |= ModFootprintSignal.ManyPerFrameCode;

            if (PatchClassCount >= ExtensivePatchesThreshold) signals |= ModFootprintSignal.ExtensivePatches;
            else if (PatchClassCount >= ManyPatchesThreshold) signals |= ModFootprintSignal.ManyPatches;
            else if (PatchClassCount >= SomePatchesThreshold) signals |= ModFootprintSignal.SomePatches;

            if (BundleBytes >= LargeBundleBytes) signals |= ModFootprintSignal.LargeBundles;
            if (HasPatcher) signals |= ModFootprintSignal.Patcher;
            if (HasServerHalf) signals |= ModFootprintSignal.ServerHalf;
            if (UnreadableAssemblyCount > 0) signals |= ModFootprintSignal.Unreadable;

            return signals;
        }
    }

    //
    // Weighted so that how much a mod patches dominates, per-frame breadth comes second, and the
    // incidental signals can only ever nudge: a mod can ship a patcher, a server half and big
    // bundles and still never run a line of code in a frame, and that mod is not the one to look
    // at first. A server half scores nothing at all - it does not run in the client's frame loop.
    //
    [JsonIgnore]
    public int Score
    {
        get
        {
            var score = 0;

            if (PerFrameTypeCount >= ManyPerFrameTypesThreshold) score += 2;
            else if (HasPerFrameCode) score += 1;

            if (PatchClassCount >= ExtensivePatchesThreshold) score += 3;
            else if (PatchClassCount >= ManyPatchesThreshold) score += 2;
            else if (PatchClassCount >= SomePatchesThreshold) score += 1;

            if (BundleBytes >= LargeBundleBytes) score += 1;
            if (HasPatcher) score += 1;

            return score;
        }
    }

    //
    // Unknown only when there was something to read and none of it could be read. A mod that ships
    // no assemblies at all - a server mod written in JavaScript, a bundle-only replacement pack -
    // is not unknown, it genuinely has no client code, and saying "Light" about it is correct.
    //
    [JsonIgnore]
    public ModFootprintLevel Level
    {
        get
        {
            if (AssemblyCount > 0 && UnreadableAssemblyCount == AssemblyCount) return ModFootprintLevel.Unknown;
            if (Score >= HeavyScore) return ModFootprintLevel.Heavy;
            if (Score >= ModerateScore) return ModFootprintLevel.Moderate;
            return ModFootprintLevel.Light;
        }
    }
}
