using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-54 N27: ModBisect, BrokenModSearch, ServerTrial's log reading and BrokenModSessionStore.
public class BrokenModSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modsearch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // Runs a search to its end against a rule for what fails; returns it and how many tries it took.
    private static (ModBisect Search, int Tries) Run(int count, Func<IReadOnlyList<int>, bool> fails, Func<int, IEnumerable<int>>? needs = null)
    {
        var search = new ModBisect(count, needs);
        var tries = 0;

        while (search.Next is { } on)
        {
            Assert.True(++tries <= 64, "the search does not end");
            search.Report(fails(on));
        }

        return (search, tries);
    }

    [Fact]
    public void One_bad_mod_is_found_wherever_it_is()
    {
        for (var count = 1; count <= 40; count++)
        {
            for (var bad = 0; bad < count; bad++)
            {
                var (search, tries) = Run(count, on => on.Contains(bad));

                Assert.Equal(bad, search.Culprit);
                Assert.Null(search.Partner);
                Assert.True(tries <= Math.Ceiling(Math.Log2(count)) + 1, $"{tries} tries for {count} mods");
            }
        }
    }

    [Fact]
    public void Two_mods_that_only_fail_together_are_both_found()
    {
        for (var count = 2; count <= 24; count++)
        {
            for (var one = 0; one < count; one++)
            {
                for (var other = one + 1; other < count; other++)
                {
                    var (search, _) = Run(count, on => on.Contains(one) && on.Contains(other));

                    Assert.Equal(other, search.Culprit);
                    Assert.Equal(one, search.Partner);
                }
            }
        }
    }

    [Fact]
    public void A_mod_is_never_tried_without_what_it_needs()
    {
        // 0 <- 1 <- 3, 2 <- 4, 5 by itself; 3 is the bad one.
        int[][] needs = [[], [0], [], [1], [2], []];

        var (search, _) = Run(6, on =>
        {
            foreach (var mod in on)
                foreach (var needed in needs[mod]) Assert.Contains(needed, on);
            return on.Contains(3);
        }, i => needs[i]);

        Assert.Equal(3, search.Culprit);
        Assert.Null(search.Partner);
    }

    [Fact]
    public void A_fault_that_comes_and_goes_still_ends_the_search()
    {
        var random = new Random(7);

        var (search, _) = Run(30, _ => random.Next(2) == 0);

        Assert.NotNull(search.Culprit);
        Assert.True(search.IsDone);
    }

    // ---- the search around it ------------------------------------------------------------------

    private static LogEntry Error(string source, string message) =>
        new(LogKind.Server, "spt.log", 1, null, "Error", source, message, []);

    private static Task<BrokenModResult> Search(int mods, Func<IReadOnlyList<int>, ServerTrialOutcome> server) =>
        BrokenModSearch.RunAsync(mods, _ => [], (on, _, _) => Task.FromResult(server(on)));

    [Fact]
    public async Task A_server_that_starts_clean_has_nothing_wrong()
    {
        var result = await Search(8, _ => new ServerTrialOutcome(true, false, []));

        Assert.Equal(BrokenModVerdict.NothingWrong, result.Verdict);
        Assert.Equal(1, result.Tries);
    }

    [Fact]
    public async Task A_server_that_stays_down_with_every_mod_off_is_not_the_mods()
    {
        var result = await Search(8, _ => new ServerTrialOutcome(false, false, [Error("Program", "port 6969 in use")]));

        Assert.Equal(BrokenModVerdict.NotTheMods, result.Verdict);
        Assert.Equal(BrokenModSymptom.ServerDoesNotStart, result.Symptom);
        Assert.Equal(2, result.Tries);
    }

    [Fact]
    public async Task The_mod_that_keeps_the_server_down_is_found()
    {
        var result = await Search(20, on => on.Contains(13)
            ? new ServerTrialOutcome(false, false, [Error("ModLoader", "BadMod could not be loaded")])
            : new ServerTrialOutcome(true, false, []));

        Assert.Equal(BrokenModVerdict.Found, result.Verdict);
        Assert.Equal(BrokenModSymptom.ServerDoesNotStart, result.Symptom);
        Assert.Equal(13, result.Culprit);
        Assert.Equal("BadMod could not be loaded", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task An_error_logged_with_every_mod_off_is_not_looked_for()
    {
        // The profile's error is there whatever is on, and turning mods off adds one of its own;
        // mod 5 is the one logging the error that started it.
        var result = await Search(12, on =>
        {
            var errors = new List<LogEntry> { Error("Profiles", "profile 12 has a problem") };
            if (on.Count < 12) errors.Add(Error("Profiles", $"item {on.Count} is from a mod that is off"));
            if (on.Contains(5)) errors.Add(Error("BadMod", "null reference in 3 places"));
            return new ServerTrialOutcome(true, false, errors);
        });

        Assert.Equal(BrokenModVerdict.Found, result.Verdict);
        Assert.Equal(BrokenModSymptom.ServerLogsErrors, result.Symptom);
        Assert.Equal(5, result.Culprit);
        Assert.Equal("BadMod", Assert.Single(result.Errors).Source);
    }

    [Fact]
    public async Task Errors_that_stay_with_every_mod_off_are_not_the_mods()
    {
        var result = await Search(4, _ => new ServerTrialOutcome(true, false, [Error("Profiles", "profile 1 has a problem")]));

        Assert.Equal(BrokenModVerdict.NotTheMods, result.Verdict);
    }

    [Fact]
    public async Task Progress_counts_every_try()
    {
        var seen = new List<BrokenModProgress>();
        var progress = new Collect(seen);

        var result = await BrokenModSearch.RunAsync(16, _ => [],
            (on, _, _) => Task.FromResult(new ServerTrialOutcome(!on.Contains(9), false, [])), progress);

        Assert.Equal(result.Tries, seen.Count);
        Assert.Equal([.. Enumerable.Range(1, seen.Count)], seen.Select(p => p.Try));
        Assert.All(seen, p => Assert.True(p.About >= p.Try));
        Assert.Equal(16, seen[0].ModsOn);
        Assert.Equal(0, seen[1].ModsOn);
    }

    [Fact]
    public async Task Errors_are_waited_for_only_when_they_are_what_is_wrong()
    {
        // The server does not start with mod 5 on: after the first try, coming up is the answer.
        var asked = new List<bool>();
        await BrokenModSearch.RunAsync(8, _ => [], (on, errors, _) =>
        {
            asked.Add(errors);
            return Task.FromResult(new ServerTrialOutcome(!on.Contains(5), false, []));
        });

        Assert.True(asked[0]);
        Assert.All(asked.Skip(1), Assert.False);

        // It starts and mod 5 logs an error: every try waits for the errors.
        asked.Clear();
        var error = new LogEntry(LogKind.Server, "spt.log", 1, null, "Error", "Five", "broke", []);
        await BrokenModSearch.RunAsync(8, _ => [], (on, errors, _) =>
        {
            asked.Add(errors);
            return Task.FromResult(new ServerTrialOutcome(true, false, on.Contains(5) ? [error] : []));
        });

        Assert.All(asked, Assert.True);
    }

    [Fact]
    public void A_bisect_says_who_is_still_suspected()
    {
        var search = new ModBisect(8);
        Assert.Equal(8, search.Suspects.Count);

        while (search.Next is { } on)
        {
            Assert.Contains(5, search.Suspects);
            search.Report(on.Contains(5));
        }

        Assert.Equal([5], search.Suspects);
    }

    [Fact]
    public async Task Recently_changed_mods_are_tried_first_and_the_rest_stay_on()
    {
        // Sixteen mods; 11 is the bad one, and 3 and 11 changed. 12 needs 11.
        IEnumerable<int> Needs(int mod) => mod == 12 ? [11] : [];
        var asked = new List<IReadOnlyList<int>>();
        Task<ServerTrialOutcome> Server(IReadOnlyList<int> on, bool errors, CancellationToken ct)
        {
            asked.Add(on);
            return Task.FromResult(new ServerTrialOutcome(!on.Contains(11), false, []));
        }

        var plain = await BrokenModSearch.RunAsync(16, Needs, Server);
        asked.Clear();
        var result = await BrokenModSearch.RunAsync(16, Needs, Server, recent: [3, 11]);

        Assert.Equal(BrokenModVerdict.Found, result.Verdict);
        Assert.Equal(11, result.Culprit);
        Assert.Null(result.Partner);
        Assert.Equal(3, result.RecentTried); // 3, 11 and the one that needs 11
        Assert.True(result.Tries < plain.Tries);

        // The try without them names no suspects: nothing is narrowed until it has passed.
        var said = new List<BrokenModProgress>();
        var first = asked.ToList();
        await BrokenModSearch.RunAsync(16, Needs, Server, new Collect(said), recent: [3, 11]);
        asked.Clear();
        asked.AddRange(first);
        Assert.Null(said[2].Suspects);
        Assert.Equal([3, 11, 12], said[3].Suspects!.Order());

        // The third try is everything but those three; after it the other thirteen are always on.
        Assert.Equal(13, asked[2].Count);
        Assert.All(asked.Skip(3), on => Assert.Equal(13, on.Except([3, 11, 12]).Count()));
        // And 12 is never on without 11.
        Assert.All(asked, on => Assert.True(!on.Contains(12) || on.Contains(11)));
    }

    [Fact]
    public async Task A_wrong_guess_at_the_recent_mods_costs_one_start()
    {
        Task<ServerTrialOutcome> Server(IReadOnlyList<int> on, bool errors, CancellationToken ct) =>
            Task.FromResult(new ServerTrialOutcome(!on.Contains(11), false, []));

        var plain = await BrokenModSearch.RunAsync(16, _ => [], Server);
        var result = await BrokenModSearch.RunAsync(16, _ => [], Server, recent: [2, 3]);

        Assert.Equal(11, result.Culprit);
        Assert.Equal(0, result.RecentTried);
        Assert.Equal(plain.Tries + 1, result.Tries);

        // Too many changed to be worth it: as if none were given.
        var most = await BrokenModSearch.RunAsync(16, _ => [], Server, recent: [.. Enumerable.Range(0, 9)]);
        Assert.Equal(plain.Tries, most.Tries);
    }

    [Fact]
    public async Task A_pair_among_the_recent_mods_is_found()
    {
        Task<ServerTrialOutcome> Server(IReadOnlyList<int> on, bool errors, CancellationToken ct) =>
            Task.FromResult(new ServerTrialOutcome(!(on.Contains(4) && on.Contains(9)), false, []));

        var result = await BrokenModSearch.RunAsync(16, _ => [], Server, recent: [4, 9, 13]);

        Assert.Equal(9, result.Culprit);
        Assert.Equal(4, result.Partner);
        Assert.Equal(3, result.RecentTried);
    }

    [Fact]
    public void Changed_mods_are_told_by_their_folder_and_what_is_in_it()
    {
        var old = Path.Combine(_dir, "Old");
        var touched = Path.Combine(_dir, "Touched");
        var added = Path.Combine(_dir, "Added");
        foreach (var folder in new[] { old, touched })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "mod.dll"), "x");
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos().Prepend(new DirectoryInfo(folder)))
            {
                entry.CreationTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                entry.LastWriteTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }
        }

        var since = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(Path.Combine(touched, "mod.dll"), new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        Directory.CreateDirectory(added); // made now

        Assert.Equal([1, 2], BrokenModSearch.ChangedSince([old, touched, added, Path.Combine(_dir, "Gone")], since));
    }

    [Fact]
    public void When_a_server_was_last_up_is_kept_by_install()
    {
        var file = Path.Combine(_dir, "serverLastUp.json");
        var at = new DateTimeOffset(2026, 11, 11, 20, 0, 0, TimeSpan.Zero);

        Assert.Null(new ServerUpLog(file).Last(@"C:\SPT"));

        new ServerUpLog(file).Note(@"C:\SPT", at);
        new ServerUpLog(file).Note(@"D:\Other", at.AddHours(1));

        Assert.Equal(at, new ServerUpLog(file).Last(@"c:\spt"));
        Assert.Equal(at.AddHours(1), new ServerUpLog(file).Last(@"D:\Other"));
    }

    // As on G: 2026-11-26, cut down: DWV and SVM write after their own work, SPT's lines are its own.
    [Fact]
    public void Each_gap_in_the_log_goes_to_the_mod_that_wrote_the_next_line()
    {
        var t = new DateTime(2026, 11, 26, 16, 14, 30);
        LogEntry Line(double seconds, string source, string message = "x") =>
            new(LogKind.Server, "spt.log", 1, t.AddSeconds(seconds), "Information", source, message, []);

        var lines = new[]
        {
            Line(0, "SPTarkov.Server.Program"),
            Line(2, "SPTarkov.Server.Modding.ModValidator"),
            Line(8, "ServerValueModifier.SVM"),
            Line(16, "DefinitiveWeaponVariants.DefinitiveWeaponVariants"),
            Line(17, "Unknown.Thing"),
            Line(20, "SPTarkov.Server.Core.Services.Hosted.SPTStartupHostedService", "Started webserver at https://0.0.0.0:6969"),
            Line(30, "DefinitiveWeaponVariants.DefinitiveWeaponVariants"),
        };
        int? ModOf(string source) => source.StartsWith("ServerValueModifier") ? 0 : source.StartsWith("DefinitiveWeaponVariants") ? 1 : null;

        var shares = ModLoadShares.From(lines, ModOf, 3);

        Assert.Equal(TimeSpan.FromSeconds(6), shares[0]);
        Assert.Equal(TimeSpan.FromSeconds(8), shares[1]);
        Assert.Equal(TimeSpan.Zero, shares[2]);
        Assert.All(ModLoadShares.From(null, ModOf, 3), s => Assert.Equal(TimeSpan.Zero, s));
    }

    [Fact]
    public void Time_left_takes_the_shares_of_the_mods_off_from_the_first_full_start()
    {
        TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);
        IReadOnlyList<TimeSpan> shares = [S(20), S(10), S(5), S(5)];
        IReadOnlyList<int> all = [0, 1, 2, 3];

        // 60 s with all on: 20 s fixed. Mod 0 off: 40 s each, three left, five seconds in.
        Assert.Equal(S(115), BrokenModSearch.TimeLeft([(all, S(60))], shares, [1, 2, 3], 3, S(5)));

        // The all-off start took 30 s against a guess of 20 s: (60 + 30) / (60 + 20) = 9/8 from then on.
        Assert.Equal(S(45 * 2), BrokenModSearch.TimeLeft([(all, S(60)), ([], S(30))], shares, [1, 2, 3], 2, S(0)));

        // No shares to go on: the count-based estimate.
        Assert.Equal(S(475), BrokenModSearch.TimeLeft([(all, S(60))], [], [0, 1], 8, S(5)));
        Assert.Null(BrokenModSearch.TimeLeft([], shares, all, 8, S(0)));
        Assert.Null(BrokenModSearch.TimeLeft([(all, S(60))], shares, all, 0, S(0)));
    }

    [Fact]
    public void Time_left_starts_from_the_first_full_start_and_follows_each_batch()
    {
        TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

        // Nothing to go on before the first start.
        Assert.Null(BrokenModSearch.TimeLeft([], 40, 8, S(5)));

        // The first full start: 60 s each, eight left, five seconds into this one.
        Assert.Equal(S(475), BrokenModSearch.TimeLeft([(40, S(60))], 0, 8, S(5)));

        // 40 on took 60 s, none on took 20 s: 20 s plus 1 s a mod, so 40 s with 20 on.
        Assert.Equal(S(190), BrokenModSearch.TimeLeft([(40, S(60)), (0, S(20))], 20, 5, S(10)));

        // A third batch on the same line changes nothing.
        Assert.Equal(S(120), BrokenModSearch.TimeLeft([(40, S(60)), (0, S(20)), (20, S(40))], 10, 4, S(0)));

        // The same mods on each time: their average.
        Assert.Equal(S(80), BrokenModSearch.TimeLeft([(10, S(30)), (10, S(50))], 10, 2, S(0)));

        // Never less than nothing, and nothing to say once none are left.
        Assert.Equal(TimeSpan.Zero, BrokenModSearch.TimeLeft([(10, S(10))], 10, 1, S(40)));
        Assert.Null(BrokenModSearch.TimeLeft([(10, S(10))], 10, 0, S(0)));
    }

    private sealed class Collect(List<BrokenModProgress> into) : IProgress<BrokenModProgress>
    {
        public void Report(BrokenModProgress value) => into.Add(value);
    }

    // ---- the log -------------------------------------------------------------------------------

    [Fact]
    public void Only_what_was_logged_since_is_read()
    {
        var install = Path.Combine(_dir, "SPT");
        var logs = Path.Combine(install, "user", "logs", "spt");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(install, "SPT.Server.exe"), "");
        var log = Path.Combine(logs, "spt20261111.log");
        File.WriteAllText(log, "[2026-11-11 10:00:00.000][Error][Old] from an earlier start\n");

        var before = ServerTrial.LogLengths(install);

        File.AppendAllText(log,
            "[2026-11-11 11:00:00.000][Information][Loader] loading mods\n" +
            "[2026-11-11 11:00:01.000][Error][BadMod] \u001b[31mcould not load 3 items\u001b[0m\n" +
            "   at BadMod.Load()\n");

        var error = Assert.Single(ServerTrial.ErrorsSince(install, before));
        Assert.Equal("BadMod", error.Source);
        Assert.Equal("could not load 3 items", error.Message);
        Assert.Equal("BadMod|could not load # items", ServerTrial.Signature(error));
    }

    [Fact]
    public void What_a_search_logged_is_taken_back_out()
    {
        var install = Path.Combine(_dir, "SPT");
        var logs = Path.Combine(install, "user", "logs", "spt");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(install, "SPT.Server.exe"), "");
        var log = Path.Combine(logs, "spt20261111.log");
        File.WriteAllText(log, "[2026-11-11 10:00:00.000][Information][Server] the run before\n");

        var before = ServerTrial.LogLengths(install);
        File.AppendAllText(log, "[2026-11-11 11:00:00.000][Error][BadMod] from a search's start\n");
        File.WriteAllText(Path.Combine(logs, "spt20261112.log"), "[2026-11-12 00:00:01.000][Error][BadMod] past midnight\n");

        ServerTrial.TrimLogs(install, before);

        Assert.Equal("[2026-11-11 10:00:00.000][Information][Server] the run before\n", File.ReadAllText(log));
        Assert.False(File.Exists(Path.Combine(logs, "spt20261112.log")));
    }

    // ---- the session ---------------------------------------------------------------------------

    [Fact]
    public void A_session_turns_mods_off_and_puts_every_one_back()
    {
        var install = Path.Combine(_dir, "SPT");
        var mods = Path.Combine(install, "user", "mods");
        string[] names = ["Alpha", "Beta", "Gamma"];
        foreach (var name in names)
        {
            Directory.CreateDirectory(Path.Combine(mods, name));
            File.WriteAllText(Path.Combine(mods, name, "mod.dll"), name);
        }

        var session = new BrokenModSession
        {
            InstallPath = install,
            StartedAt = DateTimeOffset.Now,
            Mods = [.. names.Select(n => Path.Combine(mods, n))],
            Logs = new() { [Path.Combine(install, "user", "logs", "spt", "spt.log")] = 1234 },
        };

        var store = new BrokenModSessionStore(Path.Combine(_dir, "brokenModSearch.json"));
        store.Save(session);

        var outcome = BrokenModSessionStore.Set(session, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { session.Mods[1] });

        Assert.Empty(outcome.Failed);
        Assert.False(Directory.Exists(Path.Combine(mods, "Alpha")));
        Assert.True(Directory.Exists(Path.Combine(mods, "Beta")));
        Assert.True(File.Exists(Path.Combine(install, "user", "mods.disabled", "Gamma", "mod.dll")));
        Assert.Equal(2, BrokenModSessionStore.TurnedOff(session));

        // As the next start of the app finds it.
        var found = new BrokenModSessionStore(Path.Combine(_dir, "brokenModSearch.json")).Load()!;
        Assert.Equal(session.Mods, found.Mods);
        Assert.Equal(session.Logs, found.Logs);

        Assert.Empty(BrokenModSessionStore.PutBack(found).Failed);
        Assert.All(names, n => Assert.True(File.Exists(Path.Combine(mods, n, "mod.dll"))));
        Assert.False(Directory.Exists(Path.Combine(install, "user", "mods.disabled")));
        Assert.Equal(0, BrokenModSessionStore.TurnedOff(found));

        store.Clear();
        Assert.Null(store.Load());
    }
}
