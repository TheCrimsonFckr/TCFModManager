using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class ModAssemblyMetadataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tcfmm-metadata-" + Guid.NewGuid().ToString("N"));

    public ModAssemblyMetadataTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // Stand-ins for the SPT and SemanticVersioning types, with the real namespaces and constructor
    // shapes, so the compiler emits the same IL a real mod's initialisers produce.
    private const string SptStubs = """
        namespace SemanticVersioning
        {
            public class Version
            {
                public Version(string input, bool loose = false) { }
                public Version(int major, int minor, int patch, string preRelease = null, string build = null) { }
            }

            public class Range
            {
                public Range(string rangeSpec, bool loose = false) { }
            }
        }

        namespace SPTarkov.Server.Core.Models.Spt.Mod
        {
            using System.Collections.Generic;
            using Version = SemanticVersioning.Version;
            using Range = SemanticVersioning.Range;

            public abstract record AbstractModMetadata
            {
                public abstract string ModGuid { get; init; }
                public abstract string Name { get; init; }
                public abstract string Author { get; init; }
                public abstract List<string>? Contributors { get; init; }
                public abstract Version Version { get; init; }
                public abstract Range SptVersion { get; init; }
                public abstract List<string>? Incompatibilities { get; init; }
                public abstract Dictionary<string, Range>? ModDependencies { get; init; }
                public abstract string? Url { get; init; }
                public abstract bool? IsBundleMod { get; init; }
                public abstract string License { get; init; }
            }

            public interface IModMetadata
            {
                string ModGuid { get; }
                string Name { get; }
                string Author { get; }
                Version Version { get; }
                Range SptVersion { get; }
                Dictionary<string, Range>? ModDependencies { get; }
            }
        }
        """;

    private const string BepInExStubs = """
        namespace BepInEx
        {
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public class BepInPlugin : Attribute
            {
                public BepInPlugin(string GUID, string Name, string Version) { }
            }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
            public class BepInDependency : Attribute
            {
                [Flags] public enum DependencyFlags { HardDependency = 1, SoftDependency = 2 }
                public BepInDependency(string DependencyGUID, DependencyFlags Flags = DependencyFlags.HardDependency) { }
                public BepInDependency(string DependencyGUID, string MinimumDependencyVersion) { }
            }
        }
        """;

    //
    // The stubs go in an assembly of their own and the fixture references it, as a real mod
    // references SPT - so the metadata type's base type and the Version/Range constructors are
    // TypeReferences in the fixture, exactly as in a shipped mod.
    //
    private string Compile(string source, string stubs = SptStubs)
    {
        var framework = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "System.Private.CoreLib.dll"
                or "System.Collections.dll" or "netstandard.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var stubPath = Emit("Stubs", stubs, framework);
        return Emit("Fixture", source, [.. framework, MetadataReference.CreateFromFile(stubPath)]);
    }

    private string Emit(string prefix, string source, IEnumerable<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(
            prefix + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var path = Path.Combine(_dir, compilation.AssemblyName + ".dll");
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModMetadata", name);

    [Fact]
    public void ReadServer_Spt40Record_WithStringVersion()
    {
        var dll = Compile("""
            using System.Collections.Generic;
            using SPTarkov.Server.Core.Models.Spt.Mod;
            using Version = SemanticVersioning.Version;
            using Range = SemanticVersioning.Range;

            public record ModMetadata : AbstractModMetadata
            {
                public override string ModGuid { get; init; } = "com.example.mod";
                public override string Name { get; init; } = "Example Mod";
                public override string Author { get; init; } = "Someone";
                public override List<string>? Contributors { get; init; } = null;
                public override Version Version { get; init; } = new("1.4.2");
                public override Range SptVersion { get; init; } = new("~4.0.0");
                public override List<string>? Incompatibilities { get; init; } = null;
                public override Dictionary<string, Range>? ModDependencies { get; init; } = null;
                public override string? Url { get; init; } = null;
                public override bool? IsBundleMod { get; init; } = false;
                public override string License { get; init; } = "MIT";
            }
            """);

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.NotNull(metadata);
        Assert.Equal("com.example.mod", metadata.Guid);
        Assert.Equal("Example Mod", metadata.Name);
        Assert.Equal("Someone", metadata.Author);
        Assert.Equal("1.4.2", metadata.Version);
        Assert.Equal("~4.0.0", metadata.SptVersion);
        Assert.Empty(metadata.Dependencies);
    }

    // The shape WTT - Black Division 1.1.0 ships: the version built from three integers, which a
    // search of the DLL's strings cannot find, and a dependency dictionary of GUID -> range.
    [Fact]
    public void ReadServer_IntegerVersion_AndDependencyDictionary()
    {
        var dll = Compile("""
            using System.Collections.Generic;
            using SPTarkov.Server.Core.Models.Spt.Mod;
            using Version = SemanticVersioning.Version;
            using Range = SemanticVersioning.Range;

            public record ModMetadata : AbstractModMetadata
            {
                public override string ModGuid { get; init; } = "com.blackdiv.tacticaltoaster";
                public override string Name { get; init; } = "Black Division";
                public override string Author { get; init; } = "TacticalToaster";
                public override List<string>? Contributors { get; init; }
                public override Version Version { get; init; } = new(1, 1, 0);
                public override Range SptVersion { get; init; } = new("~4.0.0");
                public override List<string>? Incompatibilities { get; init; }
                public override Dictionary<string, Range>? ModDependencies { get; init; } = new()
                {
                    { "com.morebotsapi.tacticaltoaster", new Range(">=2.0.0") },
                    { "com.wtt.commonlib", new Range(">=2.0.0") },
                    { "com.wtt.contentbackport", new Range(">=1.0.0") },
                };
                public override string? Url { get; init; }
                public override bool? IsBundleMod { get; init; }
                public override string License { get; init; } = "MIT";
            }
            """);

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.NotNull(metadata);
        Assert.Equal("com.blackdiv.tacticaltoaster", metadata.Guid);
        Assert.Equal("1.1.0", metadata.Version);
        Assert.Equal(
            [
                new ModDependencyRef("com.morebotsapi.tacticaltoaster", false, ">=2.0.0"),
                new ModDependencyRef("com.wtt.commonlib", false, ">=2.0.0"),
                new ModDependencyRef("com.wtt.contentbackport", false, ">=1.0.0"),
            ],
            metadata.Dependencies);
    }

    [Fact]
    public void ReadServer_IntegerVersion_WithPreRelease()
    {
        var dll = Compile(Spt41Record("""public Version Version { get; init; } = new(2, 0, 0, "beta.3");"""));

        Assert.Equal("2.0.0-beta.3", ModAssemblyMetadata.ReadServer(dll)?.Version);
    }

    [Fact]
    public void ReadServer_DependencyDictionary_IndexerInitialiser()
    {
        var dll = Compile(Spt41Record(
            """public Version Version { get; init; } = new("1.0.0");""",
            """
            public Dictionary<string, Range>? ModDependencies { get; init; } = new()
            {
                ["com.wtt.commonlib"] = new Range("~2.0.0"),
            };
            """));

        Assert.Equal(
            [new ModDependencyRef("com.wtt.commonlib", false, "~2.0.0")],
            ModAssemblyMetadata.ReadServer(dll)?.Dependencies);
    }

    [Fact]
    public void ReadServer_Spt41Interface()
    {
        var dll = Compile(Spt41Record("""public Version Version { get; init; } = new("0.3.0");"""));

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.NotNull(metadata);
        Assert.Equal("com.example.spt41", metadata.Guid);
        Assert.Equal("0.3.0", metadata.Version);
        Assert.Equal(">=4.1.0 <4.2.0", metadata.SptVersion);
    }

    [Fact]
    public void ReadServer_ExpressionBodiedProperties()
    {
        var dll = Compile("""
            using System.Collections.Generic;
            using SPTarkov.Server.Core.Models.Spt.Mod;
            using Version = SemanticVersioning.Version;
            using Range = SemanticVersioning.Range;

            public sealed record ModMetadata : IModMetadata
            {
                public string ModGuid => "com.example.getters";
                public string Name => "Getters";
                public string Author => "Someone";
                public Version Version => new(3, 2, 1);
                public Range SptVersion => new("~4.1.0");
                public Dictionary<string, Range>? ModDependencies => null;
            }
            """);

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.NotNull(metadata);
        Assert.Equal("com.example.getters", metadata.Guid);
        Assert.Equal("3.2.1", metadata.Version);
        Assert.Equal("~4.1.0", metadata.SptVersion);
    }

    [Fact]
    public void ReadServer_ComputedValues_StayUnknown()
    {
        var dll = Compile("""
            using System.Collections.Generic;
            using SPTarkov.Server.Core.Models.Spt.Mod;
            using Version = SemanticVersioning.Version;
            using Range = SemanticVersioning.Range;

            public static class Constants
            {
                public static readonly string Guid = "com.example." + System.Environment.ProcessorCount;
                public static string Build() => "1.0.0";
            }

            public sealed record ModMetadata : IModMetadata
            {
                public string ModGuid { get; init; } = Constants.Guid;
                public string Name { get; init; } = "Computed";
                public string Author { get; init; } = "Someone";
                public Version Version { get; init; } = new(Constants.Build());
                public Range SptVersion { get; init; } = new("~4.1.0");
                public Dictionary<string, Range>? ModDependencies { get; init; }
            }
            """);

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.NotNull(metadata);
        Assert.Null(metadata.Guid);
        Assert.Null(metadata.Version);
        Assert.Equal("Computed", metadata.Name);
        Assert.Equal("~4.1.0", metadata.SptVersion);
    }

    [Fact]
    public void ReadServer_NoMetadataType_IsNull()
    {
        var dll = Compile("public sealed class Helper { public string Value { get; init; } = \"com.not.a.mod\"; }");

        Assert.Null(ModAssemblyMetadata.ReadServer(dll));
    }

    [Fact]
    public void ReadServer_NotManagedCode_IsNull()
    {
        var path = Path.Combine(_dir, "native.dll");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x01, 0x02]);

        Assert.Null(ModAssemblyMetadata.ReadServer(path));
        Assert.True(ModAssemblyMetadata.ReadPlugin(path).IsEmpty);
    }

    [Fact]
    public void ReadServer_MissingFile_IsNull() =>
        Assert.Null(ModAssemblyMetadata.ReadServer(Path.Combine(_dir, "missing.dll")));

    // Real compiler output from Chris's own server mods (MIT). Configurable Softcore's record was
    // never bumped past 0.0.1, which is exactly what it declares.
    [Theory]
    [InlineData("ConfigurableSoftcore.Server.dll", "com.thecrimsonfuckr.configurablesoftcore", "Configurable Softcore", "0.0.1", "~4.0.0")]
    [InlineData("TCFModSync.Server.dll", "com.thecrimsonfuckr.tcfmodsync", "TCF-ModSync", "2.0.0", "~4.0.0")]
    [InlineData("TCFMM.ServerMap.Stub.Spt40.dll", "com.thecrimsonfuckr.servermap", "TCFMM Server Map", "0.2.0", ">=4.0.0 <4.1.0")]
    [InlineData("TCFMM.ServerMap.Stub.Spt41.dll", "com.thecrimsonfuckr.servermap", "TCFMM Server Map", "0.2.1", ">=4.1.0 <4.2.0")]
    public void ReadServer_RealServerMods(string file, string guid, string name, string version, string sptVersion)
    {
        var metadata = ModAssemblyMetadata.ReadServer(Fixture(file));

        Assert.NotNull(metadata);
        Assert.Equal(guid, metadata.Guid);
        Assert.Equal(name, metadata.Name);
        Assert.Equal("TheCrimsonFuckr", metadata.Author);
        Assert.Equal(version, metadata.Version);
        Assert.Equal(sptVersion, metadata.SptVersion);
        Assert.Empty(metadata.Dependencies);
    }

    [Fact]
    public void ReadPlugin_ReadsGuidNameVersionAndDependencies()
    {
        var dll = Compile("""
            using BepInEx;

            [BepInPlugin("com.blackdiv.tacticaltoaster", "BlackDiv", "1.1.0")]
            [BepInDependency("com.wtt.commonlib")]
            [BepInDependency("com.optional.thing", BepInDependency.DependencyFlags.SoftDependency)]
            [BepInDependency("com.morebotsapi.tacticaltoaster", "2.0.0")]
            public sealed class Plugin { }
            """, BepInExStubs);

        var metadata = ModAssemblyMetadata.ReadPlugin(dll);

        Assert.Equal("com.blackdiv.tacticaltoaster", metadata.Guid);
        Assert.Equal("BlackDiv", metadata.Name);
        Assert.Equal("1.1.0", metadata.Version);
        Assert.Equal(
            [
                new ModDependencyRef("com.wtt.commonlib", false),
                new ModDependencyRef("com.optional.thing", true),
                new ModDependencyRef("com.morebotsapi.tacticaltoaster", false, ">=2.0.0"),
            ],
            metadata.Dependencies.OrderBy(d => d.Identifier == "com.morebotsapi.tacticaltoaster").ThenBy(d => d.IsSoft));
        Assert.Null(metadata.SptVersion);
    }

    [Fact]
    public void ReadPlugin_NoPluginAttribute_IsEmpty()
    {
        var dll = Compile("public sealed class Library { }", BepInExStubs);

        Assert.True(ModAssemblyMetadata.ReadPlugin(dll).IsEmpty);
    }

    [Fact]
    public void ReadPlugin_ServerModDll_HasNoPlugin()
    {
        Assert.Null(ModAssemblyMetadata.ReadPlugin(Fixture("ConfigurableSoftcore.Server.dll")).Guid);
    }

    private static string Spt41Record(
        string versionLine,
        string dependencies = "public Dictionary<string, Range>? ModDependencies { get; init; }") => $$"""
        using System.Collections.Generic;
        using SPTarkov.Server.Core.Models.Spt.Mod;
        using Version = SemanticVersioning.Version;
        using Range = SemanticVersioning.Range;

        public sealed record ModMetadata : IModMetadata
        {
            public string ModGuid { get; init; } = "com.example.spt41";
            public string Name { get; init; } = "Example 4.1";
            public string Author { get; init; } = "Someone";
            {{versionLine}}
            public Range SptVersion { get; init; } = new(">=4.1.0 <4.2.0");
            public bool HasPrepatcher { get; init; }
            {{dependencies}}
        }
        """;
}
