using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-73: what Purge logs on Diagnose logs takes away, on SPT 4.1's layout as on Chris's G: install.
public class LogPurgeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-purge-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Write(string relative, string content = "x")
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public void Takes_the_server_game_and_BepInEx_logs_and_leaves_the_rest()
    {
        Write("EscapeFromTarkov.exe");
        Write("SPT_Runtime/SPT.Server.exe");
        var older = Write("SPT_Runtime/user/logs/spt/spt20261125.log", "12345");
        var newer = Write("SPT_Runtime/user/logs/spt/spt20261126.log", "123");
        var launcher = Write("SPT_Runtime/user/logs/Launcher.log");
        Write("Logs/log_2026.11.25_16-21-42_0.16.9.5.40743/2026.11.25_16-21-42_0.16.9.5.40743 errors.log", "12");
        Write("Logs/log_2026.11.25_16-21-42_0.16.9.5.40743/2026.11.25_16-21-42_0.16.9.5.40743 application.log", "1234");
        var output = Write("BepInEx/LogOutput.log", "1");
        var previous = Write("BepInEx/LogOutput_prev.log", "1");
        var full = Write("BepInEx/FullLogOutput.log", "1");
        var config = Write("BepInEx/config/BepInEx.cfg");
        var installer = Write("spt-installer.log");
        var patcher = Write("Patcher.log");

        var plan = LogPurge.Plan(Path.Combine(_root, "SPT_Runtime"));

        var session = Path.Combine(_root, "Logs", "log_2026.11.25_16-21-42_0.16.9.5.40743");
        Assert.Equal(
            new[] { older, newer, session, output, previous, full }.Order(),
            plan.Paths.Order());
        Assert.Equal(7, plan.Files);
        Assert.Equal(5 + 3 + 2 + 4 + 3, plan.Bytes);
        Assert.DoesNotContain(launcher, plan.Paths);
        Assert.DoesNotContain(config, plan.Paths);
        Assert.DoesNotContain(installer, plan.Paths);
        Assert.DoesNotContain(patcher, plan.Paths);
    }

    [Fact]
    public void Nothing_to_take_from_a_fresh_or_missing_install()
    {
        Write("EscapeFromTarkov.exe");
        Write("SPT_Runtime/SPT.Server.exe");

        Assert.True(LogPurge.Plan(_root).IsEmpty);
        Assert.True(LogPurge.Plan(Path.Combine(_root, "nowhere")).IsEmpty);
        Assert.True(LogPurge.Plan(null).IsEmpty);
    }
}
