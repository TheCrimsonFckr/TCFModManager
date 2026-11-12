using System.Text.Json;
using System.Text.Json.Nodes;
using TCFModManager.Core.Services;

namespace TCFModManager.Core.ServerMap;

//
// The server operator's record of which machine made which profile: Data\ServerMap\
// profile-owners.json beside the key, the file the Server Map mod reads for its profile rules and
// writes when a machine claims a profile it just made. Here the host gives a profile to a machine,
// or takes it back. Only ever touched on the machine running the server.
//
public static class ServerMapProfileOwners
{
    public const string FileName = "profile-owners.json";

    public const string ClientsFileName = "clients.json";

    // Null when this machine runs no Server Map server.
    public static string? DirectoryFor(string? sptInstallPath, string? appDirectory = null) =>
        ServerMapKeyFile.TryFind(sptInstallPath, out var key, appDirectory) ? Path.GetDirectoryName(key) : null;

    // Profile id to client id.
    public static IReadOnlyDictionary<string, string> Read(string directory)
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root) return new Dictionary<string, string>();

            var owners = root.FirstOrDefault(p => string.Equals(p.Key, "owners", StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
            return (owners ?? [])
                .Where(o => o.Value is JsonValue v && v.TryGetValue<string>(out _))
                .ToDictionary(o => o.Key, o => o.Value!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new Dictionary<string, string>();
        }
    }

    // Gives a profile to a machine, or to nobody when clientId is null. Everything else in the file is kept.
    public static bool TryAssign(string directory, string profileId, string? clientId)
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            JsonObject root;
            try
            {
                root = File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing ? existing : new JsonObject();
            }
            catch (JsonException)
            {
                root = new JsonObject();
            }

            var ownersKey = root.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, "owners", StringComparison.OrdinalIgnoreCase)) ?? "owners";
            if (root[ownersKey] is not JsonObject owners)
            {
                owners = new JsonObject();
                root[ownersKey] = owners;
            }

            foreach (var key in owners.Select(p => p.Key).Where(k => string.Equals(k, profileId, StringComparison.OrdinalIgnoreCase)).ToList())
                owners.Remove(key);
            if (clientId is not null) owners[profileId] = clientId;

            if (!root.Any(p => string.Equals(p.Key, "schemaVersion", StringComparison.OrdinalIgnoreCase))) root["schemaVersion"] = 1;

            Directory.CreateDirectory(directory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);

            AppLog.Info("ServerMap", clientId is null ? $"profile {profileId} given to nobody" : $"profile {profileId} given to machine {clientId}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("ServerMap", $"couldn't write {path}: {ex.Message}");
            return false;
        }
    }

    // The machines that report to this server, as the mod keeps them in clients.json: id and name.
    public static IReadOnlyList<(string ClientId, string DisplayName)> Machines(string directory)
    {
        var path = Path.Combine(directory, ClientsFileName);
        try
        {
            if (!File.Exists(path)) return [];

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("clients", out var clients) || clients.ValueKind != JsonValueKind.Array) return [];

            return clients.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.Object)
                .Select(c => (
                    Id: c.TryGetProperty("clientId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : "",
                    Name: c.TryGetProperty("displayName", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : ""))
                .Where(c => Guid.TryParseExact(c.Id, "D", out _))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
