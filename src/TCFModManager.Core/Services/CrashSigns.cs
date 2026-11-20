using System.Globalization;

namespace TCFModManager.Core.Services;

public enum WatchedProcessKind
{
    Server,
    Game,
    Headless,
}

public enum CrashCause
{
    WindowsReported,
    ExitCode,
    ServerGaveUp,
}

public sealed record WindowsCrash(
    string App,
    string FaultingModule,
    string ExceptionCode,
    int? ProcessId,
    DateTime? ProcessStartedUtc,
    string? AppPath,
    string? ModulePath,
    DateTime At);

public sealed record DotNetCrash(string App, string? Exception, IReadOnlyList<string> Frames, DateTime At)
{
    public string? FirstLine => Exception?.Split('\n')[0].Trim();
}

public sealed record ProcessCrash(
    WatchedProcessKind Kind,
    int ExitCode,
    CrashCause Cause,
    string? What,
    string? Module,
    string? ExceptionCode,
    DiagnosedMod? Mod);

// OPEN-54 N30.
public static class CrashSigns
{
    public static readonly IReadOnlySet<uint> CrashExitCodes = new HashSet<uint> { 0xC0000005, 0xC0000409, 0xE0434352 };

    public static string Hex(int exitCode) => "0x" + unchecked((uint)exitCode).ToString("X8", CultureInfo.InvariantCulture);

    // Application Error, event 1000: the record's data fields, in order.
    public static WindowsCrash? FromApplicationError(IReadOnlyList<string?> data, DateTime atUtc)
    {
        if (data.Count < 7 || string.IsNullOrWhiteSpace(data[0])) return null;

        return new WindowsCrash(
            App: data[0]!.Trim(),
            FaultingModule: (data[3] ?? string.Empty).Trim(),
            ExceptionCode: "0x" + StripHex(data[6]).ToUpperInvariant(),
            ProcessId: data.Count > 8 && ParseHex(data[8]) is { } pid and <= int.MaxValue ? (int)pid : null,
            ProcessStartedUtc: data.Count > 9 && ParseHex(data[9]) is { } started and > 0 ? FromFileTime(started) : null,
            AppPath: data.Count > 10 ? NullIfBlank(data[10]) : null,
            ModulePath: data.Count > 11 ? NullIfBlank(data[11]) : null,
            At: atUtc);
    }

    // .NET Runtime, event 1026: "Application: X.exe", "Exception Info: Type: message", then "   at ..." frames.
    public static DotNetCrash? FromDotNetRuntime(string? text, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        string? app = null, exception = null;
        var frames = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("Application:", StringComparison.Ordinal)) app ??= line["Application:".Length..].Trim();
            else if (line.StartsWith("Exception Info:", StringComparison.Ordinal)) exception ??= line["Exception Info:".Length..].Trim();
            else if (line.StartsWith("at ", StringComparison.Ordinal)) frames.Add(line[3..].Trim());
        }

        return app is null ? null : new DotNetCrash(app, exception, frames, atUtc);
    }

    // The type a frame ran in: "A.B.C.<>c__DisplayClass16_0.<<M>b__0>d.MoveNext()" -> "A.B.C.<>c__DisplayClass16_0.<<M>b__0>d.MoveNext".
    public static string FrameName(string frame)
    {
        var paren = frame.IndexOf('(');
        var name = paren >= 0 ? frame[..paren] : frame;
        var space = name.IndexOf(' ');
        return space >= 0 ? name[..space] : name;
    }

    public static bool IsPlatformFrame(string frame)
    {
        var name = FrameName(frame);
        return name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name.StartsWith("SPTarkov.", StringComparison.Ordinal)
            || name.StartsWith("Internal.", StringComparison.Ordinal);
    }

    public static bool Matches(WindowsCrash crash, int processId, DateTime startedUtc, string exePath)
    {
        if (!string.Equals(crash.App, ExeName(exePath), StringComparison.OrdinalIgnoreCase)) return false;
        if (crash.ProcessId is { } pid && pid != processId) return false;
        if (crash.ProcessStartedUtc is { } started && Math.Abs((started - startedUtc).TotalSeconds) > 2) return false;
        if (crash.ProcessId is null && crash.AppPath is { } path
            && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase)) return false;
        return crash.At >= startedUtc.AddSeconds(-2);
    }

    public static bool Matches(DotNetCrash crash, string exePath, DateTime nearUtc) =>
        string.Equals(crash.App, ExeName(exePath), StringComparison.OrdinalIgnoreCase)
        && Math.Abs((crash.At - nearUtc).TotalSeconds) <= 10;

    //
    // stoppedByApp wins over everything. Then Windows' own record, then the server's last words, then
    // the exit code. Null when nothing says it crashed.
    //
    public static ProcessCrash? Decide(
        WatchedProcessKind kind,
        int exitCode,
        bool stoppedByApp,
        WindowsCrash? windows,
        DotNetCrash? dotnet,
        LogFinding? serverGaveUp,
        LogModLocator? mods)
    {
        if (stoppedByApp) return null;

        if (windows is not null || dotnet is not null)
        {
            var frameMod = dotnet is null || mods is null
                ? null
                : dotnet.Frames.Where(f => !IsPlatformFrame(f)).Select(f => mods.ByCode(FrameName(f))).FirstOrDefault(m => m is not null);
            var moduleMod = frameMod ?? ModHolding(windows?.ModulePath, mods);

            return new ProcessCrash(kind, exitCode, CrashCause.WindowsReported, dotnet?.FirstLine,
                windows?.FaultingModule, windows?.ExceptionCode, moduleMod);
        }

        if (kind == WatchedProcessKind.Server && serverGaveUp is not null)
            return new ProcessCrash(kind, exitCode, CrashCause.ServerGaveUp, serverGaveUp.Args.Count > 1 ? serverGaveUp.Args[1] : null,
                null, null, serverGaveUp.Mod);

        return CrashExitCodes.Contains(unchecked((uint)exitCode))
            ? new ProcessCrash(kind, exitCode, CrashCause.ExitCode, null, null, Hex(exitCode), null)
            : null;
    }

    private static DiagnosedMod? ModHolding(string? path, LogModLocator? mods)
    {
        if (path is null || mods is null) return null;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return mods.All
            .Where(m => !string.IsNullOrEmpty(m.FolderPath)
                && full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(m.FolderPath)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.FolderPath.Length)
            .FirstOrDefault();
    }

    private static string ExeName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    private static string StripHex(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        return v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? v[2..] : v;
    }

    private static ulong? ParseHex(string? value) =>
        ulong.TryParse(StripHex(value), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static DateTime? FromFileTime(ulong value)
    {
        try
        {
            return DateTime.FromFileTimeUtc((long)value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
