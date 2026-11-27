namespace TCFModManager.Core.Services;

//
// OPEN-54 N27: about how much of a server start each mod takes, read from the start's log. SPT
// doesn't time its mods, so each gap between two lines is charged to the mod that wrote the second
// one; gaps before SPT's own lines, and before mods that write nothing, are left out. Rough by
// nature - BrokenModSearch.TimeLeft corrects it against how long the starts really take.
//
public static class ModLoadShares
{
    private const string Ready = "Started webserver";

    /// <summary>The line SPT writes once the server is up and listening.</summary>
    public static bool IsReady(LogEntry line) => line.Message.StartsWith(Ready, StringComparison.Ordinal);

    public static TimeSpan[] From(IReadOnlyList<LogEntry>? lines, Func<string, int?> modOf, int mods)
    {
        var shares = new TimeSpan[mods];
        if (lines is null) return shares;

        DateTime? previous = null;
        foreach (var line in lines)
        {
            if (line.At is not { } at) continue;

            if (previous is { } before && at > before && modOf(line.Source) is { } mod && mod >= 0 && mod < mods)
                shares[mod] += at - before;

            previous = at;
            if (IsReady(line)) break;
        }

        return shares;
    }
}
