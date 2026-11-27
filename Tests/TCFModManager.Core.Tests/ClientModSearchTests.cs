using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-54 N27c: the search over client mods, each try answered by the user.
public class ClientModSearchTests
{
    private const string Install = @"G:\Single Player Tarkov 4.1 Server";

    private static InstalledMod Mod(string name, InstalledModTarget target, bool patcher = false, bool disabled = false) => new()
    {
        Name = name,
        Target = target,
        IsPatcher = patcher,
        IsDisabled = disabled,
        FolderPath = target == InstalledModTarget.Server
            ? Path.Combine(Install, "SPT", "user", "mods", name)
            : Path.Combine(Install, "BepInEx", patcher ? "patchers" : "plugins", name),
    };

    [Fact]
    public void Client_suspects_are_the_turned_on_plugins_only()
    {
        IReadOnlyList<InstalledMod> scanned =
        [
            Mod("ServerHalf", InstalledModTarget.Server),
            Mod("ClientHalf", InstalledModTarget.Client),
            Mod("Patcher", InstalledModTarget.Client, patcher: true),
            Mod("Off", InstalledModTarget.Client, disabled: true),
            Mod("Other", InstalledModTarget.Client),
        ];

        var (client, _) = BrokenModSearch.Suspects(scanned, target: InstalledModTarget.Client);
        Assert.Equal(["ClientHalf", "Other"], client.Select(m => m.Name).Order());

        var (server, _) = BrokenModSearch.Suspects(scanned);
        Assert.Equal(["ServerHalf"], server.Select(m => m.Name));
    }

    [Fact]
    public async Task The_mod_the_problem_comes_with_is_found()
    {
        for (var bad = 0; bad < 12; bad++)
        {
            var result = await BrokenModSearch.RunClientAsync(12, _ => [], (on, _) => Task.FromResult(on.Contains(bad)));

            Assert.Equal(BrokenModVerdict.Found, result.Verdict);
            Assert.Equal(bad, result.Culprit);
            Assert.Null(result.Partner);
        }
    }

    [Fact]
    public async Task Working_with_every_mod_on_is_nothing_wrong()
    {
        var tries = 0;
        var result = await BrokenModSearch.RunClientAsync(5, _ => [], (_, _) => { tries++; return Task.FromResult(false); });

        Assert.Equal(BrokenModVerdict.NothingWrong, result.Verdict);
        Assert.Equal(1, tries);
    }

    [Fact]
    public async Task Breaking_with_every_mod_off_is_not_the_mods()
    {
        var result = await BrokenModSearch.RunClientAsync(5, _ => [], (_, _) => Task.FromResult(true));

        Assert.Equal(BrokenModVerdict.NotTheMods, result.Verdict);
        Assert.Equal(2, result.Tries);
    }

    [Fact]
    public async Task Each_round_is_reported_as_a_step()
    {
        var steps = new List<BrokenModStep>();
        var progress = new SyncProgress<BrokenModStep>(steps.Add);

        var result = await BrokenModSearch.RunClientAsync(8, _ => [], (on, _) => Task.FromResult(on.Contains(3)), steps: progress);

        Assert.Equal(result.Tries, steps.Count);
        Assert.Equal(BrokenModStage.AllOn, steps[0].Stage);
        Assert.True(steps[0].ShowedProblem);
        Assert.Equal(BrokenModStage.AllOff, steps[1].Stage);
        Assert.False(steps[1].ShowedProblem);
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
