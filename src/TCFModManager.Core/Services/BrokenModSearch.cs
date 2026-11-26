using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public enum BrokenModSymptom
{
    ServerDoesNotStart,
    ServerLogsErrors,
}

public enum BrokenModVerdict
{
    NothingWrong,

    NotTheMods,

    Found,
}

public sealed record BrokenModResult(
    BrokenModVerdict Verdict,
    BrokenModSymptom? Symptom,
    int? Culprit,
    int? Partner,
    IReadOnlyList<LogEntry> Errors,
    int Tries,
    int RecentTried = 0);

public sealed record BrokenModProgress(int Try, int About, int ModsOn, int Mods, IReadOnlyList<int>? Suspects = null);

//
// OPEN-54 N27: Find the broken mod, for the SPT server. Tries every server mod on (what is wrong),
// then none (is it the mods), then halves with ModBisect; a try fails when it shows what the first
// one did. Mods changed since the server last came up are tried first, for one extra start.
//
public static class BrokenModSearch
{
    public static async Task<BrokenModResult> RunAsync(
        int mods,
        Func<int, IEnumerable<int>> needs,
        Func<IReadOnlyList<int>, bool, CancellationToken, Task<ServerTrialOutcome>> tryWith,
        IProgress<BrokenModProgress>? progress = null,
        CancellationToken ct = default,
        IReadOnlyCollection<int>? recent = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mods, 1);

        var tries = 0;

        async Task<ServerTrialOutcome> Try(IReadOnlyList<int> on, int left, bool errors, IReadOnlyList<int>? suspects = null)
        {
            ct.ThrowIfCancellationRequested();
            tries++;
            progress?.Report(new BrokenModProgress(tries, tries + left, on.Count, mods, suspects));
            return await tryWith(on, errors, ct).ConfigureAwait(false);
        }

        var all = Enumerable.Range(0, mods).ToList();
        var search = new ModBisect(mods, needs);

        var first = await Try(all, 1 + search.TriesLeft, errors: true).ConfigureAwait(false);
        if (first.Started && first.Errors.Count == 0)
            return new BrokenModResult(BrokenModVerdict.NothingWrong, null, null, null, [], tries);

        var symptom = first.Started ? BrokenModSymptom.ServerLogsErrors : BrokenModSymptom.ServerDoesNotStart;

        var errorsMatter = symptom == BrokenModSymptom.ServerLogsErrors;

        var none = await Try([], search.TriesLeft, errorsMatter).ConfigureAwait(false);
        var theirs = first.Errors.Select(ServerTrial.Signature)
            .Except(none.Errors.Select(ServerTrial.Signature), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var stillWrong = symptom == BrokenModSymptom.ServerDoesNotStart ? !none.Started : !none.Started || theirs.Count == 0;
        if (stillWrong)
            return new BrokenModResult(BrokenModVerdict.NotTheMods, symptom, null, null, none.Errors, tries);

        IReadOnlyList<LogEntry> Shown(ServerTrialOutcome outcome) =>
            symptom == BrokenModSymptom.ServerDoesNotStart
                ? outcome.Errors
                : [.. outcome.Errors.Where(e => theirs.Contains(ServerTrial.Signature(e)))];

        bool Fails(ServerTrialOutcome outcome) => !outcome.Started || (symptom == BrokenModSymptom.ServerLogsErrors && Shown(outcome).Count > 0);

        var errors = Shown(first);
        var fewest = mods;

        List<int>? among = null;
        List<int> rest = [];

        if (recent is { Count: > 0 } && WithDependents(mods, needs, recent) is { Count: > 0 } changed && changed.Count * 2 <= mods)
        {
            var others = all.Except(changed).ToList();
            var narrow = new ModBisect(changed.Count, i => needs(changed[i]).Select(n => changed.IndexOf(n)).Where(n => n >= 0));

            var without = await Try(others, 1 + narrow.TriesLeft, errorsMatter).ConfigureAwait(false);
            if (!Fails(without))
            {
                among = changed;
                rest = others;
                search = narrow;
            }
        }

        int Mod(int number) => among is null ? number : among[number];

        while (search.Next is { } next)
        {
            IReadOnlyList<int> on = among is null ? next : [.. rest.Concat(next.Select(Mod)).Order()];
            IReadOnlyList<int> suspects = [.. search.Suspects.Select(Mod)];

            var outcome = await Try(on, search.TriesLeft - 1, errorsMatter, suspects).ConfigureAwait(false);
            var failed = Fails(outcome);

            if (failed && on.Count <= fewest)
            {
                fewest = on.Count;
                errors = Shown(outcome);
            }

            search.Report(failed);
        }

        return new BrokenModResult(
            BrokenModVerdict.Found, symptom,
            Mod(search.Culprit!.Value), search.Partner is { } partner ? Mod(partner) : null,
            errors, tries, among?.Count ?? 0);
    }

