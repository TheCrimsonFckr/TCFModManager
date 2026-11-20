using System.Diagnostics.Eventing.Reader;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-54 N30: Windows' own crash record for a process - Application Error 1000 and, for .NET,
// .NET Runtime 1026 - from the Application event log. Not there under Wine.
//
internal static class WindowsCrashLog
{
    public static (WindowsCrash? Windows, DotNetCrash? DotNet) Find(RunningSptProcess process, DateTime exitedUtc)
    {
        if (WineHost.IsWine) return (null, null);

        try
        {
            var window = (long)(DateTime.UtcNow - process.StartedUtc).TotalMilliseconds + 5000;

            WindowsCrash? windows = null;
            foreach (var record in Read("Application Error", 1000, window))
            {
                using (record)
                {
                    var data = record.Properties.Select(p => p.Value?.ToString()).ToList();
                    if (CrashSigns.FromApplicationError(data, Utc(record)) is { } crash
                        && CrashSigns.Matches(crash, process.Id, process.StartedUtc, process.ExePath))
                    {
                        windows = crash;
                    }
                }
            }

            var near = windows?.At ?? exitedUtc;
            DotNetCrash? dotnet = null;
            foreach (var record in Read(".NET Runtime", 1026, window))
            {
                using (record)
                {
                    var text = record.Properties.Count > 0 ? record.Properties[0].Value?.ToString() : null;
                    if (CrashSigns.FromDotNetRuntime(text, Utc(record)) is { } crash && CrashSigns.Matches(crash, process.ExePath, near))
                        dotnet = crash;
                }
            }

            return (windows, dotnet);
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException)
        {
            AppLog.Debug("Crash", $"couldn't read the Application event log: {ex.GetType().Name}: {ex.Message}");
            return (null, null);
        }
    }

    private static DateTime Utc(EventRecord record) => record.TimeCreated?.ToUniversalTime() ?? DateTime.MinValue;

    private static IEnumerable<EventRecord> Read(string provider, int id, long withinMs)
    {
        var query = new EventLogQuery("Application", PathType.LogName,
            $"*[System[Provider[@Name='{provider}'] and (EventID={id}) and TimeCreated[timediff(@SystemTime) <= {withinMs}]]]");

        using var reader = new EventLogReader(query);
        while (reader.ReadEvent() is { } record) yield return record;
    }
}
