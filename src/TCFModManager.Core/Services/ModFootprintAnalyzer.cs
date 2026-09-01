using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Reads what an installed mod ships and counts the things that decide how much of the game's
// runtime it is positioned to touch: per-frame methods it declares itself, patch classes, asset
// bundles, a preloader patcher, a server half.
//
// WHAT THIS DELIBERATELY DOES NOT DO. It never loads an assembly, runs anything, or decodes a
// method body. Every count comes from metadata tables, which means it is fast, safe on a mod built
// against a game version this app has never seen, and impossible to mislead into executing mod
// code. It also means it cannot say what a patch targets: an SPT patch names its target inside
// GetTargetMethod's IL, usually through an obfuscated GClass name, and guessing wrong there would
// be worse than saying nothing. "How much does this mod reach into" is answerable from metadata;
// "does this mod cost frame time" is not answerable without running the game.
//
public static class ModFootprintAnalyzer
{
    //
    // Unity's recurring entry points, what each one actually costs, and how many arguments it takes.
    //
    // The kinds are separated because they are not the same claim. Update and LateUpdate run once a
    // frame; FixedUpdate runs on the physics timestep and is decoupled from frame rate entirely;
    // OnGUI runs more than once a frame; OnRenderImage is GPU work and the other two camera
    // callbacks are not. Collapsing them would mean the UI describing a physics method as per-frame
    // work and a CPU callback as GPU work, which is the kind of confident wrongness this feature
    // cannot afford.
    //
    // The parameter count is part of the key rather than a blanket "no arguments" rule: every one
    // of these takes none except OnRenderImage, which takes a source and a destination
    // RenderTexture. Requiring none made the only GPU signal here undetectable.
    //
    private static readonly Dictionary<string, (PerFrameKind Kind, int Parameters)> PerFrameMethods = new(StringComparer.Ordinal)
    {
        ["Update"] = (PerFrameKind.FrameUpdate, 0),
        ["LateUpdate"] = (PerFrameKind.FrameUpdate, 0),

        // NOT once a frame. The physics timestep is 50 times a second by default, so this can run
        // several times in one frame or not at all in another.
        ["FixedUpdate"] = (PerFrameKind.Physics, 0),

        // Immediate-mode GUI: a layout pass plus one call per input event, each frame.
        ["OnGUI"] = (PerFrameKind.Gui, 0),

        // Takes the rendered frame and writes a new one. The only entry point here that is GPU work.
        ["OnRenderImage"] = (PerFrameKind.ImageEffect, 2),

        // CPU callbacks around a camera's render. They are not themselves GPU work, whatever they
        // may set up.
        ["OnPreRender"] = (PerFrameKind.CameraCallback, 0),
        ["OnPostRender"] = (PerFrameKind.CameraCallback, 0),
    };

    // What a recurring engine callback actually is, so the UI can describe it rather than guess.
    private enum PerFrameKind
    {
        FrameUpdate,
        Physics,
        Gui,
        ImageEffect,
        CameraCallback,
    }

    //
    // External base types that mean "this type is a Unity component", so a per-frame method on it
    // is really called every frame. BaseUnityPlugin is BepInEx's plugin base and UIElement is
    // EFT's own - both derive from MonoBehaviour, and because they live in other assemblies the
    // chain walk below stops at them rather than seeing MonoBehaviour itself. Add to this as more
    // bases turn up; a missing name costs a missed flag, never a wrong one.
    //
    private static readonly HashSet<string> UnityComponentBaseNames = new(StringComparer.Ordinal)
    {
        "MonoBehaviour",
        "BaseUnityPlugin",
        "UIElement",
    };

    // SPT's own patch base. A type deriving from it is a patch whether or not it carries a
    // [HarmonyPatch] attribute, and most SPT client mods use it in preference to the attributes.
    private const string ModulePatchBaseName = "ModulePatch";

    private const string HarmonyPatchAttributeName = "HarmonyPatch";

    // Unity asset bundles - the extension SPT mods ship them under.
    private const string BundleExtension = ".bundle";

