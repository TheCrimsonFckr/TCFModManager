using System.Net;
using System.Text;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

public class SpModListResolverTests
{
    private sealed class FakeApi : ISpModListApi
    {
        public Dictionary<int, IReadOnlyList<SpModCandidateVersion>> Recent { get; } = [];
        public Dictionary<int, IReadOnlyList<SpModCandidateVersion>> All { get; } = [];
        public Dictionary<int, IReadOnlyList<SpModCandidateVersion>> Addons { get; } = [];
        public Dictionary<string, List<DependencyNode>> ModTrees { get; } = [];
        public Dictionary<string, List<DependencyNode>> AddonTrees { get; } = [];

        public List<string> Calls { get; } = [];
        public Exception? Throw { get; set; }

        private void Call(string what)
        {
            Calls.Add(what);
            if (Throw is not null) throw Throw;
        }

        public Task<IReadOnlyDictionary<int, IReadOnlyList<SpModCandidateVersion>>> ModVersionsAsync(IReadOnlyList<int> modIds, CancellationToken ct)
        {
            Call($"mods {string.Join(",", modIds)}");
            return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<SpModCandidateVersion>>>(
                Recent.Where(kv => modIds.Contains(kv.Key)).ToDictionary());
        }

        public Task<IReadOnlyList<SpModCandidateVersion>> AllModVersionsAsync(int modId, CancellationToken ct)
        {
            Call($"all {modId}");
            return Task.FromResult(All.TryGetValue(modId, out var v) ? v : Recent.GetValueOrDefault(modId) ?? []);
        }

        public Task<IReadOnlyDictionary<int, IReadOnlyList<SpModCandidateVersion>>> AddonVersionsAsync(IReadOnlyList<int> addonIds, CancellationToken ct)
        {
            Call($"addons {string.Join(",", addonIds)}");
            return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<SpModCandidateVersion>>>(
                Addons.Where(kv => addonIds.Contains(kv.Key)).ToDictionary());
        }

        public Task<Dictionary<string, List<DependencyNode>>> ModDependenciesAsync(string pairs, string sptVersion, CancellationToken ct)
        {
            Call($"mod-deps {pairs} @{sptVersion}");
            return Task.FromResult(pairs.Split(',').Where(ModTrees.ContainsKey).ToDictionary(p => p, p => ModTrees[p]));
        }

        public Task<Dictionary<string, List<DependencyNode>>> AddonDependenciesAsync(string pairs, string sptVersion, CancellationToken ct)
        {
            Call($"addon-deps {pairs} @{sptVersion}");
            return Task.FromResult(pairs.Split(',').Where(AddonTrees.ContainsKey).ToDictionary(p => p, p => AddonTrees[p]));
        }
    }

    private static readonly Dictionary<int, int> NoParents = [];

    private static ModListEntry Mod(int id, string? version, string? name = null) =>
        new() { Name = name ?? $"Mod {id}", ModId = id, Version = version };

    private static ModListEntry Addon(int id, string version) =>
        new() { Name = $"Addon {id}", ModId = id, IsAddon = true, Version = version };

    private static SpModCandidateVersion V(int id, string version, string? constraint) => new(id, version, constraint);

    private static DependencyNode Node(int id, string? version, params DependencyNode[] children) => new()
    {
        Id = id,
        Name = $"Dep {id}",
        LatestCompatibleVersion = version is null ? null : new DependencyVersionRef { Id = id * 100, Version = version },
        Dependencies = [.. children],
    };

    // ---- retarget ----

