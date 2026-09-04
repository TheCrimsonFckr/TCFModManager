using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModListApplierTests : IDisposable
{
    private readonly string _installRoot;
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 29, 21, 0, 0, TimeSpan.Zero);

    public ModListApplierTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "TCFModManagerApplierTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_installRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_installRoot)) Directory.Delete(_installRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string FolderFor(string name, bool disabled) =>
        Path.Combine(_installRoot, "user", disabled ? "mods.disabled" : "mods", name);

    private ModListCandidate Candidate(string name, int? modId = null, string? version = null, bool disabled = false)
    {
        var dir = FolderFor(name, disabled);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), $$"""{ "name": "{{name}}" }""");

        return new ModListCandidate
        {
            Name = name,
            ModId = modId,
            Version = version,
            IsDisabled = disabled,
            Folders = [name.ToLowerInvariant()],
            Entries =
            [
                new InstalledMod
                {
                    Name = name,
                    Target = InstalledModTarget.Server,
                    FolderPath = dir,
                    IsDisabled = disabled,
                },
            ],
        };
    }

    private static ModList List(params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Fika night",
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        list.Entries.AddRange(entries);
        return list;
    }

    private static ModListEntry Entry(string name, int? modId = null, int? versionId = null, string? version = null) =>
        new() { Name = name, ModId = modId, VersionId = versionId, Version = version };

    // A fetch that always works, recording what it was asked for and what the disk looked like at the time.
    private sealed class RecordingFetch(Func<IReadOnlyList<ModListAction>, ModListFetchOutcome>? result = null)
    {
        public int Calls { get; private set; }
        public List<ModListAction> Asked { get; } = [];
        public Action? WhileFetching { get; init; }

        public Task<ModListFetchOutcome> InvokeAsync(IReadOnlyList<ModListAction> fetches, CancellationToken ct)
        {
            Calls++;
            Asked.AddRange(fetches);
            WhileFetching?.Invoke();
            return Task.FromResult(result?.Invoke(fetches) ?? new ModListFetchOutcome(fetches, [], false));
        }
    }

    private static Task<ModListApplyResult> ApplyAsync(
        ModList list,
        IReadOnlyList<ModListCandidate> installed,
        RecordingFetch fetch,
        ModListApplyOptions? options = null) =>
        ModListApplier.ApplyAsync(
            ModListPlanner.Build(list, installed),
            installed,
            fetch.InvokeAsync,
            options,
            Timestamp);

    [Fact]
    public async Task AnApplyWithNothingToDoNeverCallsTheFetch()
    {
        var installed = new[] { Candidate("SAIN", 2426, "3.2.0") };
        var fetch = new RecordingFetch();

        var result = await ApplyAsync(List(Entry("SAIN", 2426, 55, "3.2.0")), installed, fetch);

        Assert.Equal(0, fetch.Calls);
        Assert.True(result.Completed);
        Assert.Empty(result.Moves);
    }

    [Fact]
    public async Task ADisabledModIsEnabledBeforeTheFetchRuns()
    {
        var installed = new[] { Candidate("SAIN", 2426, "3.1.0", disabled: true) };
        var live = FolderFor("SAIN", disabled: false);
        var enabledDuringFetch = false;

        var fetch = new RecordingFetch { WhileFetching = () => enabledDuringFetch = Directory.Exists(live) };

        var result = await ApplyAsync(List(Entry("SAIN", 2426, 55, "3.2.0")), installed, fetch);

        Assert.True(enabledDuringFetch);
        Assert.True(result.Completed);
        Assert.Single(result.Enabled.Moved);
        Assert.Equal("SAIN", Assert.Single(fetch.Asked).Name);
    }

    [Fact]
    public async Task NothingIsDisabledUntilEveryFetchHasWorked()
    {
        var installed = new[]
        {
            Candidate("SAIN", 2426, "3.1.0"),
            Candidate("Realism", 1263, "1.4.2"),
        };

        var realism = FolderFor("Realism", disabled: false);
        var disabledDuringFetch = true;

        var fetch = new RecordingFetch { WhileFetching = () => disabledDuringFetch = !Directory.Exists(realism) };

        var result = await ApplyAsync(List(Entry("SAIN", 2426, 55, "3.2.0")), installed, fetch);

        Assert.False(disabledDuringFetch);
        Assert.True(result.Completed);
        Assert.Equal("Realism", Assert.Single(result.Disabled.Moved).To.Split(Path.DirectorySeparatorChar)[^1]);
        Assert.False(Directory.Exists(realism));
    }

    [Fact]
    public async Task AFailedFetchLeavesTheOldSetAlone()
    {
        var installed = new[]
        {
            Candidate("SAIN", 2426, "3.1.0"),
            Candidate("Realism", 1263, "1.4.2"),
        };

        var fetch = new RecordingFetch(f => new ModListFetchOutcome([], [new ModListFetchFailure("SAIN", "404")], false));

        var result = await ApplyAsync(List(Entry("SAIN", 2426, 55, "3.2.0")), installed, fetch);

        Assert.False(result.Completed);
        Assert.Empty(result.Disabled.Moved);
        Assert.True(Directory.Exists(FolderFor("Realism", disabled: false)));
        Assert.Equal(ModListStop.FetchFailed, result.Stopped);
        Assert.Equal(1, result.FailedFetches);
    }

    [Fact]
    public async Task ACancelledFetchLeavesTheOldSetAlone()
    {
        var installed = new[]
        {
            Candidate("SAIN", 2426, "3.1.0"),
            Candidate("Realism", 1263, "1.4.2"),
        };

        var fetch = new RecordingFetch(f => new ModListFetchOutcome([], [], Cancelled: true));

        var result = await ApplyAsync(List(Entry("SAIN", 2426, 55, "3.2.0")), installed, fetch);

        Assert.False(result.Completed);
        Assert.Empty(result.Disabled.Moved);
        Assert.True(Directory.Exists(FolderFor("Realism", disabled: false)));
        Assert.Equal(ModListStop.FetchCancelled, result.Stopped);
    }

    [Fact]
    public async Task APendingUpdateIsNotFetchedWhenItsEnableFailed()
    {
        // Something already sitting at the enabled path makes the move fail, the way a leftover
        // copy of the same mod would.
        var installed = new[] { Candidate("SAIN", 2426, "3.1.0", disabled: true) };
        Directory.CreateDirectory(FolderFor("SAIN", disabled: false));

        var fetch = new RecordingFetch();

        var result = await ApplyAsync(List(Entry("SAIN", 2426, 55, "3.2.0")), installed, fetch);

        Assert.Single(result.Enabled.Failed);
        Assert.Equal(0, fetch.Calls);
    }

    [Fact]
    public async Task AnOrdinaryInstallIsStillFetchedWhenSomeOtherModFailedToEnable()
    {
        var installed = new[] { Candidate("SAIN", 2426, "3.1.0", disabled: true) };
        Directory.CreateDirectory(FolderFor("SAIN", disabled: false));

        var fetch = new RecordingFetch();

        var result = await ApplyAsync(
            List(Entry("SAIN", 2426, 55, "3.2.0"), Entry("Realism", 1263, 40, "1.4.2")),
            installed,
            fetch);

        Assert.Single(result.Enabled.Failed);
        Assert.Equal("Realism", Assert.Single(fetch.Asked).Name);
    }

    [Fact]
    public async Task TheSnapshotRecordsWhatWasEnabledBeforeAnythingMoved()
    {
        var installed = new[]
        {
            Candidate("Realism", 1263, "1.4.2"),
            Candidate("SAIN", 2426, "3.1.0", disabled: true),
        };

        var fetch = new RecordingFetch();

        var result = await ApplyAsync(
            List(Entry("SAIN", 2426, 55, "3.1.0")),
            installed,
            fetch,
            new ModListApplyOptions { SnapshotName = "Before Fika night", SptVersion = "3.11.3" });

        var snapshot = result.Snapshot;
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.IsSnapshot);
        Assert.Equal("Before Fika night", snapshot.Name);
        Assert.Equal("3.11.3", snapshot.SptVersion);

        // Realism was enabled and SAIN was not, which is the state to come back to.
        Assert.Equal("Realism", Assert.Single(snapshot.Entries).Name);
    }

    [Fact]
    public async Task NoSnapshotIsBuiltUnlessOneIsAskedFor()
    {
        var installed = new[] { Candidate("Realism", 1263, "1.4.2") };

        var result = await ApplyAsync(List(Entry("Realism", 1263, 40, "1.4.2")), installed, new RecordingFetch());

        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task TheMovesUndoBackToWhereTheyStarted()
    {
        var installed = new[]
        {
            Candidate("SAIN", 2426, "3.2.0", disabled: true),
            Candidate("Realism", 1263, "1.4.2"),
        };

        var result = await ApplyAsync(List(Entry("SAIN", 2426, 55, "3.2.0")), installed, new RecordingFetch());

        Assert.True(result.Completed);
        Assert.Equal(2, result.Moves.Count);
        Assert.True(Directory.Exists(FolderFor("SAIN", disabled: false)));
        Assert.True(Directory.Exists(FolderFor("Realism", disabled: true)));

        ModDisableService.Revert(result.Moves);

        Assert.True(Directory.Exists(FolderFor("SAIN", disabled: true)));
        Assert.True(Directory.Exists(FolderFor("Realism", disabled: false)));
    }

    [Fact]
    public async Task AnAdditiveListNeverDisablesAnything()
    {
        var installed = new[] { Candidate("Realism", 1263, "1.4.2") };
        var list = List(Entry("SAIN", 2426, 55, "3.2.0"));
        list.Policy = ModListPolicy.Additive;

        var result = await ApplyAsync(list, installed, new RecordingFetch());

        Assert.True(result.Completed);
        Assert.Empty(result.Disabled.Moved);
        Assert.True(Directory.Exists(FolderFor("Realism", disabled: false)));
    }

    [Fact]
    public async Task ModsNobodyCanFetchAreReportedAndNeverActedOn()
    {
        var installed = Array.Empty<ModListCandidate>();
        var fetch = new RecordingFetch();

        var result = await ApplyAsync(List(Entry("FixPluginTypesSerialization")), installed, fetch);

        Assert.True(result.Completed);
        Assert.Equal(0, fetch.Calls);
        Assert.Equal("FixPluginTypesSerialization", Assert.Single(result.Manual).Name);
    }
}
