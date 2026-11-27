using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public enum ClientLoadProblem
{
    // An assembly the plugin was built against isn't in the game, BepInEx or the turned-on plugins.
    MissingAssembly,

    // The assembly is there, but not a type the plugin uses from it.
    MissingType,

    // The type is there, but not a method, field, property or event the plugin uses on it.
    MissingMember,

    // A [HarmonyPatch] attribute names a type or method that isn't there.
    PatchTargetMissing,

    // A [BepInDependency] the plugin can't load without isn't installed.
    DependencyMissing,

    // A [BepInDependency] the plugin can't load without is installed but turned off.
    DependencyOff,
}

/// <summary>One thing a client plugin uses that the install doesn't have.</summary>
/// <param name="Plugin">The plugin's DLL.</param>
/// <param name="Assembly">The assembly it expects the thing in.</param>
/// <param name="Type">The type, namespace-qualified; nested types joined with '+'.</param>
/// <param name="Member">The member, for MissingMember and a patch target's method.</param>
public sealed record ClientLoadFinding(string Plugin, ClientLoadProblem Problem, string Assembly, string? Type, string? Member);

/// <summary>A client mod and what the dry load check found in it; Mod is null for a plugin no
/// installed mod owns (SPT's own, say), named by File.</summary>
public sealed record ClientModCheck(InstalledMod? Mod, string File, IReadOnlyList<ClientLoadFinding> Findings);

//
// OPEN-54 N27b: a dry load check of the client plugins, with no game start. Each plugin's type and
// member references are looked up in the game's Managed assemblies, BepInEx's core and the other
// turned-on plugins and patchers; so are the targets its [HarmonyPatch] attributes name. A plugin
// built for another game or SPT version shows up here. Patches that find their target by name at
// runtime don't, and members a patcher adds while the game loads look missing - it narrows, it
// never clears.
//
public static class ClientLoadCheck
{
    // The client mods of an install with anything found, each with its plugins' findings and the
    // dependencies it can't load without that are missing or turned off.
    public static IReadOnlyList<ClientModCheck> Run(string gameRoot, string installPath, IReadOnlyList<InstalledMod> scanned)
    {
        var findings = Run(gameRoot);
        var loaded = InstalledModScanner.LoadedPluginGuids(installPath);
        var clients = scanned.Where(m => m.Target == InstalledModTarget.Client && !m.IsDisabled).ToList();
        var graph = ModDependencyGraph.Build(scanned);

        var byMod = new Dictionary<InstalledMod, List<ClientLoadFinding>>();
        var loose = new Dictionary<string, List<ClientLoadFinding>>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in findings)
        {
            if (clients.FirstOrDefault(m => Owns(m, finding.Plugin)) is { } owner)
                Add(byMod, owner, finding);
            else
                Add(loose, finding.Plugin, finding);
        }

        foreach (var mod in clients)
        {
            foreach (var missing in graph.MissingFor([mod], loaded))
            {
                Add(byMod, mod, missing.DisabledProvider is { } off
                    ? new ClientLoadFinding(mod.FolderPath, ClientLoadProblem.DependencyOff, "", missing.Identifier, off.DeclaredName ?? off.Name)
                    : new ClientLoadFinding(mod.FolderPath, ClientLoadProblem.DependencyMissing, "", missing.Identifier, null));
            }
        }