    //
    // Names get long and repetitive on a mod that ships dozens of components; past this the list
    // has already told the user what they needed and the rest is noise in a JSON file.
    //
    private const int MaxPerFrameMethodsReported = 50;

    //
    // One footprint for everything a single card covers. A card routinely folds several scanned
    // entries together - a client plugin, its preloader patcher, its server half - and they are
    // one mod to the user, so they are one row and one footprint here.
    //
    public static ModFootprint Analyze(IReadOnlyList<InstalledMod> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) throw new ArgumentException("At least one entry is required.", nameof(entries));

        var totals = new Totals();

        foreach (var entry in entries)
        {
            // BepInEx loads the client half, the SPT server loads the other. Which one a file sits
            // in is the only thing that decides where its cost can land, so it is decided here
            // rather than assumed later.
            var side = entry.Target == InstalledModTarget.Server ? totals.Server : totals.Client;
            WalkPath(entry.FolderPath, totals, side);
        }

        return new ModFootprint
        {
            FolderKey = KeyFor(entries[0].FolderPath),
            TotalBytes = totals.TotalBytes,
            FileCount = totals.FileCount,
            AssemblyCount = totals.AssemblyCount,
            UnreadableAssemblyCount = totals.UnreadableAssemblyCount,
            HasPatcher = entries.Any(e => e.IsPatcher),
            HasServerHalf = entries.Any(e => e.Target == InstalledModTarget.Server),
            PatchClassCount = totals.Client.PatchClasses,
            ServerPatchClassCount = totals.Server.PatchClasses,
            BundleCount = totals.Client.BundleCount,
            BundleBytes = totals.Client.BundleBytes,
            ServerBundleCount = totals.Server.BundleCount,
            ServerBundleBytes = totals.Server.BundleBytes,
            PerFrameMethods = Reported(totals.Client.PerFrameMethods),
            PerFrameMethodCount = totals.Client.PerFrameMethods.Distinct(StringComparer.Ordinal).Count(),
            PerFrameTypeCount = totals.Client.PerFrameTypes.Count,
            FrameUpdateTypeCount = totals.Client.FrameUpdateTypes.Count,
            PhysicsTypeCount = totals.Client.PhysicsTypes.Count,
            GuiTypeCount = totals.Client.GuiTypes.Count,
            ImageEffectTypeCount = totals.Client.ImageEffectTypes.Count,
            CameraCallbackTypeCount = totals.Client.CameraCallbackTypes.Count,
            ServerPerFrameTypeCount = totals.Server.PerFrameTypes.Count,
            AnalysedAt = DateTimeOffset.UtcNow,
            Stamp = StampFor(entries),
        };
    }

    private static List<string> Reported(List<string> methods) =>
        [.. methods.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(MaxPerFrameMethodsReported)];

    // The cache key for a mod folder - see ModFootprint.FolderKey for why it is lowercased here.
    public static string KeyFor(string folderPath) => folderPath.ToLowerInvariant();

    //
    // The two vocabularies this analyzer recognises, exposed so they can be asserted directly.
    // Everything below reads assembly metadata, which a test can only exercise by shipping a real
    // DLL built to have the shapes being looked for; these two are where the actual judgement
    // lives, and they are decidable from a string.
    //
    public static bool IsUnityComponentBase(string? baseTypeName) =>
        baseTypeName is not null && UnityComponentBaseNames.Contains(baseTypeName);

    public static bool IsPerFrameMethodName(string methodName) =>
        PerFrameMethods.ContainsKey(methodName);

    //
    // A cheap description of what is on disk right now, compared against a stored footprint's Stamp
    // to decide whether it still holds. File count, total size and newest write time between them
    // catch every way a mod changes in practice - updated, partially replaced, files added or
    // removed by hand - without opening a single assembly.
    //
    public static string StampFor(IReadOnlyList<InstalledMod> entries)
    {
        long bytes = 0;
        var files = 0;
        var newest = 0L;

        foreach (var entry in entries)
        {
            foreach (var file in EnumerateFiles(entry.FolderPath))
            {
                try
                {
                    var info = new FileInfo(file);
                    bytes += info.Length;
                    files++;
                    var written = info.LastWriteTimeUtc.Ticks;
                    if (written > newest) newest = written;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file that can't be stat'd still counts towards the shape of the folder.
                    files++;
                }
            }
        }

        return $"{files}:{bytes}:{newest}";
    }

    //
    // Counted separately for the client and server halves of a mod, because the page says where a
    // cost lands and pooling them would let a server assembly's patch class be labelled as client
    // CPU work. No mod in the sample does that today - every server half read as zero - but nothing
    // in the format stops one, and "no example yet" is not the same as "cannot happen".
    //
    private sealed class Side
    {
        public int PatchClasses;
        public int BundleCount;
        public long BundleBytes;
        public readonly HashSet<string> PerFrameTypes = new(StringComparer.Ordinal);
        public readonly HashSet<string> FrameUpdateTypes = new(StringComparer.Ordinal);
        public readonly HashSet<string> PhysicsTypes = new(StringComparer.Ordinal);
        public readonly HashSet<string> GuiTypes = new(StringComparer.Ordinal);
        public readonly HashSet<string> ImageEffectTypes = new(StringComparer.Ordinal);
        public readonly HashSet<string> CameraCallbackTypes = new(StringComparer.Ordinal);
        public readonly List<string> PerFrameMethods = [];
    }

    private sealed class Totals
    {
        // Whole-mod facts, which the Disk line reports as a total across both halves.
        public long TotalBytes;
        public int FileCount;
        public int AssemblyCount;
        public int UnreadableAssemblyCount;

        public readonly Side Client = new();
        public readonly Side Server = new();
    }

    private static void WalkPath(string path, Totals totals, Side side)
    {
        foreach (var file in EnumerateFiles(path))
        {
            long length;
            try
            {
                length = new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                length = 0;
            }

            totals.FileCount++;
            totals.TotalBytes += length;

            var extension = Path.GetExtension(file);

            if (string.Equals(extension, BundleExtension, StringComparison.OrdinalIgnoreCase))
            {
                side.BundleCount++;
                side.BundleBytes += length;
                continue;
            }

            if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
            {
                ReadAssembly(file, totals, side);
            }
        }
    }

    // Files under a folder, or the single file itself when the scanner recorded a loose DLL.
    private static IEnumerable<string> EnumerateFiles(string path)
    {
        try
        {
            if (File.Exists(path)) return [path];
            if (!Directory.Exists(path)) return [];
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void ReadAssembly(string dllPath, Totals totals, Side side)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);

            // A native DLL shipped alongside a mod is not an assembly that failed to read - it is
            // not an assembly at all, and counting it as unreadable would wrongly push the whole
            // mod to Unknown.
            if (!peReader.HasMetadata) return;

            totals.AssemblyCount++;
            var reader = peReader.GetMetadataReader();

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                ReadType(reader, typeDef, side);
            }
        }
        catch (BadImageFormatException)
        {
            // Same as the HasMetadata case above, just detected later.
        }
        catch (Exception)
        {
            totals.AssemblyCount++;
            totals.UnreadableAssemblyCount++;
        }
    }

    private static void ReadType(MetadataReader reader, TypeDefinition typeDef, Side side)
    {
        var baseName = RootBaseTypeName(reader, typeDef);

        if (baseName == ModulePatchBaseName)
        {
            side.PatchClasses++;
        }
        else if (HasHarmonyPatchAttribute(reader, typeDef))
        {
            // Counted in the same unit as a ModulePatch subclass, and never twice for one type -
            // a ModulePatch that also carries the attribute is one patch class, not two.
            side.PatchClasses++;
        }

        if (!IsUnityComponentBase(baseName)) return;

        var typeName = reader.GetString(typeDef.Name);

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var name = reader.GetString(method.Name);

            if (!PerFrameMethods.TryGetValue(name, out var expected)) continue;

            // Unity's messages are all instance methods with a fixed shape. Anything else that
            // happens to share the name is somebody's own method, not a per-frame callback.
            if (method.Attributes.HasFlag(MethodAttributes.Static)) continue;
            if (ParameterCount(reader, method) != expected.Parameters) continue;

            side.PerFrameMethods.Add($"{typeName}.{name}");

            // The union drives the level, so widening the vocabulary never lets a mod declaring
            // recurring work slip below one declaring less. The per-kind sets exist only so the
            // explanation can be specific: a physics method and a full-screen image effect are not
            // the same claim as an Update, and must not be described as one.
            side.PerFrameTypes.Add(typeName);

            switch (expected.Kind)
            {
                case PerFrameKind.FrameUpdate: side.FrameUpdateTypes.Add(typeName); break;
                case PerFrameKind.Physics: side.PhysicsTypes.Add(typeName); break;
                case PerFrameKind.Gui: side.GuiTypes.Add(typeName); break;
                case PerFrameKind.ImageEffect: side.ImageEffectTypes.Add(typeName); break;
                case PerFrameKind.CameraCallback: side.CameraCallbackTypes.Add(typeName); break;
            }
        }
    }

    //
    // Walks a type's base chain as far as this assembly can see, and returns the name it stops at.
    //
    // Definitions in the same assembly are followed through; a reference into another assembly is
    // where the chain ends, because resolving it would mean finding and opening that assembly -
    // which for an EFT type means the game's own, and this analyzer deliberately reads only what
    // the mod ships. Stopping at the reference is enough: the names worth recognising are all
    // external, and they are listed above.
    //
    private static string? RootBaseTypeName(MetadataReader reader, TypeDefinition typeDef)
    {
        var handle = typeDef.BaseType;

        // Guards against a malformed or circular chain rather than any real inheritance depth.
        for (var depth = 0; depth < 32; depth++)
        {
            if (handle.IsNil) return null;

            switch (handle.Kind)
            {
                case HandleKind.TypeReference:
                    return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name);

                case HandleKind.TypeDefinition:
                    handle = reader.GetTypeDefinition((TypeDefinitionHandle)handle).BaseType;
                    break;

                default:
                    // A generic base (TypeSpecification) or anything unexpected - not one of the
                    // names being looked for, and not worth decoding a signature to confirm.
                    return null;
            }
        }

        return null;
    }

    private static bool HasHarmonyPatchAttribute(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var attributeHandle in typeDef.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (AttributeTypeName(reader, attribute) == HarmonyPatchAttributeName) return true;
        }

        return false;
    }

    //
    // The attribute's type name. Handles both shapes: a MemberReference when the attribute comes
    // from another assembly (the normal case, HarmonyLib), and a MethodDefinition when it was
    // merged into the mod's own DLL - which several SPT mods do, and which would otherwise read as
    // no attribute at all.
    //
    private static string? AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (memberRef.Parent.Kind != HandleKind.TypeReference) return null;
                return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent).Name);

            case HandleKind.MethodDefinition:
                var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                return reader.GetString(reader.GetTypeDefinition(methodDef.GetDeclaringType()).Name);

            default:
                return null;
        }
    }

    //
    // Parameter count straight off the signature blob (ECMA-335 II.23.2.1): a calling-convention
    // header, an optional generic parameter count, then the parameter count. Cheaper than decoding
    // the whole signature, which would need a type provider to describe types nothing here reads.
    //
    private static int ParameterCount(MetadataReader reader, MethodDefinition method)
    {
        try
        {
            var blob = reader.GetBlobReader(method.Signature);
            var header = blob.ReadSignatureHeader();
            if (header.IsGeneric) blob.ReadCompressedInteger();
            return blob.ReadCompressedInteger();
        }
        catch (BadImageFormatException)
        {
            // Treated as "not a per-frame method" - a malformed signature is not evidence of one.
            return -1;
        }
    }
}
