using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

public sealed record ServerTrialOutcome(bool Started, bool TimedOut, IReadOnlyList<LogEntry> Errors);

public enum ServerTrialProblem
{
    AlreadyRunning,
    PortTaken,
    CouldNotStart,
    CouldNotStop,
}

public sealed class ServerTrialException(ServerTrialProblem problem, string? detail = null)
    : Exception(detail ?? problem.ToString())
{
    public ServerTrialProblem Problem { get; } = problem;
}

//
// OPEN-54 N27: one hidden start of the SPT server - watched until it listens on its port or its
// process ends, then stopped - and the errors it logged meanwhile, read from where its log files
// ended before the start.
//
public static partial class ServerTrial
{
    public static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan SettleAfterStart = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan ProcessGrace = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);

    public static async Task<ServerTrialOutcome> RunAsync(string installPath, CancellationToken ct = default, bool settle = true)
    {
        var info = SptLaunchService.Describe(installPath, SptLaunchTarget.Server);
        if (info.IsRunning) throw new ServerTrialException(ServerTrialProblem.AlreadyRunning);

        var ports = SptServerReadiness.PortsFor(info.ExePath).Except(Listening()).ToHashSet();
        if (ports.Count == 0) throw new ServerTrialException(ServerTrialProblem.PortTaken);

        var before = LogLengths(installPath);

        var launch = SptLaunchService.Launch(installPath, SptLaunchTarget.Server, hidden: true);
        if (!launch.Started)
            throw new ServerTrialException(ServerTrialProblem.CouldNotStart, launch.Error?.Message ?? launch.Problem.ToString());

        var started = false;
        var timedOut = false;
        try
        {
            var launched = DateTime.UtcNow;
            var seen = false;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (SptServerReadiness.IsListening(ports))
                {
                    started = true;
                    break;
                }

                var running = SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning;
                seen |= running;

                if (!running && (seen || DateTime.UtcNow - launched > ProcessGrace)) break;

                if (DateTime.UtcNow - launched > StartTimeout)
                {
                    timedOut = true;
                    break;
                }

                await Task.Delay(Poll, ct).ConfigureAwait(false);
            }

            if (started && settle) await Task.Delay(SettleAfterStart, ct).ConfigureAwait(false);
        }
        finally
        {
            await StopAsync(installPath).ConfigureAwait(false);
        }

        return new ServerTrialOutcome(started, timedOut, ErrorsSince(installPath, before));
    }

    public static async Task StopAsync(string installPath)
    {
        if (!SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning) return;

        await Task.Run(() => SptLaunchService.Stop(installPath, SptLaunchTarget.Server)).ConfigureAwait(false);

        var until = DateTime.UtcNow + ProcessGrace;
        while (SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning)
        {
            if (DateTime.UtcNow > until) throw new ServerTrialException(ServerTrialProblem.CouldNotStop);
            await Task.Delay(Poll).ConfigureAwait(false);
        }
    }

    private static HashSet<int> Listening()
    {
        try
        {
            return [.. IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port)];
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    public static Dictionary<string, long> LogLengths(string installPath)
    {
        var lengths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in LogFilesOf(installPath)) lengths[file.FullName] = file.Length;
        return lengths;
    }

    public static IReadOnlyList<LogEntry> ErrorsSince(string installPath, IReadOnlyDictionary<string, long> before)
    {
        var errors = new List<LogEntry>();

        foreach (var file in LogFilesOf(installPath).OrderBy(f => f.LastWriteTimeUtc))
        {
            var from = before.GetValueOrDefault(file.FullName);
            if (file.Length < from) from = 0;
            if (file.Length == from) continue;

            errors.AddRange(LogEntries.Parse(LogKind.Server, file.FullName, ReadFrom(file.FullName, from)).Where(e => e.IsError));
        }

        return errors;
    }

    //
    // Takes what was logged since LogLengths back out - each file cut back to its old length, a new
    // file deleted - so the search's starts aren't the server's "last run" to Diagnose. Only with
    // the server stopped.
    //
    public static void TrimLogs(string installPath, IReadOnlyDictionary<string, long> before)
    {
        if (SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning) return;

        foreach (var file in LogFilesOf(installPath))
        {
            try
            {
                if (!before.TryGetValue(file.FullName, out var length))
                {
                    file.Delete();
                }
                else if (file.Length > length)
                {
                    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Write, FileShare.Read);
                    stream.SetLength(length);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Search", $"couldn't take the search's lines out of {file.Name}: {ex.Message}");
            }
        }
    }

    private static List<FileInfo> LogFilesOf(string installPath)
    {
        if (ServerLogs.Folder(installPath) is not { } folder) return [];

        try
        {
            return [.. new DirectoryInfo(folder)
                .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
                    || f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Name.StartsWith("launcher", StringComparison.OrdinalIgnoreCase))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<string> ReadFrom(string path, long from)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(from, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(StripAnsi(line));
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Search", $"couldn't read {path}: {ex.Message}");
            return [];
        }
    }

    private static string StripAnsi(string line) => line.Contains('\u001b') ? Ansi().Replace(line, "") : line;

    [GeneratedRegex("\u001b\\[[0-9;]*[A-Za-z]")]
    private static partial Regex Ansi();

    // Two log lines are the same error when their source and their text, numbers aside, match.
    public static string Signature(LogEntry entry)
    {
        var text = Digits().Replace(entry.Message, "#");
        return entry.Source + "|" + (text.Length > 300 ? text[..300] : text);
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
