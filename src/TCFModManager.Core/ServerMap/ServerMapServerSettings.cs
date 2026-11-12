using System.Text.Json;
using System.Text.Json.Nodes;
using TCFModManager.Core.Services;

namespace TCFModManager.Core.ServerMap;

//
// The server operator's settings, in Data\ServerMap\servermap.json beside the key - the file the
// Server Map mod re-reads on every change. Only ever touched on the machine running the server,
// which is the one where the key file is found.
//
// Written as a merge into whatever is already there, so a setting a later mod version adds, or one
// the operator typed by hand, survives the app switching another one.
//
public static class ServerMapServerSettings
{
    public const string FileName = "servermap.json";

    public const string LanOnlyProperty = "lanOnly";

    public const string HideOtherProfilesProperty = "hideOtherProfiles";

    public const string ProtectOtherProfilesProperty = "protectOtherProfiles";

    // Null when this machine runs no Server Map server.
    public static string? PathFor(string? sptInstallPath, string? appDirectory = null) =>
        ServerMapKeyFile.TryFind(sptInstallPath, out var key, appDirectory)
            ? Path.Combine(Path.GetDirectoryName(key)!, FileName)
            : null;

    public static bool ReadLanOnly(string path) => ReadSwitch(path, LanOnlyProperty);

    public static bool TryWriteLanOnly(string path, bool lanOnly) => TryWriteSwitch(path, LanOnlyProperty, lanOnly);

    public static bool ReadSwitch(string path, string property)
    {
        try
        {
            if (!File.Exists(path)) return false;

            return JsonNode.Parse(File.ReadAllText(path)) is JsonObject root
                && root.FirstOrDefault(p => string.Equals(p.Key, property, StringComparison.OrdinalIgnoreCase))
                    .Value is JsonValue value
                && value.TryGetValue<bool>(out var on)
                && on;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static bool TryWriteSwitch(string path, string property, bool on)
    {
        try
        {
            JsonObject root;
            try
            {
                root = File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing
                    ? existing
                    : new JsonObject();
            }
            catch (JsonException)
            {
                // A hand edit that broke the file is replaced rather than left blocking the switch.
                root = new JsonObject();
            }

            foreach (var key in root.Select(p => p.Key)
                         .Where(k => string.Equals(k, property, StringComparison.OrdinalIgnoreCase)).ToList())
                root.Remove(key);

            root[property] = on;

            var temp = path + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);

            AppLog.Info("ServerMap", $"{property} switched {(on ? "on" : "off")}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("ServerMap", $"couldn't write {path}: {ex.Message}");
            return false;
        }
    }
}