        return
        [
            .. byMod.Select(m => new ClientModCheck(m.Key, m.Key.FolderPath, m.Value)).OrderBy(c => c.Mod!.DeclaredName ?? c.Mod.Name, StringComparer.OrdinalIgnoreCase),
            .. loose.Select(l => new ClientModCheck(null, l.Key, l.Value)).OrderBy(c => c.File, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static void Add<TKey>(Dictionary<TKey, List<ClientLoadFinding>> into, TKey key, ClientLoadFinding finding) where TKey : notnull
    {
        if (!into.TryGetValue(key, out var list)) into[key] = list = [];
        list.Add(finding);
    }

    private static bool Owns(InstalledMod mod, string dll)
    {
        var folder = Path.GetFullPath(mod.FolderPath);
        var file = Path.GetFullPath(dll);
        if (string.Equals(folder, file, StringComparison.OrdinalIgnoreCase)) return true;

        var prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        return mod.OwnedFiles is not { } owned
            || owned.Contains(Path.GetRelativePath(folder, file).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ClientLoadFinding> Run(string gameRoot)
    {
        var bepInEx = Path.Combine(gameRoot, "BepInEx");
        var plugins = Dlls(Path.Combine(bepInEx, "plugins"), recurse: true);
        var patchers = Dlls(Path.Combine(bepInEx, "patchers"), recurse: true);

        var index = new AssemblyIndex();
        foreach (var dll in Dlls(Path.Combine(gameRoot, "EscapeFromTarkov_Data", "Managed"), recurse: false)
            .Concat(Dlls(Path.Combine(bepInEx, "core"), recurse: false))
            .Concat(patchers)
            .Concat(plugins))
        {
            index.Add(dll);
        }

        // What the patchers may add to the game's assemblies as it loads: a member or type named in one
        // of their string literals isn't reported missing.
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var patcher in patchers) StringLiterals(patcher, added);

        return [.. plugins.SelectMany(dll => Check(dll, index)).Where(f => !AddedAtLoad(f, added))];
    }

    private static bool AddedAtLoad(ClientLoadFinding finding, IReadOnlySet<string> added) => finding.Problem switch
    {
        ClientLoadProblem.MissingMember or ClientLoadProblem.PatchTargetMissing when finding.Member is { } member => added.Contains(member),
        ClientLoadProblem.MissingType when finding.Type is { } type => added.Contains(type) || added.Contains(type[(type.LastIndexOfAny(['.', '+']) + 1)..]),
        _ => false,
    };

    private static void StringLiterals(string dll, HashSet<string> into)
    {
        try
        {
            using var stream = File.OpenRead(dll);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return;
            var md = pe.GetMetadataReader();

            var handle = MetadataTokens.UserStringHandle(1);
            while (!handle.IsNil)
            {
                into.Add(md.GetUserString(handle));
                handle = md.GetNextHandle(handle);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            AppLog.Debug("ClientCheck", $"couldn't read {dll}: {ex.Message}");
        }
    }

    public static IReadOnlyList<ClientLoadFinding> Check(string plugin, AssemblyIndex index)
    {
        var findings = new List<ClientLoadFinding>();
        try
        {
            using var stream = File.OpenRead(plugin);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return findings;
            var md = pe.GetMetadataReader();

            var missingAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var handle in md.TypeReferences)
            {
                if (Resolve(md, handle, index) is not { } key) continue;
                if (key.Assembly is null) continue;

                if (!index.HasAssembly(key.Assembly))
                {
                    if (missingAssemblies.Add(key.Assembly))
                        findings.Add(new ClientLoadFinding(plugin, ClientLoadProblem.MissingAssembly, key.Assembly, null, null));
                    continue;
                }

                if (index.Find(key) is null)
                    findings.Add(new ClientLoadFinding(plugin, ClientLoadProblem.MissingType, key.Assembly, key.Name, null));
            }

            foreach (var handle in md.MemberReferences)
            {
                var member = md.GetMemberReference(handle);
                var parent = ParentType(md, member.Parent, index);
                if (parent is not { Assembly: { } assembly } key || !index.HasAssembly(assembly)) continue;
                if (index.Find(key) is not { } type) continue;

                var name = md.GetString(member.Name);
                if (!index.HasMember(type, name))
                    findings.Add(new ClientLoadFinding(plugin, ClientLoadProblem.MissingMember, assembly, key.Name, name));
            }

            foreach (var target in HarmonyTargets(md))
            {
                var type = index.FindAnywhere(target.Type, target.Assembly);
                if (type is null)
                {
                    findings.Add(new ClientLoadFinding(plugin, ClientLoadProblem.PatchTargetMissing, target.Assembly ?? "", target.Type, target.Method));
                }
                else if (target.Method is { } method && !index.HasMember(type, method))
                {
                    findings.Add(new ClientLoadFinding(plugin, ClientLoadProblem.PatchTargetMissing, type.Assembly, target.Type, method));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            AppLog.Debug("ClientCheck", $"couldn't read {plugin}: {ex.Message}");
        }

        return findings.DistinctBy(f => (f.Problem, f.Assembly, f.Type, f.Member)).ToList();
    }

    private static IEnumerable<string> Dlls(string folder, bool recurse)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.dll", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // ---- references --------------------------------------------------------------------------------

    public readonly record struct TypeKey(string? Assembly, string Name);

    private static TypeKey? Resolve(MetadataReader md, TypeReferenceHandle handle, AssemblyIndex index)
    {
        var reference = md.GetTypeReference(handle);
        var name = Qualified(md.GetString(reference.Namespace), md.GetString(reference.Name));
        var scope = reference.ResolutionScope;

        switch (scope.Kind)
        {
            case HandleKind.AssemblyReference:
                return new TypeKey(md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name), name);
            case HandleKind.TypeReference:
                return Resolve(md, (TypeReferenceHandle)scope, index) is { } outer
                    ? new TypeKey(outer.Assembly, outer.Name + "+" + md.GetString(reference.Name))
                    : null;
            default:
                return null;
        }
    }

    private static TypeKey? ParentType(MetadataReader md, EntityHandle parent, AssemblyIndex index)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                return Resolve(md, (TypeReferenceHandle)parent, index);
            case HandleKind.TypeSpecification:
                try
                {
                    var spec = md.GetTypeSpecification((TypeSpecificationHandle)parent);
                    return spec.DecodeSignature(GenericDefinitionProvider.Instance, null) is { } h
                        && h.Kind == HandleKind.TypeReference
                        ? Resolve(md, (TypeReferenceHandle)h, index)
                        : null;
                }
                catch (BadImageFormatException)
                {
                    return null;
                }
            default:
                return null;
        }
    }

    private static string Qualified(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;

    // Decodes a generic instance down to its generic type's handle; anything else is of no interest.
    private sealed class GenericDefinitionProvider : ISignatureTypeProvider<EntityHandle?, object?>
    {
        public static readonly GenericDefinitionProvider Instance = new();

        public EntityHandle? GetGenericInstantiation(EntityHandle? genericType, ImmutableArray<EntityHandle?> typeArguments) => genericType;
        public EntityHandle? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => handle;
        public EntityHandle? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => handle;
        public EntityHandle? GetArrayType(EntityHandle? elementType, ArrayShape shape) => null;
        public EntityHandle? GetByReferenceType(EntityHandle? elementType) => null;
        public EntityHandle? GetFunctionPointerType(MethodSignature<EntityHandle?> signature) => null;
        public EntityHandle? GetGenericMethodParameter(object? genericContext, int index) => null;
        public EntityHandle? GetGenericTypeParameter(object? genericContext, int index) => null;
        public EntityHandle? GetModifiedType(EntityHandle? modifier, EntityHandle? unmodifiedType, bool isRequired) => unmodifiedType;
        public EntityHandle? GetPinnedType(EntityHandle? elementType) => null;
        public EntityHandle? GetPointerType(EntityHandle? elementType) => null;
        public EntityHandle? GetPrimitiveType(PrimitiveTypeCode typeCode) => null;
        public EntityHandle? GetSZArrayType(EntityHandle? elementType) => null;
        public EntityHandle? GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => null;
    }

    // ---- [HarmonyPatch] ----------------------------------------------------------------------------

    private sealed record PatchTarget(string Type, string? Assembly, string? Method);

    private static IEnumerable<PatchTarget> HarmonyTargets(MetadataReader md)
    {
        var targets = new List<PatchTarget>();
        var provider = new AttributeTypeNames(md);

        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);
            var classLevel = Patches(md, type.GetCustomAttributes(), provider);
            var (classType, classAssembly, classMethod) = Merge(classLevel);

            var methodTargets = new List<PatchTarget>();
            foreach (var methodHandle in type.GetMethods())
            {
                var parts = Patches(md, md.GetMethodDefinition(methodHandle).GetCustomAttributes(), provider);
                if (parts.Count == 0) continue;

                var (t, a, m) = Merge(parts);
                if ((t ?? classType) is { } targetType)
                    methodTargets.Add(new PatchTarget(targetType, t is null ? classAssembly : a, m ?? classMethod));
            }

            if (methodTargets.Count > 0) targets.AddRange(methodTargets);
            else if (classType is not null) targets.Add(new PatchTarget(classType, classAssembly, classMethod));
        }

        return targets;
    }

    private static (string? Type, string? Assembly, string? Method) Merge(List<(string? Type, string? Assembly, string? Method)> parts) =>
        (parts.Select(p => p.Type).LastOrDefault(t => t is not null),
         parts.Where(p => p.Type is not null).Select(p => p.Assembly).LastOrDefault(),
         parts.Select(p => p.Method).LastOrDefault(m => m is not null));

    private static List<(string? Type, string? Assembly, string? Method)> Patches(MetadataReader md, CustomAttributeHandleCollection attributes, AttributeTypeNames provider)
    {
        var found = new List<(string?, string?, string?)>();
        foreach (var handle in attributes)
        {
            var attribute = md.GetCustomAttribute(handle);
            if (AttributeName(md, attribute) is not "HarmonyLib.HarmonyPatch") continue;

            CustomAttributeValue<string> value;
            try
            {
                value = attribute.DecodeValue(provider);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                continue;
            }

            string? type = null, assembly = null, method = null;
            foreach (var argument in value.FixedArguments)
            {
                if (argument.Type == AttributeTypeNames.SystemType && argument.Value is string typeName)
                    (type, assembly) = SplitTypeName(typeName);
                else if (argument.Type == "string" && argument.Value is string text && method is null)
                    method = text;
            }

            if (type is not null || method is not null) found.Add((type, assembly, method));
        }

        return found;
    }

    private static string? AttributeName(MetadataReader md, CustomAttribute attribute)
    {
        EntityHandle parent = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default,
        };

        return parent.Kind switch
        {
            HandleKind.TypeReference => Qualified(md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Namespace), md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name)),
            HandleKind.TypeDefinition => Qualified(md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)parent).Namespace), md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)parent).Name)),
            _ => null,
        };
    }

    // "Ns.Outer+Inner, Assembly-CSharp, Version=..." into the type and the assembly's simple name;
    // a generic instance ("Ns.Type`1[[Arg, Asm]], Asm") comes back as its generic type.
    public static (string Type, string? Assembly) SplitTypeName(string serialized)
    {
        var depth = 0;
        var comma = -1;
        for (var i = 0; i < serialized.Length && comma < 0; i++)
        {
            switch (serialized[i])
            {
                case '[': depth++; break;
                case ']': depth--; break;
                case ',' when depth == 0: comma = i; break;
            }
        }

        var type = (comma < 0 ? serialized : serialized[..comma]).Trim();
        var bracket = type.IndexOf('[');
        if (bracket > 0) type = type[..bracket];

        var assembly = comma < 0 ? null : serialized[(comma + 1)..].Split(',')[0].Trim();
        return (type, string.IsNullOrEmpty(assembly) ? null : assembly);
    }

    private sealed class AttributeTypeNames(MetadataReader md) : ICustomAttributeTypeProvider<string>
    {
        public const string SystemType = "System.Type";

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode == PrimitiveTypeCode.String ? "string" : typeCode.ToString();
        public string GetSystemType() => SystemType;
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            Qualified(md.GetString(md.GetTypeDefinition(handle).Namespace), md.GetString(md.GetTypeDefinition(handle).Name));
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            Qualified(md.GetString(md.GetTypeReference(handle).Namespace), md.GetString(md.GetTypeReference(handle).Name));
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type == SystemType;
    }
}