    [Fact]
    public async Task AModTakesItsNewestVersionForTheTarget()
    {
        var api = new FakeApi();
        api.Recent[791] = [V(3, "4.4.3", "~4.0.0"), V(5, "4.5.1", "~4.1.3"), V(4, "4.5.0", "~4.1.3"), V(9, "5.0.0", "~4.2.0")];

        var result = await SpModListResolver.RetargetAsync([Mod(791, "4.4.3", "SAIN")], NoParents, "4.1.6", api);

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Rows);
        Assert.Equal(SpModRetargetOutcome.Changed, row.Outcome);
        Assert.Equal("4.5.1", row.To.Version);
        Assert.Equal(5, row.To.VersionId);
        Assert.Equal("SAIN", row.To.Name);
        Assert.Equal("4.4.3", row.From.Version);
    }

    [Fact]
    public async Task ThePatchFloorIsHonoured()
    {
        // The 2026-10-02 ruling: "~4.1.6" does not run on 4.1.5.
        var api = new FakeApi();
        api.Recent[2706] = [V(2, "2.1.1", "~4.1.6"), V(1, "1.3.0", "~4.1.2")];

        var row = Assert.Single((await SpModListResolver.RetargetAsync([Mod(2706, "2.1.1")], NoParents, "4.1.5", api)).Rows);

        Assert.Equal("1.3.0", row.To.Version);
    }

    [Fact]
    public async Task WhenNoRecentVersionFitsTheFullListIsAsked()
    {
        var api = new FakeApi();
        api.Recent[1] = [V(10, "3.0.0", "~4.1.0"), V(9, "2.0.0", "~4.0.0")];
        api.All[1] = [V(10, "3.0.0", "~4.1.0"), V(9, "2.0.0", "~4.0.0"), V(5, "1.2.0", "~3.11.0"), V(4, "1.1.0", "~3.11.0")];

        var row = Assert.Single((await SpModListResolver.RetargetAsync([Mod(1, "3.0.0")], NoParents, "3.11.4", api)).Rows);

        Assert.Equal("1.2.0", row.To.Version);
        Assert.Contains("all 1", api.Calls);
    }

    [Fact]
    public async Task OnlyAModCutOffAtTheEmbedCapIsAskedForMore()
    {
        var api = new FakeApi();
        api.Recent[1] = [.. Enumerable.Range(1, 10).Select(i => V(i, $"3.{i}.0", "~4.1.0"))];
        api.Recent[2] = [V(20, "1.0.0", "~4.1.0"), V(21, "0.9.0", "~4.1.0")];

        var result = await SpModListResolver.RetargetAsync([Mod(1, "3.1.0"), Mod(2, "1.0.0")], NoParents, "4.0.13", api);

        Assert.Equal(["mods 1,2", "all 1"], api.Calls);
        Assert.All(result.Rows, r => Assert.Equal(SpModRetargetOutcome.NoVersion, r.Outcome));
    }

    [Fact]
    public async Task ARecentFitNeedsNoSecondRequest()
    {
        var api = new FakeApi();
        api.Recent[1] = [V(10, "3.0.0", "~4.1.0")];

        await SpModListResolver.RetargetAsync([Mod(1, "2.0.0"), Mod(2, "1.0.0")], NoParents, "4.1.6", api);

        Assert.Equal(["mods 1,2"], api.Calls);
    }

    [Fact]
    public async Task NoVersionAnywhereKeepsTheEntryAsItWas()
    {
        var api = new FakeApi();
        api.Recent[1] = [V(10, "3.0.0", "~4.1.0")];

        var row = Assert.Single((await SpModListResolver.RetargetAsync([Mod(1, "3.0.0")], NoParents, "3.11.4", api)).Rows);

        Assert.Equal(SpModRetargetOutcome.NoVersion, row.Outcome);
        Assert.Same(row.From, row.To);
    }

    [Fact]
    public async Task AModNoLongerOnSpModIsKeptAndSaidSo()
    {
        var row = Assert.Single((await SpModListResolver.RetargetAsync([Mod(1, "1.0.0")], NoParents, "4.1.6", new FakeApi())).Rows);

        Assert.Equal(SpModRetargetOutcome.NotFound, row.Outcome);
        Assert.Equal("1.0.0", row.To.Version);
    }

    [Fact]
    public async Task AModAlreadyAtTheRightVersionIsUnchanged()
    {
        var api = new FakeApi();
        api.Recent[1] = [V(10, "3.0.0", "~4.1.0")];

        var row = Assert.Single((await SpModListResolver.RetargetAsync([Mod(1, "3.0.0")], NoParents, "4.1.6", api)).Rows);

        Assert.Equal(SpModRetargetOutcome.Unchanged, row.Outcome);
    }

    [Fact]
    public async Task AnAddonFitsItsParentsNewVersion()
    {
        var api = new FakeApi();
        api.Recent[2706] = [V(21, "2.1.1", "~4.1.6"), V(12, "1.2.1", "~4.0.13")];
        api.Addons[135] = [V(30, "1.3.0", "~2.0"), V(20, "1.0.0", "^1.2.0")];

        var result = await SpModListResolver.RetargetAsync(
            [Mod(2706, "2.1.1"), Addon(135, "1.3.0")], new Dictionary<int, int> { [135] = 2706 }, "4.0.13", api);

        Assert.Equal("1.2.1", result.Rows[0].To.Version);
        Assert.Equal(SpModRetargetOutcome.Changed, result.Rows[1].Outcome);
        Assert.Equal("1.0.0", result.Rows[1].To.Version);
        Assert.True(result.Rows[1].To.IsAddon);
    }

    [Fact]
    public async Task AddonConstraintsAreReadLiterally()
    {
        // "~2.0" against parent 2.1.1 is a real case on sp-mod: Live-Like ORBIT and ORBIT 2.1.1.
        var api = new FakeApi();
        api.Recent[2706] = [V(21, "2.1.1", "~4.1.6")];
        api.Addons[135] = [V(30, "1.3.0", "~2.0")];

        var result = await SpModListResolver.RetargetAsync(
            [Mod(2706, "2.1.1"), Addon(135, "1.3.0")], new Dictionary<int, int> { [135] = 2706 }, "4.1.6", api);

        Assert.Equal(SpModRetargetOutcome.NoVersion, result.Rows[1].Outcome);
    }

    [Fact]
    public async Task AnAddonWithNoParentOnTheListIsKept()
    {
        var api = new FakeApi();
        api.Addons[135] = [V(30, "1.3.0", "~2.0")];

        var row = Assert.Single((await SpModListResolver.RetargetAsync(
            [Addon(135, "1.3.0")], new Dictionary<int, int> { [135] = 2706 }, "4.1.6", api)).Rows);

        Assert.Equal(SpModRetargetOutcome.ParentUnknown, row.Outcome);
    }

    [Fact]
    public async Task ModAndAddonIdsDoNotCollide()
    {
        var api = new FakeApi();
        api.Recent[5] = [V(51, "9.0.0", "~4.1.0")];
        api.Recent[7] = [V(71, "2.0.0", "~4.1.0")];
        api.Addons[5] = [V(52, "1.1.0", "^2.0.0")];

        var result = await SpModListResolver.RetargetAsync(
            [Mod(5, "8.0.0"), Mod(7, "1.0.0"), Addon(5, "1.0.0")], new Dictionary<int, int> { [5] = 7 }, "4.1.6", api);

        Assert.Equal(["9.0.0", "2.0.0", "1.1.0"], result.Entries.Select(e => e.Version));
    }

    [Fact]
    public async Task EntriesWithNothingToGoOnAreSkippedInPlace()
    {
        var api = new FakeApi();
        api.Recent[1] = [V(10, "3.0.0", "~4.1.0")];
        var manual = new ModListEntry { Name = "Hand-installed" };

        var result = await SpModListResolver.RetargetAsync([manual, Mod(2, null), Mod(1, "2.0.0")], NoParents, "4.1.6", api);

        Assert.Equal([SpModRetargetOutcome.Skipped, SpModRetargetOutcome.Skipped, SpModRetargetOutcome.Changed],
            result.Rows.Select(r => r.Outcome));
        Assert.Equal(["mods 1"], api.Calls);
    }

    [Fact]
    public async Task AnApiFailureChangesNothing()
    {
        var api = new FakeApi { Throw = new HttpRequestException("offline") };
        var entries = new[] { Mod(1, "1.0.0") };

        var result = await SpModListResolver.RetargetAsync(entries, NoParents, "4.1.6", api);

        Assert.Equal(SpModResolveFailure.Api, result.Failure);
        Assert.Equal("offline", result.Detail);
        Assert.Same(entries[0], Assert.Single(result.Entries));
    }

    [Fact]
    public async Task CancellingIsNotAFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var api = new FakeApi { Throw = new TaskCanceledException() };

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            SpModListResolver.RetargetAsync([Mod(1, "1.0.0")], NoParents, "4.1.6", api, ct: cts.Token));
    }

    [Fact]
    public async Task ProgressCountsEveryEntryAsked()
    {
        var api = new FakeApi();
        var seen = new List<SpModResolveProgress>();

        await SpModListResolver.RetargetAsync([Mod(1, "1.0.0"), Addon(2, "1.0.0")], NoParents, "4.1.6", api, new SyncProgress(seen));

        Assert.Equal(new SpModResolveProgress(0, 2), seen[0]);
        Assert.Equal(new SpModResolveProgress(2, 2), seen[^1]);
    }

    private sealed class SyncProgress(List<SpModResolveProgress> seen) : IProgress<SpModResolveProgress>
    {
        public void Report(SpModResolveProgress value) => seen.Add(value);
    }

    // ---- dependencies ----

    [Fact]
    public async Task MissingDependenciesAreFoundThroughTheWholeTree()
    {
        var api = new FakeApi();
        api.ModTrees["2706:2.1.1"] = [Node(791, "4.5.1", Node(902, "1.5.0"), Node(827, "1.9.0"))];

        var result = await SpModListResolver.MissingDependenciesAsync([Mod(2706, "2.1.1", "ORBIT")], NoParents, "4.1.6", api);

        Assert.True(result.Succeeded);
        Assert.Equal([791, 827, 902], result.Missing.Select(d => d.ModId));
        Assert.Equal(["ORBIT"], Assert.Single(result.Missing, d => d.ModId == 791).NeededBy);
        Assert.Equal(["Dep 791"], Assert.Single(result.Missing, d => d.ModId == 902).NeededBy);
        Assert.Equal(["mod-deps 2706:2.1.1 @4.1.6"], api.Calls);
    }

    [Fact]
    public async Task ADependencyAlreadyOnTheListIsNotMissingButItsOwnAreChecked()
    {
        var api = new FakeApi();
        api.ModTrees["2706:2.1.1"] = [Node(791, "4.5.1", Node(902, "1.5.0"))];

        var result = await SpModListResolver.MissingDependenciesAsync(
            [Mod(2706, "2.1.1"), Mod(791, "4.4.3")], NoParents, "4.1.6", api);

        Assert.Equal([902], result.Missing.Select(d => d.ModId));
    }

    [Fact]
    public async Task ADependencySharedByTwoEntriesIsListedOnceWithBoth()
    {
        var api = new FakeApi();
        var conflicted = Node(902, "1.5.0");
        conflicted.Conflict = true;
        api.ModTrees["1:1.0.0"] = [Node(902, "1.5.0")];
        api.ModTrees["2:1.0.0"] = [conflicted];

        var result = await SpModListResolver.MissingDependenciesAsync([Mod(1, "1.0.0", "One"), Mod(2, "1.0.0", "Two")], NoParents, "4.1.6", api);

        var dep = Assert.Single(result.Missing);
        Assert.Equal(["One", "Two"], dep.NeededBy);
        Assert.True(dep.Conflict);
    }

    [Fact]
    public async Task ADependencyWithNoVersionIsReportedButNotAddable()
    {
        var api = new FakeApi();
        api.ModTrees["1:1.0.0"] = [Node(50, null), Node(51, "2.0.0")];

        var result = await SpModListResolver.MissingDependenciesAsync([Mod(1, "1.0.0")], NoParents, "4.1.6", api);

        Assert.Equal(2, result.Missing.Count);
        Assert.False(Assert.Single(result.Missing, d => d.ModId == 50).HasVersion);
        Assert.Equal([51], result.Addable.Select(d => d.ModId));
    }

    [Fact]
    public async Task AnAddonsMissingParentIsMarkedAndListedFirst()
    {
        var api = new FakeApi();
        api.AddonTrees["135:1.3.0"] = [Node(2706, "2.1.0", Node(791, "4.5.1"))];

        var result = await SpModListResolver.MissingDependenciesAsync(
            [Addon(135, "1.3.0")], new Dictionary<int, int> { [135] = 2706 }, "4.1.6", api);

        Assert.Equal([2706, 791], result.Missing.Select(d => d.ModId));
        Assert.True(result.Missing[0].IsParent);
        Assert.False(result.Missing[1].IsParent);
        Assert.Equal(["addon-deps 135:1.3.0 @4.1.6"], api.Calls);
    }

    [Fact]
    public async Task ABuildSuffixIsDroppedWhenSpModDoesNotRecogniseTheFullVersion()
    {
        var api = new FakeApi();
        api.ModTrees["2898:1.1.0"] = [Node(5, "1.0.0")];

        var result = await SpModListResolver.MissingDependenciesAsync([Mod(2898, "1.1.0+spt4.0")], NoParents, "4.0.13", api);

        Assert.Equal([5], result.Missing.Select(d => d.ModId));
        Assert.Empty(result.NotChecked);
        Assert.Equal(["mod-deps 2898:1.1.0+spt4.0 @4.0.13", "mod-deps 2898:1.1.0 @4.0.13"], api.Calls);
    }

    [Fact]
    public async Task EntriesSpModCannotPlaceAreNotChecked()
    {
        var api = new FakeApi();
        var manual = new ModListEntry { Name = "Hand-installed" };

        var result = await SpModListResolver.MissingDependenciesAsync([manual, Mod(1, null), Mod(2, "1.0.0")], NoParents, "4.1.6", api);

        Assert.Equal(3, result.NotChecked.Count);
        Assert.Equal(["mod-deps 2:1.0.0 @4.1.6"], api.Calls);
    }

    [Fact]
    public async Task ModsAndAddonsAreAskedInBatches()
    {
        var api = new FakeApi();
        var entries = Enumerable.Range(1, 30).Select(i => Mod(i, "1.0.0")).Append(Addon(1, "1.0.0")).ToList();

        await SpModListResolver.MissingDependenciesAsync(entries, NoParents, "4.1.6", api);

        Assert.Equal(3, api.Calls.Count);
        Assert.Equal(25, api.Calls[0].Split(',').Length);
        Assert.Equal(5, api.Calls[1].Split(',').Length);
        Assert.StartsWith("addon-deps 1:1.0.0", api.Calls[2]);
    }

    [Fact]
    public async Task ACycleInTheTreeDoesNotLoop()
    {
        var api = new FakeApi();
        var a = Node(10, "1.0.0");
        var b = Node(11, "1.0.0", a);
        a.Dependencies.Add(b);
        api.ModTrees["1:1.0.0"] = [a];

        var result = await SpModListResolver.MissingDependenciesAsync([Mod(1, "1.0.0")], NoParents, "4.1.6", api);

        Assert.Equal([10, 11], result.Missing.Select(d => d.ModId).Order());
    }

    [Fact]
    public async Task AnUnknownSptVersionIsItsOwnFailure()
    {
        var api = new FakeApi
        {
            Throw = new SpModApiException(HttpStatusCode.BadRequest, "VALIDATION_FAILED", "SPT version not found or not published."),
        };

        var result = await SpModListResolver.MissingDependenciesAsync([Mod(1, "1.0.0")], NoParents, "9.9.9", api);

        Assert.Equal(SpModResolveFailure.UnknownSptVersion, result.Failure);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public async Task AnyOtherApiErrorIsAnApiFailure()
    {
        var api = new FakeApi { Throw = new SpModApiException(HttpStatusCode.InternalServerError, null, "boom") };

        var result = await SpModListResolver.MissingDependenciesAsync([Mod(1, "1.0.0")], NoParents, "4.1.6", api);

        Assert.Equal(SpModResolveFailure.Api, result.Failure);
    }

    [Fact]
    public void ADependencyBecomesAPinnedEntry()
    {
        var entry = new SpModDependency(791, "SAIN", "4.5.1", 15192, ["ORBIT"], false, false).ToEntry();

        Assert.Equal((791, "4.5.1", 15192, false), (entry.ModId!.Value, entry.Version, entry.VersionId!.Value, entry.IsAddon));
        Assert.True(entry.IsPinned);
    }

    // ---- the real API wrapper ----

    private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(responses[Math.Min(_next++, responses.Length - 1)](request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string ModsPage = """
        {"success":true,"data":[{"id":791,"name":"SAIN","versions":[
          {"id":5,"version":"4.5.1","spt_version_constraint":"~4.1.3"},
          {"id":3,"version":"4.4.3","spt_version_constraint":"~4.0.0"}]}]}
        """;

    [Fact]
    public async Task TheBatchAsksForVersionsAndLegacyMods()
    {
        var handler = new ScriptedHandler(_ => Json(ModsPage));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://sp-mod.com") };
        var api = new SpModListApi(new SpModApiClient(http));

        var versions = await api.ModVersionsAsync([791, 902], CancellationToken.None);

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).Query);
        Assert.Contains("filter[id]=791,902", query);
        Assert.Contains("filter[include_legacy]=true", query);
        Assert.Contains("include=versions", query);
        Assert.Contains("per_page=50", query);

        Assert.Equal(["4.5.1", "4.4.3"], versions[791].Select(v => v.Version));
        Assert.Equal("~4.1.3", versions[791][0].Constraint);
        Assert.False(versions.ContainsKey(902));
    }

    [Fact]
    public async Task OverFiftyModsAreSplit()
    {
        var handler = new ScriptedHandler(_ => Json("""{"success":true,"data":[]}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://sp-mod.com") };

        await new SpModListApi(new SpModApiClient(http)).ModVersionsAsync([.. Enumerable.Range(1, 120)], CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ARateLimitIsWaitedOut()
    {
        var handler = new ScriptedHandler(
            _ =>
            {
                var limited = Json("""{"success":false,"code":"RATE_LIMITED","message":"slow down"}""", HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return limited;
            },
            _ => Json(ModsPage));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://sp-mod.com") };

        var versions = await new SpModListApi(new SpModApiClient(http)).ModVersionsAsync([791], CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(versions.ContainsKey(791));
    }
}
