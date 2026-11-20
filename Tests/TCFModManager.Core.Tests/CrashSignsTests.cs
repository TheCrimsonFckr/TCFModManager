using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// OPEN-54 N30. The sample is the Live Flea Prices crash on Chris's 4.1 install, 2026-11-09 17:53:22.
public sealed class CrashSignsTests
{
    private const string ServerExe = @"G:\Single Player Tarkov 4.1 Server\SPT_Runtime\SPT.Server.exe";
    private static readonly DateTime Started = new(2026, 11, 9, 17, 50, 1, DateTimeKind.Utc);
    private static readonly DateTime CrashedAt = new(2026, 11, 9, 17, 53, 22, DateTimeKind.Utc);

    private static readonly string?[] ApplicationError =
    [
        "SPT.Server.exe", "4.1.6.0", "68e8a2f1", "KERNELBASE.dll", "10.0.26100.6899", "c6d0b7a4", "e0434352", "00000000000c7f8a",
        "0x2a4c", "0x" + Started.ToFileTimeUtc().ToString("x16"), ServerExe, @"C:\WINDOWS\System32\KERNELBASE.dll",
        "1b3c5d7e-0000-4000-8000-000000000001", "", "",
    ];

    private const string DotNetRuntime =
        "Application: SPT.Server.exe\r\n" +
        "CoreCLR Version: 10.0.1425.47011\r\n" +
        ".NET Version: 10.0.14\r\n" +
        "Description: The process was terminated due to an unhandled exception.\r\n" +
        "Exception Info: System.Threading.Tasks.TaskCanceledException: A task was canceled.\r\n" +
        "   at System.Threading.Tasks.Task.ThrowIfExceptional(Boolean)\r\n" +
        "   at System.Net.Http.HttpClient.HandleFailure(System.Exception, Boolean, System.Net.Http.HttpResponseMessage, System.Threading.CancellationTokenSource, System.Threading.CancellationToken, System.Threading.CancellationTokenSource)\r\n" +
        "   at DrakiaXYZ.LiveFleaPrices.LiveFleaPricesMod.<>c__DisplayClass16_0.<<OnLoadAsync>b__0>d.MoveNext()\r\n" +
        "   at SPTarkov.Server.Core.Utils.App.Run()\r\n";

    private static readonly DiagnosedMod Flea = new("LiveFleaPrices", "Live Flea Prices", Path.Combine("SPT", "user", "mods", "LiveFleaPrices"), InstalledModTarget.Server, "xyz.drakia.livefleaprices");
    private static LogModLocator Mods() => LogModLocator.From([(Flea, ["LiveFleaPrices"], ["DrakiaXYZ.LiveFleaPrices"])]);

    [Fact]
    public void Windows_crash_record_is_read_and_matched_to_the_process_by_id_and_start_time()
    {
        var crash = CrashSigns.FromApplicationError(ApplicationError, CrashedAt);

        Assert.NotNull(crash);
        Assert.Equal("SPT.Server.exe", crash.App);
        Assert.Equal("KERNELBASE.dll", crash.FaultingModule);
        Assert.Equal("0xE0434352", crash.ExceptionCode);
        Assert.Equal(0x2a4c, crash.ProcessId);
        Assert.Equal(Started, crash.ProcessStartedUtc);

        Assert.True(CrashSigns.Matches(crash, 0x2a4c, Started, ServerExe));
        Assert.False(CrashSigns.Matches(crash, 0x2a4d, Started, ServerExe));
        Assert.False(CrashSigns.Matches(crash, 0x2a4c, Started.AddMinutes(1), ServerExe));
        Assert.False(CrashSigns.Matches(crash, 0x2a4c, Started, @"G:\Single Player Tarkov 4.1 Server\EscapeFromTarkov.exe"));
    }

