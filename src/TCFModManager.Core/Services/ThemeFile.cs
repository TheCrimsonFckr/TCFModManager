using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed class ThemeDocument
{
    public int SchemaVersion { get; set; } = ThemeFile.SchemaVersion;

    public string? App { get; set; }

    public ThemeDefinition? Theme { get; set; }
}

public enum ThemeFileProblem
{
    Unreadable,
    NotATheme,
    NewerVersion,
}

public sealed record ThemeRead(ThemeDefinition? Theme, ThemeFileProblem? Problem)
{
    public bool Succeeded => Theme is not null;

    public static ThemeRead Failed(ThemeFileProblem problem) => new(null, problem);
}

public static class ThemeFile
{
    public const int SchemaVersion = 1;

    public const string Extension = ".tcftheme";

    public const string FilePattern = "*" + Extension;

    public const string AllFilesPattern = "*.*";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Write(ThemeDefinition theme) =>
        JsonSerializer.Serialize(
            new ThemeDocument { SchemaVersion = SchemaVersion, App = "TCFModManager", Theme = theme },
            Options);

    public static ThemeRead Read(string json)
    {
        ThemeDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<ThemeDocument>(json, Options);
        }
        catch (JsonException)
        {
            return ThemeRead.Failed(ThemeFileProblem.Unreadable);
        }

        if (document is null) return ThemeRead.Failed(ThemeFileProblem.Unreadable);
        if (document.SchemaVersion > SchemaVersion) return ThemeRead.Failed(ThemeFileProblem.NewerVersion);
        if (document.Theme is not { } theme) return ThemeRead.Failed(ThemeFileProblem.NotATheme);

        ThemeRules.Normalise(theme);
        return new ThemeRead(theme, null);
    }

    public static ThemeRead ReadFile(string path)
    {
        try
        {
            return Read(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ThemeRead.Failed(ThemeFileProblem.Unreadable);
        }
    }
}