    private static List<int> WithDependents(int mods, Func<int, IEnumerable<int>> needs, IReadOnlyCollection<int> picked)
    {
        var chosen = new bool[mods];
        foreach (var mod in picked)
            if (mod >= 0 && mod < mods) chosen[mod] = true;

        for (var i = 0; i < mods; i++)
            if (!chosen[i] && needs(i).Any(n => n >= 0 && n < mods && chosen[n])) chosen[i] = true;

        return [.. Enumerable.Range(0, mods).Where(i => chosen[i])];
    }

    // Which of these mod folders, or anything directly in them, were made or written after a moment.
    public static List<int> ChangedSince(IReadOnlyList<string> folders, DateTimeOffset since)
    {
        var moment = since.UtcDateTime;
        var changed = new List<int>();

        for (var i = 0; i < folders.Count; i++)
        {
            try
            {
                FileSystemInfo entry = Directory.Exists(folders[i]) ? new DirectoryInfo(folders[i]) : new FileInfo(folders[i]);
                if (!entry.Exists) continue;

                var inside = entry is DirectoryInfo folder ? folder.EnumerateFileSystemInfos() : [];
                if (inside.Prepend(entry).Any(e => e.CreationTimeUtc > moment || e.LastWriteTimeUtc > moment)) changed.Add(i);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return changed;
    }

    // About how long is left. One start done: each start takes as long as the first full one. More:
    // a fixed part plus a part per mod on, fitted to every start so far, for the mods on now.
    public static TimeSpan? TimeLeft(IReadOnlyList<(int ModsOn, TimeSpan Took)> finished, int modsOnNow, int triesLeft, TimeSpan intoCurrent)
    {
        if (finished.Count == 0 || triesLeft <= 0) return null;

        var each = TimeSpan.FromSeconds(StartSeconds(finished, modsOnNow));
        var left = each * triesLeft - intoCurrent;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private static double StartSeconds(IReadOnlyList<(int ModsOn, TimeSpan Took)> finished, int modsOn)
    {
        var meanMods = finished.Average(f => (double)f.ModsOn);
        var meanSeconds = finished.Average(f => f.Took.TotalSeconds);
        var spread = finished.Sum(f => (f.ModsOn - meanMods) * (f.ModsOn - meanMods));
        if (spread == 0) return meanSeconds;

        var perMod = Math.Max(0d, finished.Sum(f => (f.ModsOn - meanMods) * (f.Took.TotalSeconds - meanSeconds)) / spread);
        var fixedPart = Math.Max(0d, meanSeconds - perMod * meanMods);
        return fixedPart + perMod * modsOn;
    }

    // The install's enabled server mods, each after what it needs; a mod installed twice is left out.
    public static (IReadOnlyList<InstalledMod> Mods, Func<int, IEnumerable<int>> Needs) Suspects(IReadOnlyList<InstalledMod> scanned)
    {
        var twice = ModDisableService.DuplicatePairs(scanned).Select(p => p.Enabled).ToHashSet();
        var graph = ModDependencyGraph.Build(scanned);

        var enabled = scanned.Where(m => m.Target == InstalledModTarget.Server && !m.IsDisabled && !twice.Contains(m)).ToList();
        var ordered = graph.DependencyOrder(enabled, m => [m]);

        var number = new Dictionary<InstalledMod, int>();
        for (var i = 0; i < ordered.Count; i++) number[ordered[i]] = i;

        return (ordered, i => graph.DependenciesOf(ordered[i])
            .Where(link => !link.IsSoft && number.ContainsKey(link.Dependency))
            .Select(link => number[link.Dependency]));
    }
}
