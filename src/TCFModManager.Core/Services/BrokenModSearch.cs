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

/// <summary>What a start is for: the first two settle what is wrong and whether it is the mods,
/// the rest narrow it down to one mod, try it on its own, then look for the other of a pair.</summary>
public enum BrokenModStage
{
    AllOn,
    AllOff,
    RecentOff,
    Narrowing,
    Alone,
    Partner,
}

/// <summary>A finished start: what it was for, how many mods were on, and whether the problem showed.</summary>
public sealed record BrokenModStep(int Try, BrokenModStage Stage, int ModsOn, int Mods, bool ShowedProblem);

public sealed record BrokenModProgress(int Try, int About, int ModsOn, int Mods, IReadOnlyList<int>? Suspects = null)
{
    public IReadOnlyList<int> On { get; init; } = [];

    public BrokenModStage Stage { get; init; }

    /// <summary>About which mods the starts after this one will have on (ModBisect.Ahead).</summary>
    public IReadOnlyList<IReadOnlyList<int>> Ahead { get; init; } = [];
}

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
        IReadOnlyCollection<int>? recent = null,
        IProgress<BrokenModStep>? steps = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mods, 1);

        var tries = 0;

        async Task<ServerTrialOutcome> Try(BrokenModStage stage, IReadOnlyList<int> on, IReadOnlyList<IReadOnlyList<int>> ahead, bool errors, IReadOnlyList<int>? suspects = null)
        {
            ct.ThrowIfCancellationRequested();
            tries++;
            progress?.Report(new BrokenModProgress(tries, tries + ahead.Count, on.Count, mods, suspects) { On = on, Ahead = ahead, Stage = stage });
            return await tryWith(on, errors, ct).ConfigureAwait(false);
        }

        void Done(BrokenModStage stage, IReadOnlyList<int> on, bool showed) =>
            steps?.Report(new BrokenModStep(tries, stage, on.Count, mods, showed));

        var all = Enumerable.Range(0, mods).ToList();
        var search = new ModBisect(mods, needs);

        static IReadOnlyList<IReadOnlyList<int>> From(ModBisect bisect, Func<IReadOnlyList<int>, IReadOnlyList<int>> map) =>
            bisect.Next is { } next ? [map(next), .. bisect.Ahead.Select(map)] : [];

        var first = await Try(BrokenModStage.AllOn, all, [[], .. From(search, s => s)], errors: true).ConfigureAwait(false);
        Done(BrokenModStage.AllOn, all, !first.Started || first.Errors.Count > 0);
        if (first.Started && first.Errors.Count == 0)
            return new BrokenModResult(BrokenModVerdict.NothingWrong, null, null, null, [], tries);

        var symptom = first.Started ? BrokenModSymptom.ServerLogsErrors : BrokenModSymptom.ServerDoesNotStart;

        var errorsMatter = symptom == BrokenModSymptom.ServerLogsErrors;

        var none = await Try(BrokenModStage.AllOff, [], From(search, s => s), errorsMatter).ConfigureAwait(false);
        var theirs = first.Errors.Select(ServerTrial.Signature)
            .Except(none.Errors.Select(ServerTrial.Signature), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var stillWrong = symptom == BrokenModSymptom.ServerDoesNotStart ? !none.Started : !none.Started || theirs.Count == 0;
        Done(BrokenModStage.AllOff, [], stillWrong);
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

            var without = await Try(BrokenModStage.RecentOff, others, From(narrow, s => [.. others.Concat(s.Select(i => changed[i])).Order()]), errorsMatter).ConfigureAwait(false);
            Done(BrokenModStage.RecentOff, others, Fails(without));
            if (!Fails(without))
            {
                among = changed;
                rest = others;
                search = narrow;
            }
        }

        int Mod(int number) => among is null ? number : among[number];
        IReadOnlyList<int> On(IReadOnlyList<int> next) => among is null ? next : [.. rest.Concat(next.Select(Mod)).Order()];

        while (search.Next is { } next)
        {
            var on = On(next);
            IReadOnlyList<int> suspects = [.. search.Suspects.Select(Mod)];

            var stage = search.Stage;
            var outcome = await Try(stage, on, [.. search.Ahead.Select(On)], errorsMatter, suspects).ConfigureAwait(false);
            var failed = Fails(outcome);
            Done(stage, on, failed);

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

    //
    // OPEN-54 N27c: the same search over client mods, where each try is a game start the user answers
    // for. broke: true when the problem showed with those mods on.
    //
    public static Task<BrokenModResult> RunClientAsync(
        int mods,
        Func<int, IEnumerable<int>> needs,
        Func<IReadOnlyList<int>, CancellationToken, Task<bool>> broke,
        IProgress<BrokenModProgress>? progress = null,
        CancellationToken ct = default,
        IReadOnlyCollection<int>? recent = null,
        IProgress<BrokenModStep>? steps = null) =>
        RunAsync(mods, needs, async (on, _, token) =>
            new ServerTrialOutcome(!await broke(on, token).ConfigureAwait(false), false, []), progress, ct, recent, steps);

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

    // Whether a server run since a mod was found went without the problem: it began after the find,
    // came up, and - when the problem was errors - logged none of the ones the search pinned on it.
    public static bool ProblemGone(IReadOnlyList<LogEntry> run, DateTime foundAt, bool wontStart, IReadOnlySet<string> signatures)
    {
        if (run.FirstOrDefault(e => e.At is not null)?.At is not { } began || began < foundAt) return false;
        if (!run.Any(ModLoadShares.IsReady)) return false;

        return wontStart || !run.Any(e => e.IsError && signatures.Contains(ServerTrial.Signature(e)));
    }

    // About how long is left: this start and each one expected after it (ModBisect.Ahead), each
    // guessed from the mods' shares of the first full start (ModLoadShares) - that start's time less
    // the shares of the mods off - and scaled by how far the guesses for the starts so far were out.
    // With no shares to go on, each is a fixed part plus a part per mod on, fitted to the starts so far.
    public static TimeSpan? TimeLeft(
        IReadOnlyList<(IReadOnlyList<int> On, TimeSpan Took)> finished,
        IReadOnlyList<TimeSpan>? shares,
        IReadOnlyList<int> onNow,
        IReadOnlyList<IReadOnlyList<int>> ahead,
        TimeSpan intoCurrent)
    {
        if (finished.Count == 0) return null;

        Func<IReadOnlyList<int>, double> each;
        if (shares is { Count: > 0 } && shares.Any(s => s > TimeSpan.Zero))
        {
            double Shares(IEnumerable<int> on) => on.Where(i => i >= 0 && i < shares.Count).Sum(i => shares[i].TotalSeconds);

            var fixedPart = Math.Max(0d, finished[0].Took.TotalSeconds - shares.Sum(s => s.TotalSeconds));
            double Guess(IEnumerable<int> on) => fixedPart + Shares(on);

            var guessed = finished.Sum(f => Guess(f.On));
            var scale = guessed > 0 ? finished.Sum(f => f.Took.TotalSeconds) / guessed : 1d;
            each = on => scale * Guess(on);
        }
        else
        {
            var points = finished.Select(f => (f.On.Count, f.Took)).ToList();
            each = on => StartSeconds(points, on.Count);
        }

        var seconds = Math.Max(0d, each(onNow) - intoCurrent.TotalSeconds) + ahead.Sum(each);
        return TimeSpan.FromSeconds(seconds);
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

    // The install's enabled server mods, each after what it needs - what it declares, and the mod it
    // is an addon for (AddonParents) - so no try has a mod on without them; a mod installed twice is
    // left out.
    public static (IReadOnlyList<InstalledMod> Mods, Func<int, IEnumerable<int>> Needs) Suspects(
        IReadOnlyList<InstalledMod> scanned, IReadOnlyList<AddonParent>? addons = null, InstalledModTarget target = InstalledModTarget.Server)
    {
        var twice = ModDisableService.DuplicatePairs(scanned).Select(p => p.Enabled).ToHashSet();
        var graph = ModDependencyGraph.Build(scanned);

        var enabled = scanned.Where(m => m.Target == target && !m.IsPatcher && !m.IsDisabled && !twice.Contains(m)).ToList();
        var included = enabled.ToHashSet();

        var parents = new Dictionary<InstalledMod, List<InstalledMod>>();
        foreach (var link in addons ?? [])
        {
            if (!included.Contains(link.Addon) || !included.Contains(link.Parent)) continue;
            if (!parents.TryGetValue(link.Addon, out var list)) parents[link.Addon] = list = [];
            list.Add(link.Parent);
        }

        IEnumerable<InstalledMod> NeedsOf(InstalledMod mod) => graph.DependenciesOf(mod)
            .Where(link => !link.IsSoft && included.Contains(link.Dependency))
            .Select(link => link.Dependency)
            .Concat(parents.GetValueOrDefault(mod) ?? []);

        var ordered = new List<InstalledMod>();
        var placed = new HashSet<InstalledMod>();
        var visiting = new HashSet<InstalledMod>();

        void Place(InstalledMod mod)
        {
            if (placed.Contains(mod) || !visiting.Add(mod)) return;
            foreach (var needed in NeedsOf(mod)) Place(needed);
            visiting.Remove(mod);
            if (placed.Add(mod)) ordered.Add(mod);
        }

        foreach (var mod in graph.DependencyOrder(enabled, m => [m])) Place(mod);

        var number = new Dictionary<InstalledMod, int>();
        for (var i = 0; i < ordered.Count; i++) number[ordered[i]] = i;

        return (ordered, i => NeedsOf(ordered[i]).Where(number.ContainsKey).Select(m => number[m]).Distinct());
    }
}
