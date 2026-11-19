using System.Text.Json;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

public enum SptProfileFixState
{
    NotFound,
    Off,
    On,
}

public enum SptProfileFixOutcome
{
    TurnedOn,
    NotChangeable,
    Failed,
}

//
// OPEN-52 N29: SPT's own fix for a profile it won't load - core.json's removeModItemsFromProfile and
// removeInvalidTradersFromProfile. Only those two values are changed; the file as it was is kept
// beside it the first time.
//
public static partial class SptProfileFixes
{
    public static readonly string[] Switches = ["removeModItemsFromProfile", "removeInvalidTradersFromProfile"];

    public const string BackupSuffix = ".before-tcfmm";

    [GeneratedRegex(@"(""(?<name>removeModItemsFromProfile|removeInvalidTradersFromProfile)""\s*:\s*)(?<value>true|false)")]
    private static partial Regex Switch();

    // SPT 4 keeps core.json in SPT_Data/configs, SPT 3 in SPT_Data/Server/configs.
    public static string? ConfigPath(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)
            || !SptInstallationService.TryGetServerRoot(installPath, out var serverRoot)) return null;

        var server = Path.Combine(installPath, serverRoot);
        return new[]
            {
                Path.Combine(server, "SPT_Data", "configs", "core.json"),
                Path.Combine(server, "SPT_Data", "Server", "configs", "core.json"),
            }
            .FirstOrDefault(File.Exists);
    }

    public static SptProfileFixState Read(string? configPath)
    {
        if (configPath is null || !File.Exists(configPath)) return SptProfileFixState.NotFound;

        try
        {
            return StateOf(File.ReadAllText(configPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Diagnose", $"couldn't read {configPath}: {ex.Message}");
            return SptProfileFixState.NotFound;
        }
    }

    public static SptProfileFixState StateOf(string json)
    {
        var values = Switch().Matches(json)
            .GroupBy(m => m.Groups["name"].Value)
            .ToDictionary(g => g.Key, g => g.All(m => m.Groups["value"].Value == "true"));

        if (Switches.Any(s => !values.ContainsKey(s))) return SptProfileFixState.NotFound;
        return values.Values.All(on => on) ? SptProfileFixState.On : SptProfileFixState.Off;
    }

    public static string? TurnedOn(string json)
    {
        if (StateOf(json) == SptProfileFixState.NotFound) return null;

        var changed = Switch().Replace(json, m => m.Groups[1].Value + "true");
        try
        {
            using var _ = JsonDocument.Parse(changed,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return null;
        }

        return changed;
    }

    public static SptProfileFixOutcome TurnOn(string configPath)
    {
        try
        {
            var json = File.ReadAllText(configPath);
            if (TurnedOn(json) is not { } changed) return SptProfileFixOutcome.NotChangeable;

            var backup = configPath + BackupSuffix;
            if (!File.Exists(backup)) File.Copy(configPath, backup);

            SafeFile.WriteText(configPath, changed);
            AppLog.Info("Diagnose", $"turned on {string.Join(", ", Switches)} in {configPath}");
            return SptProfileFixOutcome.TurnedOn;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Diagnose", $"couldn't change {configPath}: {ex.Message}");
            return SptProfileFixOutcome.Failed;
        }
    }
}
