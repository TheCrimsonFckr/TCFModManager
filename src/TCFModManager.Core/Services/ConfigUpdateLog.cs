using System.Text.Json;
using System.Text.Json.Serialization;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// The last few updates' config reports, in Data\config_updates.json.
//
// Kept because the moment an update finishes is not always the moment somebody reads about it - the
// Configs page can say what the last update did to the file being looked at, days later. Newest
// first, capped: this is a record of recent events, not a history to browse.
//
public sealed class ConfigUpdateLog(string? filePath = null)
{
    private const int Keep = 50;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly JsonFile<ConfigUpdateHistory> _file =
        new(filePath ?? Path.Combine(AppPaths.DataDirectory, "config_updates.json"), Options, keepBackups: true);

    public string FilePath => _file.FilePath;

    public ConfigUpdateHistory Load()
    {
        try
        {
            return _file.Read() ?? new ConfigUpdateHistory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConfigUpdateHistory();
        }
    }

    // Best-effort, like every other store here: a report nobody could write down is not a reason to
    // fail the install it describes.
    public void Add(ConfigUpdateReport report)
    {
        if (report.Files.Count == 0) return;

        try
        {
            var history = Load();
            history.Reports.Insert(0, report);
            if (history.Reports.Count > Keep) history.Reports.RemoveRange(Keep, history.Reports.Count - Keep);

            _file.Write(history);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Configs", $"couldn't record the config report: {ex.Message}");
        }
    }

    //
    // The most recent update that said something about one config file, and what it said about it.
    // Null when no recorded update mentions it.
    //
    public (ConfigUpdateReport Report, ConfigFileOutcome Outcome)? LastFor(string relativePath)
    {
        foreach (var report in Load().Reports)
        {
            var outcome = report.Files.FirstOrDefault(f =>
                string.Equals(f.Path, relativePath, StringComparison.OrdinalIgnoreCase));

            if (outcome is not null) return (report, outcome);
        }

        return null;
    }
}

public sealed class ConfigUpdateHistory
{
    public List<ConfigUpdateReport> Reports { get; init; } = [];
}
