using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The Play page's server log, server port and Diagnose logs' files on the two layouts SPT ships:
// 4.0's SPT\ beside the game, and 4.1's SPT_Runtime\ (OPEN-12 F8, F16). Folder shapes as on Chris's
// two installs, 2026-10-11.
//
public class LaunchLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-layout-" + Guid.NewGuid().ToString("N"));

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

    [Theory]
    [InlineData("SPT")]
    [InlineData("SPT_Runtime")]
    public void Finds_the_server_log_port_and_game_logs(string serverFolder)
    {
        Write("EscapeFromTarkov.exe");
        var exe = Write($"{serverFolder}/SPT.Server.exe");
        Write($"{serverFolder}/SPT.Launcher.exe");
        Write($"{serverFolder}/SPT_Data/configs/http.json", "{ \"ip\": \"127.0.0.1\", \"port\": 7000 }");
        var log = Write($"{serverFolder}/user/logs/spt/spt20261011.log", "[2026-10-11 10:00:00.000][Info][SPTarkov.Server] Server starting");
        Write("BepInEx/LogOutput.log", "[Info   :   BepInEx] Loading");
        Write("Logs/log_2026.10.11_10-00-00_0.16.9/2026.10.11_10-00-00_0.16.9 errors.log", "");

        Assert.Equal(Path.Combine(_root, serverFolder, "user", "logs"), ServerLogs.Folder(_root));
        Assert.Equal(log, ServerLogs.Newest(_root));
        Assert.Contains(7000, SptServerReadiness.PortsFor(exe));
        Assert.Equal("127.0.0.1:7000", SptDirectLaunch.ServerAddress(exe));

        var files = LogFiles.Find(_root);
        Assert.Equal(log, files.Server);
        Assert.NotNull(files.BepInEx);
        Assert.NotNull(files.GameErrors);
    }

    [Theory]
    [InlineData("4.0.13", DirectLaunchSupport.Unsupported)]
    [InlineData("4.1.2", DirectLaunchSupport.Unsupported)]
    [InlineData("4.1.3", DirectLaunchSupport.Supported)]
    [InlineData("4.1.6", DirectLaunchSupport.Supported)]
    [InlineData("4.1.7", DirectLaunchSupport.NewerUntested)]
    [InlineData("4.2.0", DirectLaunchSupport.Unsupported)]
    public void Direct_launch_is_offered_on_4_1_3_and_later_in_4_1(string version, DirectLaunchSupport expected) =>
        Assert.Equal(expected, SptDirectLaunch.Support(version));
}
