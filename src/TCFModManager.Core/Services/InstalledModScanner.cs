using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Scans an SPT install folder for installed client (BepInEx/plugins) and server (user/mods) mods.
public static class InstalledModScanner
{
    // Folder/DLL names that are core SPT client files rather than an installed mod.
    private static readonly HashSet<string> CoreSptEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "spt",
    };

    public static List<InstalledMod> Scan(string? installPath)
    {
        var results = new List<InstalledMod>();
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) return results;

        ScanClientFolder(Path.Combine(installPath, "BepInEx", "plugins"), results);

        // Checks all three known server-content layouts; whichever exists is scanned.
        ScanServerFolder(Path.Combine(installPath, "SPT_Runtime", "user", "mods"), results);
        ScanServerFolder(Path.Combine(installPath, "SPT", "user", "mods"), results);
        ScanServerFolder(Path.Combine(installPath, "user", "mods"), results);

        return results;
    }

    // Client (BepInEx) mods are versioned via their DLL's embedded file version resource.
    // Handles both a subfolder containing DLLs and a single loose DLL directly in plugins.
    private static void ScanClientFolder(string root, List<InstalledMod> results)
    {
        if (!Directory.Exists(root)) return;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(name) || CoreSptEntries.Contains(name)) continue;

            // Prefer a DLL whose name matches the folder; otherwise take the first DLL found.
            var dlls = Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).ToList();
            var dll = dlls.FirstOrDefault(d => string.Equals(Path.GetFileNameWithoutExtension(d), name, StringComparison.OrdinalIgnoreCase))
                ?? dlls.FirstOrDefault();

            // Check every DLL in the folder for the [BepInPlugin] attribute; it isn't necessarily
            // on the same DLL used for versioning.
            var guid = dlls.Select(TryReadBepInPluginGuid).FirstOrDefault(g => g is not null);

            results.Add(new InstalledMod
            {
                Name = name,
                Version = dll is null ? null : TryGetFileVersion(dll),
                Guid = guid,
                Target = InstalledModTarget.Client,
                FolderPath = dir,
                InstalledAt = TryGetCreationTime(dir),
            });
        }

        foreach (var dll in Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            if (CoreSptEntries.Contains(name)) continue;

            results.Add(new InstalledMod
            {
                Name = name,
                Version = TryGetFileVersion(dll),
                Guid = TryReadBepInPluginGuid(dll),
                Target = InstalledModTarget.Client,
                FolderPath = dll,
                InstalledAt = TryGetCreationTime(dll),
            });
        }
    }

    // Server mods are versioned via their package.json manifest's "name"/"version"/"author"
    // fields. Falls back to scanning for a DLL and reading its FileVersionInfo when package.json is
    // missing or has no version.
    private static void ScanServerFolder(string root, List<InstalledMod> results)
    {
        if (!Directory.Exists(root)) return;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var folderName = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(folderName)) continue;

            var name = folderName;
            string? version = null;
            string? author = null;

            var packageJsonPath = Path.Combine(dir, "package.json");
            if (File.Exists(packageJsonPath))
            {
                try
                {
                    using var stream = File.OpenRead(packageJsonPath);
                    using var doc = JsonDocument.Parse(stream);
                    var root2 = doc.RootElement;

                    if (root2.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(n.GetString()))
                    {
                        name = n.GetString()!;
                    }

                    if (root2.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                        version = v.GetString();

                    if (root2.TryGetProperty("author", out var a))
                    {
                        // npm's "author" field is either a plain string or an object with a "name".
                        author = a.ValueKind switch
                        {
                            JsonValueKind.String => a.GetString(),
                            JsonValueKind.Object when a.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String => an.GetString(),
                            _ => null,
                        };
                    }
                }
                catch (JsonException)
                {
                    // Malformed package.json - still list the mod by its folder name.
                }
            }

            if (version is null)
            {
                // Fall back to a DLL in the folder, preferring one whose name matches the folder/mod name.
                var dlls = Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).ToList();
                var dll = dlls.FirstOrDefault(d => string.Equals(Path.GetFileNameWithoutExtension(d), name, StringComparison.OrdinalIgnoreCase))
                    ?? dlls.FirstOrDefault();
                if (dll is not null) version = TryGetFileVersion(dll);
            }

            results.Add(new InstalledMod
            {
                Name = name,
                Version = version,
                Author = author,
                Target = InstalledModTarget.Server,
                FolderPath = dir,
                InstalledAt = TryGetCreationTime(dir),
            });
        }
    }

    private static DateTimeOffset? TryGetCreationTime(string path)
    {
        try
        {
            return new DateTimeOffset(Directory.Exists(path) ? Directory.GetCreationTimeUtc(path) : File.GetCreationTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryGetFileVersion(string dllPath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dllPath);
            var raw = (info.FileVersion ?? info.ProductVersion ?? "").Trim();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Reads the GUID from a compiled BepInEx plugin DLL's [BepInPlugin("guid", ...)]
    // attribute by walking its raw PE/ECMA-335 metadata, without loading or executing the assembly.
    // Checks every type in the DLL for a custom attribute resolving to "BepInEx.BepInPlugin", then
    // decodes the first constructor argument as the GUID. Returns null (never throws) if the DLL
    // isn't readable managed code or has no [BepInPlugin]-attributed type.
    private static string? TryReadBepInPluginGuid(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return null;

            var reader = peReader.GetMetadataReader();

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);

                foreach (var attributeHandle in typeDef.GetCustomAttributes())
                {
                    var attribute = reader.GetCustomAttribute(attributeHandle);
                    if (!IsBepInPluginAttribute(reader, attribute)) continue;

                    var guid = TryDecodeFirstStringArgument(reader, attribute);
                    if (!string.IsNullOrWhiteSpace(guid)) return guid;
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // True if this custom attribute's constructor resolves to BepInEx.BepInPlugin, resolved
    // by namespace/name from the DLL's own TypeReference table without loading BepInEx.dll.
    private static bool IsBepInPluginAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference) return false;

        var memberRef = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (memberRef.Parent.Kind != HandleKind.TypeReference) return false;

        var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
        return reader.GetString(typeRef.Namespace) == "BepInEx"
            && reader.GetString(typeRef.Name) == "BepInPlugin";
    }

    private static string? TryDecodeFirstStringArgument(MetadataReader reader, CustomAttribute attribute)
    {
        var blobReader = reader.GetBlobReader(attribute.Value);

        // Custom attribute value blobs start with a fixed 2-byte prolog (0x0001) per ECMA-335 II.23.3.
        if (blobReader.ReadUInt16() != 1) return null;

        return blobReader.ReadSerializedString();
    }
}
