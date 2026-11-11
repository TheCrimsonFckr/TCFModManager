using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class ProblemReportTests
{
    private static readonly ProblemSetup Setup = new()
    {
        AppVersion = "1.28.0-beta",
        UiLanguage = "de",
        SptVersion = "4.1.6",
        SptLayout = "SPT_Runtime",
        Headless = false,
        SptInstallPath = @"G:\SPT",
        WindowsVersion = new Version(10, 0, 26100, 0),
        Is64Bit = true,
        Runtime = ".NET 9.0.10",
        MonitorMode = true,
    };

    private static ProblemReport Build(ProblemSource source, Exception? exception = null, string? shown = null) =>
        ProblemReport.Build(new ProblemContext { Source = source, Setup = Setup, Exception = exception, Shown = shown });

    private static T Thrown<T>(T exception) where T : Exception
    {
        try
        {
            throw exception;
        }
        catch (T caught)
        {
            return caught;
        }
    }

    [Fact]
    public void An_install_failure_names_its_reason_and_only_the_values_it_carries()
    {
        var report = Build(ProblemSource.Download, new ModInstallException(ModInstallFailure.UnsafeArchiveEntry)
        {
            ModName = "Foo",
            Version = "1.2.3",
            ArchiveEntry = "../evil.dll",
        });

        Assert.Equal("Install failed: UnsafeArchiveEntry", report.Title);
        Assert.Contains("Reason: ModInstallFailure.UnsafeArchiveEntry\n", report.Details);
        Assert.Contains("  ModName: Foo\n", report.Details);
        Assert.Contains("  Version: 1.2.3\n", report.Details);
        Assert.Contains("  ArchiveEntry: ../evil.dll\n", report.Details);
        Assert.DoesNotContain("PlacedFiles", report.Details);
        Assert.DoesNotContain("Action", report.Details);
        Assert.DoesNotContain("Running", report.Details);
    }

    [Fact]
    public void Install_in_use_carries_the_action_and_what_is_running()
    {
        var report = Build(ProblemSource.Download, new ModInstallException(ModInstallFailure.InstallInUse)
        {
            Action = ModInstallAction.Remove,
            Running = ["SPT.Server", "EscapeFromTarkov"],
        });

        Assert.Contains("  Action: Remove\n", report.Details);
        Assert.Contains("  Running: SPT.Server, EscapeFromTarkov\n", report.Details);
    }

    [Fact]
    public void A_download_that_failed_without_a_reason_is_named_by_its_exception()
    {
        var report = Build(ProblemSource.Download, Thrown(new HttpRequestException("timed out")));

        Assert.Equal("Install failed: HttpRequestException", report.Title);
        Assert.Contains("Exception:\nSystem.Net.Http.HttpRequestException: timed out", report.Details);
    }

    [Fact]
    public void A_download_that_failed_with_only_a_sentence_is_titled_plainly()
    {
        var report = Build(ProblemSource.Download, shown: "Foo 1.2.3 has no file to download.");

        Assert.Equal("Install failed", report.Title);
        Assert.Null(report.Details);
    }

    [Fact]
    public void An_app_update_failure_names_its_reason()
    {
        var report = Build(ProblemSource.AppUpdate, new AppUpdateException(AppUpdateFailure.NotEnoughFreeSpace)
        {
            DriveName = "C:",
            RequiredBytes = 200_000_000,
            AvailableBytes = 1_000,
        });

        Assert.Equal("App update failed: NotEnoughFreeSpace", report.Title);
        Assert.Contains("Reason: AppUpdateFailure.NotEnoughFreeSpace\n", report.Details);
        Assert.Contains("  DriveName: C:\n  RequiredBytes: 200000000\n  AvailableBytes: 1000\n", report.Details);
    }

    [Fact]
    public void A_mod_list_that_failed_to_fetch_says_how_many()
    {
        var report = ProblemReport.Build(new ProblemContext
        {
            Source = ProblemSource.ModList,
            Setup = Setup,
            ListResult = ListResult(ModListStop.FetchFailed) with
            {
                FailedFetches = 2,
                Fetched = new ModListFetchOutcome(
                    [],
                    [new ModListFetchFailure("Foo", "Server error 504."), new ModListFetchFailure("Bar", "No file to download.")],
                    false),
            },
        });

        Assert.Equal("Mod list apply stopped: FetchFailed", report.Title);
        Assert.Equal(
            "Reason: ModListStop.FetchFailed\n  FailedFetches: 2\n  Failed: Foo - Server error 504.\n  Failed: Bar - No file to download.\n",
            report.Details);
    }

    [Fact]
    public void A_mod_list_whose_moves_were_refused_carries_the_refusal()
    {
        var refusal = Thrown(new ModInstallException(ModInstallFailure.InstallInUse)
        {
            Action = ModInstallAction.ApplyList,
            Running = ["SPT.Server"],
        });

        var report = ProblemReport.Build(new ProblemContext
        {
            Source = ProblemSource.ModList,
            Setup = Setup,
            ListResult = ListResult(ModListStop.MovesRefused) with { Refusal = refusal },
        });

        Assert.Equal("Mod list apply stopped: MovesRefused", report.Title);
        Assert.StartsWith(
            "Reason: ModListStop.MovesRefused\n  Reason: ModInstallFailure.InstallInUse\n    Action: ApplyList\n    Running: SPT.Server\n",
            report.Details);
        Assert.Contains("Exception:\nTCFModManager.Core.Services.ModInstallException: InstallInUse", report.Details);
    }

    [Fact]
    public void A_crash_carries_the_stack_and_the_inner_exception()
    {
        var crash = Thrown(new InvalidOperationException("outer boom", new IOException("inner boom")));

        var report = Build(ProblemSource.Crash, crash);

        Assert.Equal("Unhandled exception: InvalidOperationException", report.Title);
        Assert.Contains("outer boom", report.Details);
        Assert.Contains("System.IO.IOException: inner boom", report.Details);
        Assert.Contains(nameof(Thrown), report.Details);
    }

    [Fact]
    public void A_crash_from_the_last_run_is_named_from_its_text()
    {
        var report = ProblemReport.Build(new ProblemContext
        {
            Source = ProblemSource.PreviousCrash,
            Setup = Setup,
            ExceptionText = "System.IO.IOException: disk full\r\n   at Somewhere()",
        });

        Assert.Equal("Closed unexpectedly: IOException", report.Title);
        Assert.Equal("Exception:\nSystem.IO.IOException: disk full\n   at Somewhere()\n", report.Details);
    }

    [Fact]
    public void A_report_from_help_leaves_what_happened_empty()
    {
        var report = Build(ProblemSource.Help);

        Assert.Equal("", report.Title);
        Assert.Null(report.Details);
        Assert.Contains("**What happened**\n\n**Logs**\n", report.Body("https://codepaste.sp-mod.com/?a#b"));
    }

    [Fact]
    public void The_body_keeps_the_template_headings_in_order()
    {
        var body = Build(ProblemSource.Crash, Thrown(new IOException("x")), "Something went wrong.").Body(null);

        var steps = body.IndexOf("**Steps to reproduce**\n1. \n", StringComparison.Ordinal);
        var expected = body.IndexOf("**What you expected**\n", StringComparison.Ordinal);
        var happened = body.IndexOf("**What happened**\n", StringComparison.Ordinal);
        var logs = body.IndexOf("**Logs**\n", StringComparison.Ordinal);

        Assert.Equal(0, steps);
        Assert.True(steps < expected && expected < happened && happened < logs);
    }

    [Fact]
    public void What_the_user_saw_is_quoted_line_by_line()
    {
        var body = Build(ProblemSource.Download, new ModInstallException(ModInstallFailure.NoInstallFolder), "Line one.\r\nLine two.")
            .Body(null);

        Assert.Contains("Install failed: NoInstallFolder\n> Line one.\n> Line two.\n", body);
    }

    [Fact]
    public void With_a_link_the_logs_section_holds_the_versions_and_the_link()
    {
        var report = Build(ProblemSource.Help);

        var body = report.Body("https://codepaste.sp-mod.com/?abc#key");

        Assert.EndsWith(
            "**Logs**\nTCF Mod Manager 1.28.0-beta · SPT 4.1.6 · Windows 10.0.26100\nhttps://codepaste.sp-mod.com/?abc#key\n",
            body);
        Assert.DoesNotContain("```", body);
    }

    [Fact]
    public void Without_a_link_the_logs_text_is_inline_in_a_fence()
    {
        var report = Build(ProblemSource.Help);

        var body = report.Body(null);

        Assert.Contains("**Logs**\nTCF Mod Manager 1.28.0-beta · SPT 4.1.6 · Windows 10.0.26100\n```text\nApp\n", body);
        Assert.Contains(report.LogsText, body);
        Assert.EndsWith("```\n", body);
    }

    [Fact]
    public void The_logs_text_holds_the_setup_sections()
    {
        var logs = Build(ProblemSource.Help).LogsText;

        Assert.StartsWith(
            "App\n  Version: 1.28.0-beta\n  Single file: no\n  Language: de\n"
            + "SPT\n  Version: 4.1.6\n  Layout: SPT_Runtime\n  Headless: no\n  Install: G:\\SPT\n"
            + "Windows\n  Version: 10.0.26100\n  64-bit: yes\n  .NET: .NET 9.0.10\n"
            + "Settings\n  Monitor mode: on\n  Mod page gate: off\n  Server Map: not connected\n",
            logs);
        Assert.DoesNotContain("Installed mods", logs);
        Assert.DoesNotContain("Log\n", logs);
    }

    [Fact]
    public void Installed_mods_are_listed_only_when_given()
    {
        var logs = ProblemReport.Build(new ProblemContext
        {
            Source = ProblemSource.Help,
            Setup = Setup,
            InstalledMods = [new ProblemMod("Foo", "1.0.0", "12", false), new ProblemMod("Bar", null, null, true), new ProblemMod("Foo Extra", "0.3.0", "77", false, IsAddon: true)],
        }).LogsText;

        Assert.Contains("Installed mods (3)\n  Foo 1.0.0 (sp-mod 12)\n  Bar disabled\n  Foo Extra 0.3.0 (sp-mod addon 77)\n", logs);
    }

    [Fact]
    public void A_fence_is_always_longer_than_any_backtick_run_inside_it()
    {
        Assert.StartsWith("```text\n", ProblemReport.Fenced("no backticks"));
        Assert.StartsWith("```text\n", ProblemReport.Fenced("a `` b"));
        Assert.StartsWith("````text\n", ProblemReport.Fenced("a ``` b"));
        Assert.Equal("``````text\na ````` b\n``````\n", ProblemReport.Fenced("a ````` b\n"));
    }

    [Fact]
    public void A_log_with_a_fence_in_it_cannot_close_the_report_fence_early()
    {
        var report = ProblemReport.Build(new ProblemContext
        {
            Source = ProblemSource.Help,
            Setup = Setup,
            Log = "INFO first\n```\nINFO after",
        });

        Assert.Contains("````text\n", report.Body(null));
    }

    [Fact]
    public void Everything_in_the_report_is_redacted_and_uses_lf()
    {
        var report = ProblemReport.Build(new ProblemContext
        {
            Source = ProblemSource.Crash,
            Setup = Setup with { SptInstallPath = @"C:\Users\jdoe\SPT" },
            Exception = Thrown(new IOException(@"C:\Users\jdoe\x.json is locked on DESKTOP-AB12")),
            Shown = @"Could not open C:\Users\jdoe\x.json",
            Log = "2026-11-11 INFO started on DESKTOP-AB12\r\n2026-11-11 INFO user jdoe\r\n",
            Redaction = new RedactionValues
            {
                UserProfile = @"C:\Users\jdoe",
                UserName = "jdoe",
                MachineName = "DESKTOP-AB12",
            },
        });

        var body = report.Body(null);

        Assert.DoesNotContain("jdoe", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DESKTOP-AB12", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\r", body);
        Assert.Contains(@"%USERPROFILE%\x.json is locked on <machine>", body);
        Assert.Contains(@"  Install: %USERPROFILE%\SPT", report.LogsText);
        Assert.EndsWith("Log\n2026-11-11 INFO started on <machine>\n2026-11-11 INFO user <user>\n", report.LogsText);
    }

    private static ModListApplyResult ListResult(ModListStop stop) => new()
    {
        ListId = Guid.NewGuid(),
        Enabled = ModDisableOutcome.Empty,
        Disabled = ModDisableOutcome.Empty,
        Fetched = ModListFetchOutcome.Empty,
        Manual = [],
        Stopped = stop,
    };
}