//
// Every type, and every member name on it, of the assemblies a client plugin can reach - with each
// type's base, so an inherited member counts, and type forwarders followed.
//
public sealed class AssemblyIndex
{
    public sealed class IndexedType(string assembly, string name)
    {
        public string Assembly { get; } = assembly;
        public string Name { get; } = name;
        public HashSet<string> Members { get; } = new(StringComparer.Ordinal);
        public ClientLoadCheck.TypeKey? Base { get; set; }
    }

    private sealed class IndexedAssembly
    {
        public Dictionary<string, IndexedType> Types { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Forwarded { get; } = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, IndexedAssembly> _assemblies = new(StringComparer.OrdinalIgnoreCase);

    public bool HasAssembly(string name) => _assemblies.ContainsKey(name);

    public void Add(string dll)
    {
        try
        {
            using var stream = File.OpenRead(dll);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return;
            var md = pe.GetMetadataReader();
            if (!md.IsAssembly) return;

            var name = md.GetString(md.GetAssemblyDefinition().Name);
            if (_assemblies.ContainsKey(name)) return;

            var assembly = new IndexedAssembly();
            _assemblies[name] = assembly;

            foreach (var handle in md.TypeDefinitions)
            {
                var definition = md.GetTypeDefinition(handle);
                var type = new IndexedType(name, FullName(md, handle));

                foreach (var method in definition.GetMethods()) type.Members.Add(md.GetString(md.GetMethodDefinition(method).Name));
                foreach (var field in definition.GetFields()) type.Members.Add(md.GetString(md.GetFieldDefinition(field).Name));
                foreach (var property in definition.GetProperties()) type.Members.Add(md.GetString(md.GetPropertyDefinition(property).Name));
                foreach (var e in definition.GetEvents()) type.Members.Add(md.GetString(md.GetEventDefinition(e).Name));

                type.Base = BaseOf(md, definition.BaseType, name);
                assembly.Types[type.Name] = type;
            }

            foreach (var handle in md.ExportedTypes)
            {
                var exported = md.GetExportedType(handle);
                if (!exported.IsForwarder || exported.Implementation.Kind != HandleKind.AssemblyReference) continue;

                var target = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation).Name);
                var ns = md.GetString(exported.Namespace);
                var typeName = md.GetString(exported.Name);
                assembly.Forwarded[ns.Length == 0 ? typeName : ns + "." + typeName] = target;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            AppLog.Debug("ClientCheck", $"couldn't index {dll}: {ex.Message}");
        }
    }

