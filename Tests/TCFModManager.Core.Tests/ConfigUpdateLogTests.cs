using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ConfigUpdateLogTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly string _directory;
    private readonly ConfigUpdateLog _log;

    public ConfigUpdateLogTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TCFModManagerConfigLog_" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _log = new ConfigUpdateLog(Path.Combine(_directory, "config_updates.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static ConfigUpdateReport Report(string toVersion, ConfigOutcomeKind kind) => new()
    {
        ModId = 42,
        ModName = "Test Mod",
        FromVersion = "1.0.0",
        ToVersion = toVersion,
        At = Timestamp,
        Files = [new ConfigFileOutcome { Path = "user/mods/TestMod/config.json", Kind = kind }],
    };

    [Fact]
    public void LastFor_ReturnsTheMostRecentUpdateThatMentionsTheFile()
    {
        _log.Add(Report("1.1.0", ConfigOutcomeKind.Replaced));
        _log.Add(Report("1.2.0", ConfigOutcomeKind.DefaultsUpdated));

        var last = _log.LastFor("user/mods/TestMod/config.json");

        Assert.NotNull(last);
        Assert.Equal("1.2.0", last!.Value.Report.ToVersion);
        Assert.Equal(ConfigOutcomeKind.DefaultsUpdated, last.Value.Outcome.Kind);
    }

    [Fact]
    public void LastFor_IsNullForAFileNoUpdateMentions()
    {
        _log.Add(Report("1.1.0", ConfigOutcomeKind.Replaced));

        Assert.Null(_log.LastFor("user/mods/Other/config.json"));
    }

    [Fact]
    public void AReportWithNoFilesIsNotRecorded()
    {
        _log.Add(new ConfigUpdateReport
        {
            ModId = 42,
            ModName = "Test Mod",
            ToVersion = "1.1.0",
            At = Timestamp,
        });

        Assert.False(File.Exists(_log.FilePath));
    }

    // The enums are written as names, like every other store here, so the file stays hand-readable.
    [Fact]
    public void OutcomesRoundTripThroughTheFile()
    {
        _log.Add(Report("1.1.0", ConfigOutcomeKind.Replaced));

        Assert.Contains("Replaced", File.ReadAllText(_log.FilePath));
        Assert.Equal(ConfigOutcomeKind.Replaced, _log.Load().Reports[0].Files[0].Kind);
    }
}
