using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ProfileStatsTests : IDisposable
{
    private const string Id = "6a6f1df7c585f25ad04523b7";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-stats-" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly string _server;

    public ProfileStatsTests()
    {
        _game = Path.Combine(_root, "Game");
        _server = Path.Combine(_game, "SPT_Runtime");
        Directory.CreateDirectory(_server);
        File.WriteAllText(Path.Combine(_game, "EscapeFromTarkov.exe"), "game");
        File.WriteAllText(Path.Combine(_server, "SPT.Server.exe"), "server");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string ProfileFile => Path.Combine(_server, "user", "profiles", Id + ".json");

    private static void Write(string full, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static string Profile(string pmcCounters = "", string scavCounters = "", string items = "", long inGame = 0, long scavInGame = 0) => $$"""
        { "info": { "id": "{{Id}}", "username": "test", "edition": "SPT Developer", "wipe": false },
          "characters": {
            "pmc": {
              "Info": { "Nickname": "Test", "Side": "Bear", "Level": 69, "Experience": 19926995, "RegistrationDate": 1785667134 },
              "Inventory": { "items": [ {{items}} ] },
              "Stats": { "Eft": { "TotalInGameTime": {{inGame}}, "OverallCounters": { "Items": [ {{pmcCounters}} ] } } }
            },
            "scav": {
              "Info": { "Nickname": "Scav", "Side": "Savage", "Level": 1 },
              "Stats": { "Eft": { "TotalInGameTime": {{scavInGame}}, "OverallCounters": { "Items": [ {{scavCounters}} ] } } }
            }
          } }
        """;

    private const string PlayedPmc = """
        { "Key": ["Sessions", "Pmc"], "Value": 10 },
        { "Key": ["ExitStatus", "Survived", "Pmc"], "Value": 4 },
        { "Key": ["ExitStatus", "Transit", "Pmc"], "Value": 1 },
        { "Key": ["ExitStatus", "Killed", "Pmc"], "Value": 3 },
        { "Key": ["ExitStatus", "Left", "Pmc"], "Value": 1 },
        { "Key": ["ExitStatus", "Runner", "Pmc"], "Value": 1 },
        { "Key": ["Kills", "Pmc"], "Value": 7 },
        { "Key": ["Kills"], "Value": 9 },
        { "Key": ["Deaths"], "Value": 3 },
        { "Key": ["LongestWinStreak", "Pmc"], "Value": 2 }
        """;

    private SptProfileStats Stats() => Assert.IsType<SptProfileStats>(SptLocalProfiles.ReadStats(_game, Id));

    [Fact]
    public void Who_the_profile_is_comes_from_the_pmc()
    {
        Write(ProfileFile, Profile());

        var stats = Stats();

        Assert.Equal((Id, "Test", "Bear", "SPT Developer", 69, 19926995L), (stats.ProfileId, stats.Nickname, stats.Side, stats.Edition, stats.Level, stats.Experience));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1785667134), stats.Registered);
    }

    [Fact]
    public void Counters_are_matched_as_the_server_matches_them()
    {
        Write(ProfileFile, Profile(PlayedPmc, inGame: 6000));

        var pmc = Stats().Pmc;

        Assert.Equal(10, pmc.Raids);
        Assert.Equal(5, pmc.Survived);
        Assert.Equal(3, pmc.Killed);
        Assert.Equal(1, pmc.Awol);
        Assert.Equal(1, pmc.RunThrough);
        Assert.Equal(0, pmc.Mia);
        Assert.Equal(7, pmc.Kills);
        Assert.Equal(3, pmc.Deaths);
        Assert.Equal(2, pmc.LongestWinStreak);
        Assert.Equal(2.33, pmc.KillDeathRatio);
        Assert.Equal(50, pmc.SurvivalRatePercent);
        Assert.Equal(10, pmc.LeaveRatePercent);
        Assert.Equal(TimeSpan.FromMinutes(10), pmc.AverageLife);
        Assert.Equal(TimeSpan.FromSeconds(6000), pmc.Online);
    }

    [Fact]
    public void Overall_adds_the_scav_and_keeps_the_better_streak()
    {
        Write(ProfileFile, Profile(PlayedPmc, """
            { "Key": ["Sessions", "Scav"], "Value": 2 },
            { "Key": ["LongestWinStreak", "Scav"], "Value": 5 }
            """, inGame: 6000, scavInGame: 1200));

        var overall = Stats().Overall;

        Assert.Equal(12, overall.Raids);
        Assert.Equal(5, overall.LongestWinStreak);
        Assert.Equal(7200, overall.InGameSeconds);
    }

    [Fact]
    public void A_profile_with_no_raids_has_no_rates()
    {
        Write(ProfileFile, Profile());

        var pmc = Stats().Pmc;

        Assert.Equal(0, pmc.Raids);
        Assert.Null(pmc.SurvivalRatePercent);
        Assert.Null(pmc.LeaveRatePercent);
        Assert.Null(pmc.AverageLife);
        Assert.Equal(0, pmc.KillDeathRatio);
    }

    [Fact]
    public void Kd_is_the_kills_when_there_are_no_deaths()
    {
        Write(ProfileFile, Profile("""{ "Key": ["Sessions", "Pmc"], "Value": 1 }, { "Key": ["Kills"], "Value": 4 }"""));

        Assert.Equal(4, Stats().Pmc.KillDeathRatio);
    }

    [Fact]
    public void Money_is_counted_across_the_whole_pmc_inventory()
    {
        Write(ProfileFile, Profile(items: $$"""
            { "_id": "a", "_tpl": "{{SptMoney.RoublesTemplate}}", "parentId": "stash", "upd": { "StackObjectsCount": 500 } },
            { "_id": "b", "_tpl": "{{SptMoney.RoublesTemplate}}", "parentId": "backpack", "upd": { "StackObjectsCount": 250 } },
            { "_id": "c", "_tpl": "{{SptMoney.DollarsTemplate}}", "parentId": "stash" },
            { "_id": "d", "_tpl": "{{SptMoney.EurosTemplate}}", "upd": { "StackObjectsCount": 3000000000 } },
            { "_id": "e", "_tpl": "{{SptMoney.GpTemplate}}", "upd": { "StackObjectsCount": 12 } },
            { "_id": "f", "_tpl": "5c0e874186f7745dc7616606", "upd": { "StackObjectsCount": 99 } }
            """));

        Assert.Equal(new SptMoney(750, 1, 3_000_000_000, 12), Stats().Money);
    }

    [Fact]
    public void A_half_written_file_keeps_the_figures_last_read()
    {
        Write(ProfileFile, Profile(PlayedPmc));
        Assert.Equal(10, Stats().Pmc.Raids);

        Write(ProfileFile, """{ "info": { "id": "6a6f1df7c585f25ad04523b7", "usern""");
        File.SetLastWriteTimeUtc(ProfileFile, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(10, Stats().Pmc.Raids);
        Assert.Single(SptLocalProfiles.Read(_game));
    }

    [Fact]
    public void An_unknown_profile_has_no_stats()
    {
        Write(ProfileFile, Profile());

        Assert.Null(SptLocalProfiles.ReadStats(_game, "6a0000000000000000000000"));
    }

    [Fact]
    public void The_level_table_is_read_from_globals()
    {
        Write(Path.Combine(_server, "SPT_Data", "database", "globals.json"), """
            { "config": { "exp": { "level": { "exp_table": [ { "exp": 0 }, { "exp": 1000 }, { "exp": 3017 }, { "exp": 5000 } ] } } } }
            """);

        var table = Assert.IsType<SptLevelTable>(SptLevelTable.Read(_game));

        Assert.Equal(3, table.MaxLevel);
        Assert.Equal(1000, table.ExperienceFor(2));
        Assert.Equal(4017, table.ExperienceFor(3));
        Assert.Equal(9017, table.ExperienceFor(10));
    }

    [Fact]
    public void Progress_is_through_the_current_level()
    {
        var table = new SptLevelTable([0, 1000, 3017, 5000]);

        var progress = table.Progress(2, 2000);

        Assert.Equal((1000L, 4017L, 2017L), (progress.LevelStart, progress.NextLevelStart, progress.ToNextLevel));
        Assert.Equal(1000d / 3017, progress.Fraction, 6);
        Assert.False(progress.IsMaxLevel);
    }

    [Fact]
    public void The_last_level_is_full()
    {
        var progress = new SptLevelTable([0, 1000, 3017, 5000]).Progress(3, 9500);

        Assert.True(progress.IsMaxLevel);
        Assert.Equal(0, progress.ToNextLevel);
        Assert.Equal(1, progress.Fraction);
    }

    [Fact]
    public void The_servers_figures_make_the_same_progress()
    {
        var server = new SptMiniProfile { Level = 69, CurrentExperience = 19926995, PreviousExperience = 19926895, NextLevelExperience = 22926895, MaxLevel = 78 };

        var progress = Assert.IsType<SptLevelProgress>(SptLevelProgress.FromServer(server));

        Assert.Equal(2999900, progress.ToNextLevel);
        Assert.Null(SptLevelProgress.FromServer(new SptMiniProfile { Level = 69 }));
    }
}
