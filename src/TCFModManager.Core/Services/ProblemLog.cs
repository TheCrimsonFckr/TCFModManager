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

    public static string? ReadPreviousSession()
    {
        AppLog.Flush();
        return ReadLatestSession(AppLog.Directory, DateTime.Now, sessionsBack: 1);
    }

    public static string? ReadLatestSession(string directory, DateTime now, int maxLines = MaxLines, int sessionsBack = 0)
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

        var wanted = sessionsBack + 1;
        var lines = new List<string>();
        foreach (var file in files)
        {
            var read = ReadLines(file);
            if (read is null) continue;

            lines.InsertRange(0, read);
            if (Headers(lines).Count >= wanted) break;
        }

        var headers = Headers(lines);
        List<string> session;
        if (headers.Count >= wanted)
        {
            var start = headers[^wanted];
            var end = sessionsBack == 0 ? lines.Count : headers[^sessionsBack];
            session = lines.GetRange(start, end - start);
        }
        else if (sessionsBack == 0)
        {
            session = lines;
        }
        else
        {
            return null;
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

    private static List<int> Headers(IReadOnlyList<string> lines)
    {
        var headers = new List<int>();
        for (var i = 0; i < lines.Count - 1; i++)
        {
            if (IsHeaderAt(lines, i)) headers.Add(i);
        }

        return headers;
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