    [Fact]
    public void Ids_written_without_0x_read_the_same()
    {
        var bare = ApplicationError.ToArray();
        bare[8] = "2a4c";
        bare[9] = Started.ToFileTimeUtc().ToString("x16");

        var crash = CrashSigns.FromApplicationError(bare, CrashedAt);

        Assert.Equal(0x2a4c, crash?.ProcessId);
        Assert.Equal(Started, crash?.ProcessStartedUtc);
    }

    [Fact]
    public void A_server_crash_names_the_mod_from_its_first_frame_outside_dotnet_and_SPT()
    {
        var windows = CrashSigns.FromApplicationError(ApplicationError, CrashedAt);
        var dotnet = CrashSigns.FromDotNetRuntime(DotNetRuntime, CrashedAt);

        Assert.True(CrashSigns.Matches(dotnet!, ServerExe, CrashedAt));
        var crash = CrashSigns.Decide(WatchedProcessKind.Server, unchecked((int)0xE0434352), false, windows, dotnet, null, Mods());

        Assert.NotNull(crash);
        Assert.Equal(CrashCause.WindowsReported, crash.Cause);
        Assert.Equal(Flea, crash.Mod);
        Assert.Equal("System.Threading.Tasks.TaskCanceledException: A task was canceled.", crash.What);
        Assert.Equal("KERNELBASE.dll", crash.Module);
    }

    [Fact]
    public void Whatever_the_app_stopped_is_never_a_crash()
    {
        var windows = CrashSigns.FromApplicationError(ApplicationError, CrashedAt);

        Assert.Null(CrashSigns.Decide(WatchedProcessKind.Server, unchecked((int)0xC0000005), true, windows, null, null, Mods()));
    }

    [Fact]
    public void Without_Windows_record_the_exit_code_decides()
    {
        Assert.Null(CrashSigns.Decide(WatchedProcessKind.Game, 0, false, null, null, null, null));
        Assert.Null(CrashSigns.Decide(WatchedProcessKind.Game, 1, false, null, null, null, null));
        Assert.Null(CrashSigns.Decide(WatchedProcessKind.Server, unchecked((int)0xC000013A), false, null, null, null, null));

        var crash = CrashSigns.Decide(WatchedProcessKind.Headless, unchecked((int)0xC0000005), false, null, null, null, null);
        Assert.Equal(CrashCause.ExitCode, crash?.Cause);
        Assert.Equal("0xC0000005", crash?.ExceptionCode);
    }

    [Fact]
    public void A_server_that_gave_up_while_starting_is_a_crash_whatever_its_exit_code()
    {
        var log = LogEntries.Parse(LogKind.Server, "spt.log", ["[2026-11-20 10:00:00.000][Critical][SPTarkov.Server.Core] The server has unexpectedly stopped"]);
        var gaveUp = new LogFinding(LogFindingKind.ServerStopped, LogSeverity.Critical, Flea, [LogDiagnoser.StopUnexpected, "Could not load file or assembly"], 1, log[0], []);

        var crash = CrashSigns.Decide(WatchedProcessKind.Server, 0, false, null, null, gaveUp, null);

        Assert.Equal(CrashCause.ServerGaveUp, crash?.Cause);
        Assert.Equal("Could not load file or assembly", crash?.What);
        Assert.Equal(Flea, crash?.Mod);
        Assert.Null(CrashSigns.Decide(WatchedProcessKind.Game, 0, false, null, null, gaveUp, null));
    }

    [Fact]
    public void A_native_module_inside_a_mod_folder_names_that_mod()
    {
        var data = ApplicationError.ToArray();
        data[0] = "EscapeFromTarkov.exe";
        data[3] = "native.dll";
        data[11] = Path.GetFullPath(Path.Combine(Flea.FolderPath, "lib", "native.dll"));

        var crash = CrashSigns.Decide(WatchedProcessKind.Game, unchecked((int)0xC0000005), false, CrashSigns.FromApplicationError(data, CrashedAt), null, null, Mods());

        Assert.Equal(Flea, crash?.Mod);
    }
}