    public IndexedType? Find(ClientLoadCheck.TypeKey key)
    {
        var assemblyName = key.Assembly;
        for (var hops = 0; assemblyName is not null && hops < 4; hops++)
        {
            if (!_assemblies.TryGetValue(assemblyName, out var assembly)) return null;
            if (assembly.Types.TryGetValue(key.Name, out var type)) return type;

            var outer = key.Name.Split('+')[0];
            assemblyName = assembly.Forwarded.TryGetValue(outer, out var next) ? next : null;
        }

        return null;
    }

    // A type named in an attribute: in the assembly it names, else in any indexed assembly.
    public IndexedType? FindAnywhere(string name, string? assembly)
    {
        name = name.Replace('/', '+');
        if (assembly is not null && Find(new ClientLoadCheck.TypeKey(assembly, name)) is { } exact) return exact;
        return _assemblies.Values.Select(a => a.Types.GetValueOrDefault(name)).FirstOrDefault(t => t is not null);
    }

    public bool HasMember(IndexedType type, string member)
    {
        var current = type;
        for (var depth = 0; current is not null && depth < 32; depth++)
        {
            if (current.Members.Contains(member)) return true;
            if (current.Base is not { } baseKey) return false;

            current = Find(baseKey);
            if (current is null) return true;
        }

        return true;
    }

