using System.Globalization;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

/// <summary>One figure on the Profile card: its label and its value, worded.</summary>
public sealed record ProfileStatCell(string Label, string Value);

/// <summary>The Profile card's figures for one profile (OPEN-27), worded for the current language.</summary>
public sealed class ProfileStatsView
{
    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);

    private ProfileStatsView(SptProfileStats stats, string fallbackName, SptLevelProgress? progress, bool levelFromServer, DateTimeOffset now)
    {
        ProfileId = stats.ProfileId;
        Name = stats.Nickname.Length > 0 ? stats.Nickname : fallbackName;
        Detail = Text(Strings.Play_ProfileDetailFormat, stats.Level, stats.Side.ToUpperInvariant(), stats.Edition);

        Roubles = Count(stats.Money.Roubles);
        Dollars = Count(stats.Money.Dollars);
        Euros = Count(stats.Money.Euros);
        Gp = Count(stats.Money.Gp);

        LevelText = Text(Strings.Profile_LevelFormat, stats.Level);
        ExperienceText = Text(Strings.Profile_ExperienceFormat, progress?.Experience ?? stats.Experience);
        HasProgress = progress is not null;
        if (progress is not null)
        {
            ToNextText = progress.IsMaxLevel ? Strings.Profile_MaxLevel : Text(Strings.Profile_ToNextFormat, progress.ToNextLevel, progress.Level + 1);
            LevelPercent = progress.Fraction * 100;
            LevelSourceToolTip = levelFromServer ? Strings.Profile_LevelFromServerToolTip : Strings.Profile_LevelFromFileToolTip;
        }

        var lifetime = Lifetime(stats.Registered, now);
        OverallRows = Rows(stats.Overall, lifetime);
        PmcRows = Rows(stats.Pmc, lifetime);
        ScavRows = Rows(stats.Scav, lifetime);
    }

    public static ProfileStatsView Build(SptProfileStats stats, string fallbackName, SptLevelProgress? progress, bool levelFromServer) =>
        new(stats, fallbackName, progress, levelFromServer, DateTimeOffset.UtcNow);

    public string ProfileId { get; }

    public string Name { get; }

    public string Detail { get; }

    public string Roubles { get; }

    public string Dollars { get; }

    public string Euros { get; }

    public string Gp { get; }

    public string LevelText { get; }

    public string ExperienceText { get; }

    public bool HasProgress { get; }

    public string ToNextText { get; } = "";

    public double LevelPercent { get; }

    public string? LevelSourceToolTip { get; }

    public IReadOnlyList<ProfileStatCell> OverallRows { get; }

    public IReadOnlyList<ProfileStatCell> PmcRows { get; }

    public IReadOnlyList<ProfileStatCell> ScavRows { get; }

    private static IReadOnlyList<ProfileStatCell> Rows(SptCharacterStats s, string lifetime) =>
    [
        new(Strings.Profile_StatRaids, Count(s.Raids)),
        new(Strings.Profile_StatSurvived, Count(s.Survived)),
        new(Strings.Profile_StatKia, Count(s.Killed)),
        new(Strings.Profile_StatRunThrough, Count(s.RunThrough)),
        new(Strings.Profile_StatAwol, Count(s.Awol)),
        new(Strings.Profile_StatMia, Count(s.Mia)),
        new(Strings.Profile_StatKills, Count(s.Kills)),
        new(Strings.Profile_StatKd, s.KillDeathRatio.ToString("0.##", CultureInfo.CurrentCulture)),
        new(Strings.Profile_StatSurvivalRate, Percent(s.SurvivalRatePercent)),
        new(Strings.Profile_StatLeaveRate, Percent(s.LeaveRatePercent)),
        new(Strings.Profile_StatAverageLife, s.AverageLife is { } life ? Clock(life, withSeconds: true) : Strings.Profile_NoValue),
        new(Strings.Profile_StatSurvivalStreak, Count(s.LongestWinStreak)),
        new(Strings.Profile_StatTimeInRaid, Clock(s.Online, withSeconds: false)),
        new(Strings.Profile_StatAccountAge, lifetime),
    ];

    private static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string Percent(int? value) => value is { } v ? Text(Strings.Profile_PercentFormat, v) : Strings.Profile_NoValue;

    private static string Clock(TimeSpan span, bool withSeconds)
    {
        var hours = (long)span.TotalHours;
        var separator = CultureInfo.CurrentCulture.DateTimeFormat.TimeSeparator;
        if (!withSeconds) return hours + separator + span.Minutes.ToString("00", CultureInfo.CurrentCulture);

        var minutesAndSeconds = span.Minutes.ToString("00", CultureInfo.CurrentCulture) + separator + span.Seconds.ToString("00", CultureInfo.CurrentCulture);
        return hours > 0 ? hours + separator + minutesAndSeconds : ((long)span.TotalMinutes).ToString(CultureInfo.CurrentCulture) + separator + span.Seconds.ToString("00", CultureInfo.CurrentCulture);
    }

    private static string Lifetime(DateTimeOffset? registered, DateTimeOffset now)
    {
        if (registered is not { } since || since > now) return Strings.Profile_NoValue;

        var age = now - since;
        if (age <= TimeSpan.FromHours(48)) return Text(Strings.Profile_LifetimeHoursFormat, (int)age.TotalHours, age.Minutes);
        if (age <= TimeSpan.FromDays(365)) return Text(Strings.Profile_LifetimeDaysFormat, (int)age.TotalDays);

        var years = (int)(age.TotalDays / 365);
        return Text(Strings.Profile_LifetimeYearsFormat, years, (int)(age.TotalDays - 365 * years));
    }
}
