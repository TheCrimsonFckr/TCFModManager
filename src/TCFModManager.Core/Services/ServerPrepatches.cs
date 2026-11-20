using System.Text.Json;

namespace TCFModManager.Core.Services;

/// <summary>One value a mod's prepatch adds: the folder (the mod's ModGuid), the enum's short name ("SkillTypes") and the value.</summary>
public sealed record PrepatchValue(string Guid, string EnumType, string Name);

//
// The values SPT 4.1's enum prepatches add: user\patchers\<ModGuid>\*.json, each a list of
// { enumType, constantName, constantValue }. A mod turned off leaves its folder there.
//
public static class ServerPrepatches
{
    // Every value added, with the folder (a mod's ModGuid) that adds it. Files that can't be read are skipped.
    public static IReadOnlyList<(string Guid, string Value)> Read(string installPath) =>
        [.. Values(installPath).Select(v => (v.Guid, v.Name))];

    public static IReadOnlyList<PrepatchValue> Values(string installPath)
    {
        var found = new List<PrepatchValue>();

        foreach (var mods in DisabledModPaths.ServerContainers(installPath))
        {
            if (Path.GetDirectoryName(mods) is not { } user) continue;

            var patchers = Path.Combine(user, "patchers");
            if (!Directory.Exists(patchers)) continue;

            try
            {
                foreach (var folder in Directory.EnumerateDirectories(patchers))
                    foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
                        foreach (var (enumType, name) in ValuesIn(file))
                            found.Add(new PrepatchValue(Path.GetFileName(folder), enumType, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug("Diagnose", $"couldn't read {patchers}: {ex.Message}");
            }
        }

        return found;
    }

    private static List<(string EnumType, string Name)> ValuesIn(string file)
    {
        var values = new List<(string, string)>();
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
                    var enumType = item.TryGetProperty("enumType", out var type) && type.ValueKind == JsonValueKind.String
                        ? ShortName(type.GetString())
                        : string.Empty;
                    values.Add((enumType, value));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Debug("Diagnose", $"couldn't read {file}: {ex.Message}");
        }

        return values;
    }

    private static string ShortName(string? fullName) =>
        fullName is null ? string.Empty : fullName[(fullName.LastIndexOf('.') + 1)..];
}