    private static string FullName(MetadataReader md, TypeDefinitionHandle handle)
    {
        var definition = md.GetTypeDefinition(handle);
        var name = md.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil) return FullName(md, declaring) + "+" + name;

        var ns = md.GetString(definition.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static ClientLoadCheck.TypeKey? BaseOf(MetadataReader md, EntityHandle baseType, string ownAssembly)
    {
        if (baseType.IsNil) return null;

        switch (baseType.Kind)
        {
            case HandleKind.TypeDefinition:
                return new ClientLoadCheck.TypeKey(ownAssembly, FullName(md, (TypeDefinitionHandle)baseType));
            case HandleKind.TypeReference:
                return RefKey(md, (TypeReferenceHandle)baseType);
            case HandleKind.TypeSpecification:
                try
                {
                    var spec = md.GetTypeSpecification((TypeSpecificationHandle)baseType);
                    var reader = spec.Signature;
                    var blob = md.GetBlobReader(reader);
                    if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return null;
                    blob.ReadSignatureTypeCode();
                    var generic = blob.ReadTypeHandle();
                    return generic.Kind switch
                    {
                        HandleKind.TypeDefinition => new ClientLoadCheck.TypeKey(ownAssembly, FullName(md, (TypeDefinitionHandle)generic)),
                        HandleKind.TypeReference => RefKey(md, (TypeReferenceHandle)generic),
                        _ => null,
                    };
                }
                catch (BadImageFormatException)
                {
                    return null;
                }
            default:
                return null;
        }
    }

    private static ClientLoadCheck.TypeKey? RefKey(MetadataReader md, TypeReferenceHandle handle)
    {
        var reference = md.GetTypeReference(handle);
        var name = md.GetString(reference.Namespace) is { Length: > 0 } ns ? ns + "." + md.GetString(reference.Name) : md.GetString(reference.Name);
        return reference.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference => new ClientLoadCheck.TypeKey(md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name), name),
            HandleKind.TypeReference => RefKey(md, (TypeReferenceHandle)reference.ResolutionScope) is { } outer
                ? new ClientLoadCheck.TypeKey(outer.Assembly, outer.Name + "+" + md.GetString(reference.Name))
                : null,
            _ => null,
        };
    }
}
