using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace TCFModManager.Core.Services;

//
// The names an enum has in the install's own SPTarkov.Server.Core.dll - SPT's values before any
// prepatch adds to them. Read from the assembly's metadata, nothing loaded.
//
public static class ServerEnums
{
    public const string SkillTypes = "SPTarkov.Server.Core.Models.Enums.SkillTypes";
    public const string WildSpawnType = "SPTarkov.Server.Core.Models.Eft.Common.WildSpawnType";

    private static readonly Dictionary<(string Dll, string Enum), (DateTime Written, IReadOnlySet<string> Names)> Cache = new();
    private static readonly Lock Gate = new();

    public static string? CoreDll(string? installPath)
    {
        if (!SptInstallationService.TryGetServerRoot(installPath, out var serverRoot)) return null;
        var dll = Path.Combine(installPath!, serverRoot, "SPTarkov.Server.Core.dll");
        return File.Exists(dll) ? dll : null;
    }

    // Null when the dll isn't there, can't be read, or has no such enum.
    public static IReadOnlySet<string>? Names(string? installPath, string fullName) =>
        CoreDll(installPath) is { } dll ? NamesIn(dll, fullName) : null;

    public static IReadOnlySet<string>? NamesIn(string dll, string fullName)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(dll);
            lock (Gate)
            {
                if (Cache.TryGetValue((dll, fullName), out var hit) && hit.Written == written) return hit.Names;
            }

            using var stream = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return null;

            var reader = pe.GetMetadataReader();
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                if (!type.GetDeclaringType().IsNil) continue;
                if (reader.GetString(type.Namespace) + "." + reader.GetString(type.Name) != fullName) continue;

                var names = type.GetFields()
                    .Select(reader.GetFieldDefinition)
                    .Where(f => (f.Attributes & FieldAttributes.Static) != 0)
                    .Select(f => reader.GetString(f.Name))
                    .ToHashSet(StringComparer.Ordinal);

                lock (Gate) Cache[(dll, fullName)] = (written, names);
                return names;
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            AppLog.Debug("Profiles", $"couldn't read {fullName} from {dll}: {ex.Message}");
            return null;
        }
    }
}
