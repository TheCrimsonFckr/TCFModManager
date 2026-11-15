using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-46: a dedicated headless - one nobody plays at, often running from the server's own SPT
// folder - takes a Server + Client mod whole (OPEN-44 Q14) and keeps Client only mods off, from a
// served list and from its own.
//
public class ModListSharedHeadlessTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private const ModListEntryScope ServerAndClient = ModListEntryScope.Server | ModListEntryScope.Client;

    private static readonly ModListEntryScope Player = InstallRole.ScopeFor(InstallRoles.Player);
    // The shared-folder headless hosts, so it carries the Server flag (OPEN-44 Q13).
    private static readonly ModListEntryScope Dedicated = InstallRole.ScopeFor(InstallRoles.Headless) | ModListEntryScope.Server;
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

    //
    // OPEN-44 Q14 superseded OPEN-46's set-aside client half: a hosting headless takes a Server +
    // Client mod whole, both halves live, because half a mod is a state nobody troubleshoots against.
    //

    [Theory]
    [InlineData(ModListOrigin.Server)]
    [InlineData(ModListOrigin.Local)]
    public void AServerAndClientModIsInstalledWhole(ModListOrigin origin)
    {
        var plan = ModListPlanner.Build(List(origin, Entry("DynamicMaps", ServerAndClient)), [], machine: Dedicated);

        Assert.Equal(ModListActionKind.Install, Only(plan, "DynamicMaps").Kind);
    }

    [Theory]
    [InlineData(ModListOrigin.Server)]
    [InlineData(ModListOrigin.Local)]
    public void AnInstalledWholeModIsKept(ModListOrigin origin)
    {
        var plan = ModListPlanner.Build(
            List(origin, Entry("DynamicMaps", ServerAndClient)), [TwoHalves("DynamicMaps")], machine: Dedicated);

        Assert.Equal(ModListActionKind.Keep, Only(plan, "DynamicMaps").Kind);
        Assert.True(plan.IsNoOp);
    }

    // What v1.29.0-beta and earlier left behind: client half set aside, server half live.
    [Fact]
    public void AModSplitByAnEarlierBuildIsEnabledWhole()
    {
        var plan = ModListPlanner.Build(
            List(ModListOrigin.Local, Entry("DynamicMaps", ServerAndClient)),
            [TwoHalves("DynamicMaps", clientAside: true)],
            machine: Dedicated);

        Assert.Equal(ModListActionKind.Enable, Only(plan, "DynamicMaps").Kind);
        Assert.False(plan.IsNoOp);
    }

    [Fact]
    public void ADisabledServerAndClientModIsEnabledWhole()
    {
        var plan = ModListPlanner.Build(
            List(ModListOrigin.Local, Entry("DynamicMaps", ServerAndClient)),
            [TwoHalves("DynamicMaps", clientAside: true, serverAside: true)],
            machine: Dedicated);

        Assert.Equal(ModListActionKind.Enable, Only(plan, "DynamicMaps").Kind);
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

        Assert.Equal(ModListActionKind.Keep, Only(plan, "SAIN").Kind);
    }

    [Fact]
    public void APlayerIsUnchanged()
    {
        var list = List(ModListOrigin.Local, Entry("DynamicMaps", ServerAndClient), Entry("HUD", ModListEntryScope.Client));

        var plan = ModListPlanner.Build(list, [TwoHalves("DynamicMaps"), ClientOnly("HUD")], machine: Player);

        Assert.All(plan.Actions, a => Assert.Equal(ModListActionKind.Keep, a.Kind));
    }

    [Fact]
    public void AMachineClaimingToPlayAndHostIsPlannedAsAPlayer()
    {
        var list = List(ModListOrigin.Server, Entry("DynamicMaps", ServerAndClient), Entry("HUD", ModListEntryScope.Client));

        var plan = ModListPlanner.Build(list, [TwoHalves("DynamicMaps"), ClientOnly("HUD")], machine: PlaysAndHosts);

        Assert.All(plan.Actions, a => Assert.Equal(ModListActionKind.Keep, a.Kind));
    }

    //
    // OPEN-44 Q11, made whole by Q14: a served list names a mod and scopes it off the players. A
    // player who has it enabled gets the whole mod disabled - if it has a client half. A server-only
    // one is inert on a client and may be its own solo server, so it is left alone, and a mod the
    // list never names is never touched.
    //
    [Fact]
    public void AServedEntryScopedOffAPlayerIsDisabledWhole()
    {
        var list = List(
            ModListOrigin.Server,
            Entry("Bots", ModListEntryScope.Server | ModListEntryScope.Headless),
            Entry("fika-server", ModListEntryScope.Server),
            Entry("HeadlessTweaks", ModListEntryScope.Headless));

        var serverOnly = new ModListCandidate
        {
            Name = "fika-server",
            Version = "1.0.0",
            Folders = ["fika-server"],
            Entries = [Half(InstalledModTarget.Server, "fika-server", disabled: false)],
        };

        var plan = ModListPlanner.Build(
            list,
            [TwoHalves("Bots"), serverOnly, ClientOnly("HeadlessTweaks"), ClientOnly("A sound pack")],
            machine: Player);

        Assert.Equal(["Bots", "HeadlessTweaks"], plan.Disable.Select(a => a.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(plan.Actions, a => a.Name == "fika-server");
        Assert.DoesNotContain(plan.Actions, a => a.Name == "A sound pack");
    }

    // Already disabled asks for nothing; a split one is disabled the rest of the way; pinned stays pinned.
    [Fact]
    public void AServedEntryScopedOffAPlayerLeavesADisabledOrPinnedModAlone()
    {
        var list = List(ModListOrigin.Server, Entry("Bots", ModListEntryScope.Server | ModListEntryScope.Headless));

        Assert.Empty(ModListPlanner.Build(list, [TwoHalves("Bots", clientAside: true, serverAside: true)], machine: Player).Actions);

        Assert.Equal(ModListActionKind.Disable, Only(ModListPlanner.Build(list, [TwoHalves("Bots", clientAside: true)], machine: Player), "Bots").Kind);

        var pinned = Only(ModListPlanner.Build(list, [TwoHalves("Bots")], neverAutoDisable: new HashSet<string> { "bots" }, machine: Player), "Bots");
        Assert.Equal(ModListActionKind.Pinned, pinned.Kind);
    }

    //
    // The applier.
    //

    private static Task<ModListFetchOutcome> NoFetch(IReadOnlyList<ModListAction> fetches, CancellationToken _) =>
        Task.FromResult(new ModListFetchOutcome(fetches, [], false));

    [Fact]
    public async Task ASplitModIsEnabledWholeOnApply()
    {
        var mod = TwoHalves("DynamicMaps", clientAside: true);
        var plan = ModListPlanner.Build(List(ModListOrigin.Local, Entry("DynamicMaps", ServerAndClient)), [mod], machine: Dedicated);

        var result = await ModListApplier.ApplyAsync(plan, [mod], NoFetch, new ModListApplyOptions { InstallPath = _install });

        Assert.True(result.Completed);
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Client, "DynamicMaps", false)));
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Server, "DynamicMaps", false)));
        Assert.False(Directory.Exists(Folder(InstalledModTarget.Client, "DynamicMaps", true)));
    }

    [Fact]
    public async Task AFreshInstallIsLeftWhole()
    {
        var plan = ModListPlanner.Build(List(ModListOrigin.Server, Entry("DynamicMaps", ServerAndClient)), [], machine: Dedicated);

        var result = await ModListApplier.ApplyAsync(
            plan,
            [],
            (fetches, _) =>
            {
                TwoHalves("DynamicMaps");
                return Task.FromResult(new ModListFetchOutcome(fetches, [], false));
            },
            new ModListApplyOptions { InstallPath = _install });

        Assert.True(result.Completed);
        Assert.Empty(result.Disabled.Moved);
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Client, "DynamicMaps", false)));
        Assert.True(Directory.Exists(Folder(InstalledModTarget.Server, "DynamicMaps", false)));
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
