using System.Globalization;
using System.Text;

namespace TCFModManager.Core.Services;

public static class ProblemLog
{
    public const int MaxLines = 5000;

    private const string FilePattern = "tcfmm-*.log";
    private const int HeaderSearchLines = 20;
    private const int MinimumRulerLength = 10;

    public static string? ReadCurrentSession()
    {
        AppLog.Flush();
        return ReadLatestSession(AppLog.Directory, DateTime.Now);
    }

    public static string? ReadLatestSession(string directory, DateTime now, int maxLines = MaxLines)
    {
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, FilePattern)
                .Where(file => DateOf(file) is { } date && date <= now.Date)
                .OrderByDescending(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var session = new List<string>();
        foreach (var file in files)
        {
            var lines = ReadLines(file);
            if (lines is null) continue;

            var start = LastHeader(lines);
            session.InsertRange(0, start >= 0 ? lines.Skip(start) : lines);
            if (start >= 0) break;
        }

        while (session.Count > 0 && string.IsNullOrWhiteSpace(session[0])) session.RemoveAt(0);
        while (session.Count > 0 && string.IsNullOrWhiteSpace(session[^1])) session.RemoveAt(session.Count - 1);
        if (session.Count == 0) return null;

        return string.Join('\n', Capped(session, maxLines));
    }

    private static List<string> Capped(List<string> session, int maxLines)
    {
        if (session.Count <= maxLines) return session;

        var header = HeaderLength(session);
        var tail = Math.Max(0, maxLines - header - 1);
        var omitted = session.Count - header - tail;

        var capped = new List<string>(maxLines);
        capped.AddRange(session.GetRange(0, header));
        capped.Add($"... {omitted} lines left out ...");
        capped.AddRange(session.GetRange(session.Count - tail, tail));
        return capped;
    }

    private static int HeaderLength(List<string> session)
    {
        if (session.Count < 2 || !IsHeaderAt(session, 0)) return 0;

        for (var i = 2; i < Math.Min(session.Count, HeaderSearchLines); i++)
        {
            if (IsRuler(session[i])) return i + 1;
        }

        return 0;
    }

    private static int LastHeader(IReadOnlyList<string> lines)
    {
        for (var i = lines.Count - 2; i >= 0; i--)
        {
            if (IsHeaderAt(lines, i)) return i;
        }

        return -1;
    }

    private static bool IsHeaderAt(IReadOnlyList<string> lines, int index) =>
        IsRuler(lines[index])
        && lines[index + 1].StartsWith(SelfMod.Name + " ", StringComparison.Ordinal)
        && lines[index + 1].Contains(" started ", StringComparison.Ordinal);

    private static bool IsRuler(string line) =>
        line.Length >= MinimumRulerLength && line.All(c => c == '=');

    private static DateTime? DateOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return name.Length == "tcfmm-yyyyMMdd".Length
            && DateTime.TryParseExact(name["tcfmm-".Length..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
    }

    private static List<string>? ReadLines(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(line);
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
