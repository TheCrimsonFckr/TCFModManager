using System.Text.Json;

namespace TCFModManager.Core.Services;

//
// The values SPT 4.1's enum prepatches add: user\patchers\<ModGuid>\*.json, each a list of
// { enumType, constantName, constantValue }. A mod turned off leaves its folder there.
//
public static class ServerPrepatches
{
    // Every value added, with the folder (a mod's ModGuid) that adds it. Files that can't be read are skipped.
    public static IReadOnlyList<(string Guid, string Value)> Read(string installPath)
    {
        var found = new List<(string, string)>();

        foreach (var mods in DisabledModPaths.ServerContainers(installPath))
        {
            if (Path.GetDirectoryName(mods) is not { } user) continue;

            var patchers = Path.Combine(user, "patchers");
            if (!Directory.Exists(patchers)) continue;

            try
            {
                foreach (var folder in Directory.EnumerateDirectories(patchers))
                    foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
                        foreach (var value in ValuesIn(file))
                            found.Add((Path.GetFileName(folder), value));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug("Diagnose", $"couldn't read {patchers}: {ex.Message}");
            }
        }

        return found;
    }

    private static List<string> ValuesIn(string file)
    {
        var values = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Array) return values;

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("constantName", out var name)
                    && name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } value)
                {
                    values.Add(value);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Debug("Diagnose", $"couldn't read {file}: {ex.Message}");
        }

        return values;
    }
}
