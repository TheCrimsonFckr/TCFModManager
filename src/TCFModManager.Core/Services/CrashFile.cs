using System.Globalization;

namespace TCFModManager.Core.Services;

public sealed record LastCrash(DateTimeOffset At, string Text);

public static class CrashFile
{
    public const string FileName = "last-crash.txt";

    public static string DefaultPath => Path.Combine(AppLog.Directory, FileName);

    public static void Write(Exception? exception, DateTimeOffset at, string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                at.ToString("o", CultureInfo.InvariantCulture) + "\n" + (exception?.ToString() ?? "unknown"));
        }
        catch (Exception)
        {
        }
    }

    public static LastCrash? Take(string? path = null)
    {
        path ??= DefaultPath;

        string text;
        try
        {
            if (!File.Exists(path)) return null;
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("App", $"couldn't read {FileName}: {ex.Message}");
            return null;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("App", $"couldn't remove {FileName}: {ex.Message}");
        }

        var parts = text.Split('\n', 2);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1])) return null;

        return DateTimeOffset.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? new LastCrash(at, parts[1].Trim())
            : null;
    }
}
