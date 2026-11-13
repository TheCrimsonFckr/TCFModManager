using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-46: a dedicated headless - one nobody plays at, often running from the server's own SPT
// folder - takes a Server + Client mod for its server half and keeps Client only mods off, from a
// served list and from its own.
//
public class ModListSharedHeadlessTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private const ModListEntryScope ServerAndClient = ModListEntryScope.Server | ModListEntryScope.Client;

    private static readonly ModListEntryScope Player = InstallRole.ScopeFor(InstallRoles.Player);
    private static readonly ModListEntryScope Dedicated = InstallRole.ScopeFor(InstallRoles.Headless);
    private static readonly ModListEntryScope PlaysAndHosts = InstallRole.ScopeFor(InstallRoles.Player | InstallRoles.Headless);

    private readonly string _install = Path.Combine(Path.GetTempPath(), "tcfmm-shared-headless-" + Guid.NewGuid().ToString("N"));

    public ModListSharedHeadlessTests() => Directory.CreateDirectory(_install);

    public void Dispose()
    {
        try { Directory.Delete(_install, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static ModListEntry Entry(string name, ModListEntryScope scope, string version = "1.0.0") =>
        new()
        {
            Name = name,
            ModId = 4600 + Math.Abs(StringComparer.Ordinal.GetHashCode(name) % 1000),
            Scope = scope,
            Version = version,
            Folders = [name.ToLowerInvariant()],
        };

    private static ModList List(ModListOrigin origin, params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Shared box",
            Origin = origin,
            Policy = ModListPolicy.Exclusive,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        list.Entries.AddRange(entries);
        return list;
    }

    private string Folder(InstalledModTarget target, string name, bool disabled) =>
        target == InstalledModTarget.Client
            ? Path.Combine(_install, "BepInEx", disabled ? "plugins.disabled" : "plugins", name)
            : Path.Combine(_install, "SPT", "user", disabled ? "mods.disabled" : "mods", name);

    private InstalledMod Half(InstalledModTarget target, string name, bool disabled)
    {
        var dir = Folder(target, name, disabled);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".dll"), target.ToString());

        return new InstalledMod { Name = name, Target = target, FolderPath = dir, IsDisabled = disabled };
    }

    // A two-halved mod like Dynamic Maps, each half live or set aside as asked.
    private ModListCandidate TwoHalves(string name, bool clientAside = false, bool serverAside = false, string version = "1.0.0")
    {
        var client = Half(InstalledModTarget.Client, name, clientAside);
        var server = Half(InstalledModTarget.Server, name, serverAside);

        return new ModListCandidate
        {
            Name = name,
            Version = version,
            Folders = [name.ToLowerInvariant()],
            IsDisabled = clientAside && serverAside,
            Entries = [client, server],
        };
    }

    private ModListCandidate ClientOnly(string name, bool disabled = false) => new()
    {
        Name = name,
        Version = "1.0.0",
        Folders = [name.ToLowerInvariant()],
        IsDisabled = disabled,
        Entries = [Half(InstalledModTarget.Client, name, disabled)],
    };

    private static ModListAction Only(ModListPlan plan, string name) => Assert.Single(plan.Actions, a => a.Name == name);

    //
    // The planner.
    //

    [Theory]
    [InlineData(ModListOrigin.Server)]
    [InlineData(ModListOrigin.Local)]
    public void AServerAndClientModIsInstalledForItsServerHalf(ModListOrigin origin)
    {
        var plan = ModListPlanner.Build(List(origin, Entry("DynamicMaps", ServerAndClient)), [], machine: Dedicated);

        var action = Only(plan, "DynamicMaps");
        Assert.Equal(ModListActionKind.Install, action.Kind);
        Assert.True(action.SetsClientHalfAside);
    }

    [Theory]
    [InlineData(ModListOrigin.Server)]
    [InlineData(ModListOrigin.Local)]
    public void AnInstalledLiveClientHalfIsSetAside(ModListOrigin origin)
    {
        var plan = ModListPlanner.Build(
            List(origin, Entry("DynamicMaps", ServerAndClient)), [TwoHalves("DynamicMaps")], machine: Dedicated);

        Assert.Equal(ModListActionKind.SetAsideClientHalf, Only(plan, "DynamicMaps").Kind);
        Assert.True(plan.RequiresGameClosed);
        Assert.False(plan.IsNoOp);
    }

    [Fact]
    public void AClientHalfAlreadySetAsideIsLeftAlone()
    {
        var plan = ModListPlanner.Build(
            List(ModListOrigin.Local, Entry("DynamicMaps", ServerAndClient)),
            [TwoHalves("DynamicMaps", clientAside: true)],
            machine: Dedicated);

        var action = Only(plan, "DynamicMaps");
        Assert.Equal(ModListActionKind.Keep, action.Kind);
        Assert.True(action.SetsClientHalfAside);
        Assert.True(plan.IsNoOp);
    }

    [Fact]
    public void ADisabledServerAndClientModIsEnabledForItsServerHalf()
    {
        var plan = ModListPlanner.Build(
            List(ModListOrigin.Local, Entry("DynamicMaps", ServerAndClient)),
            [TwoHalves("DynamicMaps", clientAside: true, serverAside: true)],
            machine: Dedicated);

        var action = Only(plan, "DynamicMaps");
        Assert.Equal(ModListActionKind.Enable, action.Kind);
        Assert.True(action.SetsClientHalfAside);
    }

    [Fact]
    public void AnUpdateCarriesTheFlag()
    {
        var plan = ModListPlanner.Build(
            List(ModListOrigin.Local, Entry("DynamicMaps", ServerAndClient, version: "1.3.0")),
            [TwoHalves("DynamicMaps", version: "1.2.1")],
            machine: Dedicated);

        var action = Only(plan, "DynamicMaps");
        Assert.Equal(ModListActionKind.Update, action.Kind);
        Assert.True(action.SetsClientHalfAside);
    }

    [Theory]
    [InlineData(ModListOrigin.Server)]
    [InlineData(ModListOrigin.Local)]
    public void AClientOnlyModIsNeverInstalledAndSetAsideWhenPresent(ModListOrigin origin)
    {
        var list = List(origin, Entry("HUD", ModListEntryScope.Client), Entry("Missing HUD", ModListEntryScope.Client));

        var plan = ModListPlanner.Build(list, [ClientOnly("HUD")], machine: Dedicated);

        var hud = Only(plan, "HUD");
        Assert.Equal(ModListActionKind.Disable, hud.Kind);
        Assert.NotNull(hud.Entry);

        Assert.DoesNotContain(plan.Actions, a => a.Name == "Missing HUD");
    }

    [Fact]
    public void AClientOnlyModAlreadyDisabledIsLeftAlone()
    {
        var plan = ModListPlanner.Build(
            List(ModListOrigin.Local, Entry("HUD", ModListEntryScope.Client)),
            [ClientOnly("HUD", disabled: true)],
            machine: Dedicated);

        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void APinnedClientOnlyModIsPinnedNotDisabled()
    {
        var hud = ClientOnly("HUD");

        var plan = ModListPlanner.Build(
            List(ModListOrigin.Local, Entry("HUD", ModListEntryScope.Client)),
            [hud],
            neverAutoDisable: ModListPlanner.PinKeys(hud).ToHashSet(StringComparer.OrdinalIgnoreCase),
            machine: Dedicated);

        Assert.Equal(ModListActionKind.Pinned, Only(plan, "HUD").Kind);
    }

    [Fact]
    public void ServerOnlyAndEveryoneEntriesAreUnchanged()
    {
        var server = new ModListCandidate
        {
            Name = "fika-server",
            Version = "1.0.0",
            Folders = ["fika-server"],
            Entries = [Half(InstalledModTarget.Server, "fika-server", disabled: false)],
        };

        var plan = ModListPlanner.Build(
            List(ModListOrigin.Local, Entry("fika-server", ModListEntryScope.Server), Entry("SAIN", ModListEntryScope.Everyone)),
            [server, TwoHalves("SAIN")],
            machine: Dedicated);

        Assert.Equal(ModListActionKind.Keep, Only(plan, "fika-server").Kind);

        var sain = Only(plan, "SAIN");
        Assert.Equal(ModListActionKind.Keep, sain.Kind);
        Assert.False(sain.SetsClientHalfAside);
    }

    [Fact]
    public void APlayerIsUnchanged()
    {
        var list = List(ModListOrigin.Local, Entry("DynamicMaps", ServerAndClient), Entry("HUD", ModListEntryScope.Client));

        var plan = ModListPlanner.Build(list, [TwoHalves("DynamicMaps"), ClientOnly("HUD")], machine: Player);

        Assert.All(plan.Actions, a => Assert.Equal(ModListActionKind.Keep, a.Kind));
        Assert.All(plan.Actions, a => Assert.False(a.SetsClientHalfAside));
    }

    [Fact]
    public void AMachineThatPlaysAndHostsIsUnchanged()
    {
        var list = List(ModListOrigin.Server, Entry("DynamicMaps", ServerAndClient), Entry("HUD", ModListEntryScope.Client));

        var plan = ModListPlanner.Build(list, [TwoHalves("DynamicMaps"), ClientOnly("HUD")], machine: PlaysAndHosts);

        Assert.All(plan.Actions, a => Assert.Equal(ModListActionKind.Keep, a.Kind));
        Assert.All(plan.Actions, a => Assert.False(a.SetsClientHalfAside));
    }

    //
    // The applier.
    //

    private static Task<ModListFetchOutcome> NoFetch(IReadOnlyList<ModListAction> fetches, CancellationToken _) =>
        Task.FromResult(new ModListFetchOutcome(fetches, [], false));

    [Fact]
    public async Task SettingAsideMovesTheClientHalfAndLeavesTheServerHalf()
    {
        var mod = TwoHalves("DynamicMaps");
        var plan = ModListPlanner.Build(List(ModListOrigin.Local, Entry("DynamicMaps", ServerAndClient)), [mod], machine: Dedicated);

        var result = await ModListApplier.ApplyAsync(plan, [mod], NoFetch, new ModListApplyOptions { InstallPath = _install });

        Assert.True(result.Completed);
        Assert.Single(result.Disabled.Moved);
        Assert.False(Directory.Exists(Folder(InstalledModTarget.Client, "DynamicMaps", false)));
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Client, "DynamicMaps", true)));
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Server, "DynamicMaps", false)));

        ModDisableService.Revert(result.Moves, _install);
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Client, "DynamicMaps", false)));
    }

    [Fact]
    public async Task AFreshInstallHasItsClientHalfSetAsideFromTheRescan()
    {
        var plan = ModListPlanner.Build(List(ModListOrigin.Server, Entry("DynamicMaps", ServerAndClient)), [], machine: Dedicated);

        var rescans = 0;
        var result = await ModListApplier.ApplyAsync(
            plan,
            [],
            NoFetch,
            new ModListApplyOptions
            {
                InstallPath = _install,
                Rescan = () =>
                {
                    rescans++;
                    return Task.FromResult<IReadOnlyList<ModListCandidate>>([TwoHalves("DynamicMaps")]);
                },
            });

        Assert.Equal(1, rescans);
        Assert.Single(result.Disabled.Moved);
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Client, "DynamicMaps", true)));
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Server, "DynamicMaps", false)));
    }

    [Fact]
    public async Task AFetchThatFailsStillSetsAsideWhatArrived()
    {
        var plan = ModListPlanner.Build(
            List(ModListOrigin.Server, Entry("DynamicMaps", ServerAndClient), Entry("Other", ServerAndClient)),
            [],
            machine: Dedicated);

        var result = await ModListApplier.ApplyAsync(
            plan,
            [],
            (fetches, _) => Task.FromResult(new ModListFetchOutcome(
                [.. fetches.Where(f => f.Name == "DynamicMaps")],
                [new ModListFetchFailure("Other", "gone")],
                false)),
            new ModListApplyOptions
            {
                InstallPath = _install,
                Rescan = () => Task.FromResult<IReadOnlyList<ModListCandidate>>([TwoHalves("DynamicMaps")]),
            });

        Assert.Equal(ModListStop.FetchFailed, result.Stopped);
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Client, "DynamicMaps", true)));
    }

    [Fact]
    public async Task EnablingForTheServerHalfLeavesTheClientHalfAside()
    {
        var mod = TwoHalves("DynamicMaps", clientAside: true, serverAside: true);
        var plan = ModListPlanner.Build(List(ModListOrigin.Local, Entry("DynamicMaps", ServerAndClient)), [mod], machine: Dedicated);

        var result = await ModListApplier.ApplyAsync(plan, [mod], NoFetch, new ModListApplyOptions { InstallPath = _install });

        Assert.True(result.Completed);
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Server, "DynamicMaps", false)));
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Client, "DynamicMaps", true)));
        Assert.False(Directory.Exists(Folder(InstalledModTarget.Client, "DynamicMaps", false)));
    }

    [Fact]
    public async Task AClientOnlyModIsDisabledOnApply()
    {
        var hud = ClientOnly("HUD");
        var plan = ModListPlanner.Build(List(ModListOrigin.Server, Entry("HUD", ModListEntryScope.Client)), [hud], machine: Dedicated);

        var result = await ModListApplier.ApplyAsync(plan, [hud], NoFetch, new ModListApplyOptions { InstallPath = _install });

        Assert.True(result.Completed);
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Client, "HUD", true)));
    }
}
