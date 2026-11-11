using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class ProblemLogTests : IDisposable
{
    private const string Ruler = "========================================================";

    private static readonly DateTime Today = new(2026, 11, 11, 21, 0, 0);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "problemlog-" + Guid.NewGuid().ToString("N"));

    public ProblemLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string[] Header(string started) =>
    [
        "",
        Ruler,
        $"TCF Mod Manager 1.28.0-beta started {started}",
        "OS Microsoft Windows NT 10.0.26100.0 / .NET 9.0.10 / x64",
        @"Data folder: G:\SPT\TCFModManager\Data",
        "Log level: Info (create a file named \"verbose\" here for Debug)",
        Ruler,
    ];

    private void Write(DateTime day, params IEnumerable<string>[] parts) =>
        File.WriteAllText(
            Path.Combine(_dir, $"tcfmm-{day:yyyyMMdd}.log"),
            string.Join("\r\n", parts.SelectMany(p => p)) + "\r\n");

    [Fact]
    public void Only_the_last_session_in_the_file_is_read()
    {
        Write(Today, Header("2026-11-11 09:00:00"), ["INFO first session"], Header("2026-11-11 20:00:00"), ["INFO second session"]);

        var log = ProblemLog.ReadLatestSession(_dir, Today);

        Assert.Equal(
            string.Join('\n', Header("2026-11-11 20:00:00").Skip(1).Append("INFO second session")),
            log);
    }

    [Fact]
    public void A_session_that_ran_past_midnight_starts_in_the_previous_file()
    {
        Write(Today.AddDays(-1), Header("2026-11-10 08:00:00"), ["INFO old"], Header("2026-11-10 23:00:00"), ["INFO before midnight"]);
        Write(Today, ["INFO after midnight"]);

        var log = ProblemLog.ReadLatestSession(_dir, Today)!;

        Assert.StartsWith(Ruler + "\nTCF Mod Manager 1.28.0-beta started 2026-11-10 23:00:00\n", log);
        Assert.EndsWith(Ruler + "\nINFO before midnight\nINFO after midnight", log);
        Assert.DoesNotContain("INFO old", log);
    }

    [Fact]
    public void A_session_left_running_for_days_goes_back_to_its_header()
    {
        Write(Today.AddDays(-3), Header("2026-11-08 12:00:00"), ["INFO day one"]);
        Write(Today.AddDays(-2), ["INFO day two"]);
        Write(Today, ["INFO day four"]);

        var log = ProblemLog.ReadLatestSession(_dir, Today)!;

        Assert.Contains("started 2026-11-08 12:00:00", log);
        Assert.EndsWith("INFO day one\nINFO day two\nINFO day four", log);
    }

    [Fact]
    public void Files_dated_after_now_and_other_files_are_ignored()
    {
        Write(Today, Header("2026-11-11 20:00:00"), ["INFO today"]);
        Write(Today.AddDays(1), Header("2026-11-12 08:00:00"), ["INFO tomorrow"]);
        File.WriteAllText(Path.Combine(_dir, "tcfmm-notadate.log"), "INFO junk");
        File.WriteAllText(Path.Combine(_dir, "verbose"), "");

        var log = ProblemLog.ReadLatestSession(_dir, Today)!;

        Assert.EndsWith("INFO today", log);
        Assert.DoesNotContain("tomorrow", log);
        Assert.DoesNotContain("junk", log);
    }

    [Fact]
    public void A_long_session_keeps_its_header_and_the_newest_lines()
    {
        var lines = Enumerable.Range(1, 100).Select(i => $"INFO line {i:D3}").ToArray();
        Write(Today, Header("2026-11-11 20:00:00"), lines);

        var log = ProblemLog.ReadLatestSession(_dir, Today, maxLines: 20)!.Split('\n');

        Assert.Equal(20, log.Length);
        Assert.Equal(Header("2026-11-11 20:00:00").Skip(1), log.Take(6));
        Assert.Equal("... 87 lines left out ...", log[6]);
        Assert.Equal(lines[^13..], log[7..]);
    }

    [Fact]
    public void A_session_within_the_cap_is_left_whole()
    {
        Write(Today, Header("2026-11-11 20:00:00"), ["INFO a", "INFO b"]);

        var log = ProblemLog.ReadLatestSession(_dir, Today, maxLines: 8)!;

        Assert.DoesNotContain("left out", log);
        Assert.EndsWith("INFO a\nINFO b", log);
    }

    [Fact]
    public void A_file_still_open_for_writing_can_be_read()
    {
        var file = Path.Combine(_dir, $"tcfmm-{Today:yyyyMMdd}.log");
        using var writer = new StreamWriter(new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
        writer.Write(string.Join("\r\n", Header("2026-11-11 20:00:00").Append("INFO live")) + "\r\n");
        writer.Flush();

        var log = ProblemLog.ReadLatestSession(_dir, Today);

        Assert.EndsWith("INFO live", log);
    }

    [Fact]
    public void No_folder_or_no_files_means_no_log()
    {
        Assert.Null(ProblemLog.ReadLatestSession(Path.Combine(_dir, "missing"), Today));
        Assert.Null(ProblemLog.ReadLatestSession(_dir, Today));

        Write(Today, ["", "  "]);
        Assert.Null(ProblemLog.ReadLatestSession(_dir, Today));
    }
}
