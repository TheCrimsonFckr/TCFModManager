using System.Text.Json;

namespace TCFModManager.Core.Services;

/// <summary>
/// What SPT's launcher shows on its profile page, read from the profile file. Counted the way SPT
/// 4.1.6's server counts it for that page (ProfileHelper.GetProfileStats / GetAccountCurrency).
/// Read through <see cref="SptLocalProfiles.ReadStats"/>.
/// </summary>
public sealed record SptProfileStats
{
    public string ProfileId { get; init; } = "";

    public string Nickname { get; init; } = "";

    public string Side { get; init; } = "";

    public string Edition { get; init; } = "";

    public int Level { get; init; }

    public long Experience { get; init; }

    public DateTimeOffset? Registered { get; init; }

    public SptMoney Money { get; init; } = SptMoney.None;

    public SptCharacterStats Pmc { get; init; } = SptCharacterStats.Empty;

    public SptCharacterStats Scav { get; init; } = SptCharacterStats.Empty;

    public SptCharacterStats Overall => SptCharacterStats.Combine(Pmc, Scav);

    internal static SptProfileStats? FromJson(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
            return null;

        var id = ProfileJson.Text(info, "id");
        if (id.Length == 0) return null;

        JsonElement pmc = default, scav = default;
        if (root.TryGetProperty("characters", out var characters) && characters.ValueKind == JsonValueKind.Object)
        {
            characters.TryGetProperty("pmc", out pmc);
            characters.TryGetProperty("scav", out scav);
        }

        var pmcInfo = ProfileJson.Object(pmc, "Info");
        var registered = ProfileJson.Int64(pmcInfo, "RegistrationDate");

        return new SptProfileStats
        {
            ProfileId = id,
            Edition = ProfileJson.Text(info, "edition"),
            Nickname = ProfileJson.Text(pmcInfo, "Nickname"),
            Side = ProfileJson.Text(pmcInfo, "Side"),
            Level = (int)ProfileJson.Int64(pmcInfo, "Level"),
            Experience = ProfileJson.Int64(pmcInfo, "Experience"),
            Registered = registered > 0 ? DateTimeOffset.FromUnixTimeSeconds(registered) : null,
            Money = SptMoney.FromInventory(ProfileJson.Object(pmc, "Inventory")),
            Pmc = SptCharacterStats.FromCharacter(pmc),
            Scav = SptCharacterStats.FromCharacter(scav),
        };
    }
}

/// <summary>One character's raid record (PMC or Scav).</summary>
public sealed record SptCharacterStats
{
    public static SptCharacterStats Empty { get; } = new();

    public long Raids { get; init; }

    public long Survived { get; init; }

    public long Killed { get; init; }

    public long Deaths { get; init; }

    public long Kills { get; init; }

    public long RunThrough { get; init; }

    public long Awol { get; init; }

    public long Mia { get; init; }

    public long LongestWinStreak { get; init; }

    public long InGameSeconds { get; init; }

    public double KillDeathRatio => Deaths > 0 ? Math.Round((double)Kills / Deaths, 2) : Kills;

    public int? SurvivalRatePercent => Percent(Survived);

    public int? LeaveRatePercent => Percent(Awol);

    public TimeSpan? AverageLife => Raids > 0 ? TimeSpan.FromSeconds((double)InGameSeconds / Raids) : null;

    public TimeSpan Online => TimeSpan.FromSeconds((double)InGameSeconds);

    private int? Percent(long part) => Raids > 0 ? (int)((double)part / Raids * 100) : null;

    public static SptCharacterStats Combine(SptCharacterStats pmc, SptCharacterStats scav) => new()
    {
        Raids = pmc.Raids + scav.Raids,
        Survived = pmc.Survived + scav.Survived,
        Killed = pmc.Killed + scav.Killed,
        Deaths = pmc.Deaths + scav.Deaths,
        Kills = pmc.Kills + scav.Kills,
        RunThrough = pmc.RunThrough + scav.RunThrough,
        Awol = pmc.Awol + scav.Awol,
        Mia = pmc.Mia + scav.Mia,
        LongestWinStreak = Math.Max(pmc.LongestWinStreak, scav.LongestWinStreak),
        InGameSeconds = pmc.InGameSeconds + scav.InGameSeconds,
    };

    internal static SptCharacterStats FromCharacter(JsonElement character)
    {
        var eft = ProfileJson.Object(ProfileJson.Object(character, "Stats"), "Eft");
        var counters = ProfileJson.Object(ProfileJson.Object(eft, "OverallCounters"), "Items");
        if (eft.ValueKind != JsonValueKind.Object) return Empty;

        return new SptCharacterStats
        {
            Raids = Counter(counters, "Sessions"),
            Survived = Counter(counters, "ExitStatus", "Survived") + Counter(counters, "ExitStatus", "Transit"),
            Killed = Counter(counters, "ExitStatus", "Killed"),
            Deaths = Counter(counters, "Deaths"),
            Kills = Counter(counters, "Kills"),
            RunThrough = Counter(counters, "ExitStatus", "Runner"),
            Awol = Counter(counters, "ExitStatus", "Left"),
            Mia = Counter(counters, "ExitStatus", "MissingInAction"),
            LongestWinStreak = Counter(counters, "LongestWinStreak"),
            InGameSeconds = ProfileJson.Int64(eft, "TotalInGameTime"),
        };
    }

    internal static long Counter(JsonElement items, params string[] tags)
    {
        if (items.ValueKind != JsonValueKind.Array) return 0;

        foreach (var counter in items.EnumerateArray())
        {
            if (counter.ValueKind != JsonValueKind.Object
                || !counter.TryGetProperty("Key", out var key) || key.ValueKind != JsonValueKind.Array)
                continue;

            var keys = key.EnumerateArray()
                .Where(k => k.ValueKind == JsonValueKind.String)
                .Select(k => k.GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            if (!keys.IsSupersetOf(tags)) continue;

            return ProfileJson.Int64(counter, "Value");
        }

        return 0;
    }
}

/// <summary>Currency across the PMC's whole inventory (stash, gear and all), as the launcher counts it.</summary>
public sealed record SptMoney(long Roubles, long Dollars, long Euros, long Gp)
{
    public const string RoublesTemplate = "5449016a4bdc2d6f028b456f";
    public const string DollarsTemplate = "5696686a4bdc2da3298b456a";
    public const string EurosTemplate = "569668774bdc2da2298b4568";
    public const string GpTemplate = "5d235b4d86f7742e017bc88a";

    public static SptMoney None { get; } = new(0, 0, 0, 0);

    internal static SptMoney FromInventory(JsonElement inventory)
    {
        var items = ProfileJson.Object(inventory, "items");
        if (items.ValueKind != JsonValueKind.Array) return None;

        long roubles = 0, dollars = 0, euros = 0, gp = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var template = ProfileJson.Text(item, "_tpl");
            if (template is not (RoublesTemplate or DollarsTemplate or EurosTemplate or GpTemplate)) continue;

            var upd = ProfileJson.Object(item, "upd");
            var stack = upd.ValueKind == JsonValueKind.Object && upd.TryGetProperty("StackObjectsCount", out var count) && count.ValueKind == JsonValueKind.Number
                ? ProfileJson.Int64(upd, "StackObjectsCount")
                : 1;

            switch (template)
            {
                case RoublesTemplate: roubles += stack; break;
                case DollarsTemplate: dollars += stack; break;
                case EurosTemplate: euros += stack; break;
                default: gp += stack; break;
            }
        }

        return new SptMoney(roubles, dollars, euros, gp);
    }
}

/// <summary>Where a level's EXP sits between the start of that level and the start of the next.</summary>
public sealed record SptLevelProgress(int Level, long Experience, long LevelStart, long NextLevelStart, int MaxLevel)
{
    public bool IsMaxLevel => Level >= MaxLevel || NextLevelStart <= LevelStart;

    public long ToNextLevel => IsMaxLevel ? 0 : Math.Max(0, NextLevelStart - Experience);

    public double Fraction => IsMaxLevel
        ? 1
        : Math.Clamp((double)(Experience - LevelStart) / (NextLevelStart - LevelStart), 0, 1);

    /// <summary>From the running server's own figures, which include a modded level table. Null when
    /// the server didn't send them.</summary>
    public static SptLevelProgress? FromServer(SptMiniProfile profile) =>
        profile.PreviousExperience is { } previous && profile.NextLevelExperience is { } next && profile.MaxLevel is { } max
            ? new SptLevelProgress(profile.Level, profile.CurrentExperience, previous, next, max)
            : null;
}

/// <summary>
/// SPT's level table (<c>SPT_Data\database\globals.json</c>, <c>config.exp.level.exp_table</c>): the EXP
/// each level takes. Stock figures only - a mod that changes the table does so in the server's memory.
/// </summary>
public sealed class SptLevelTable
{
    private static readonly Dictionary<string, (DateTime Written, long Length, SptLevelTable? Table)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();

    private readonly long[] _perLevel;

    public SptLevelTable(IEnumerable<long> perLevel) => _perLevel = [.. perLevel];

    public int MaxLevel => _perLevel.Length - 1;

    /// <summary>The EXP a level starts at: the first <paramref name="level"/> entries summed (the server's GetExperience).</summary>
    public long ExperienceFor(int level)
    {
        var count = Math.Clamp(level, 0, _perLevel.Length);
        long total = 0;
        for (var i = 0; i < count; i++) total += _perLevel[i];
        return total;
    }

    public SptLevelProgress Progress(int level, long experience) =>
        new(level, experience, ExperienceFor(level), ExperienceFor(level + 1), MaxLevel);

    public static string? GlobalsFile(string? installPath)
    {
        if (!SptInstallationService.TryGetServerRoot(installPath, out var serverRoot)) return null;

        var server = Path.GetFullPath(Path.Combine(installPath!, serverRoot));
        return new[]
            {
                Path.Combine(server, "SPT_Data", "database", "globals.json"),
                Path.Combine(server, "SPT_Data", "Server", "database", "globals.json"),
            }
            .FirstOrDefault(File.Exists);
    }

    public static SptLevelTable? Read(string? installPath)
    {
        if (GlobalsFile(installPath) is not { } file) return null;

        FileInfo info;
        try
        {
            info = new FileInfo(file);
            lock (Gate)
            {
                if (Cache.TryGetValue(file, out var hit) && hit.Written == info.LastWriteTimeUtc && hit.Length == info.Length)
                    return hit.Table;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        SptLevelTable? table;
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            table = FromGlobals(doc.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Debug("ProfileStats", $"couldn't read the level table in {file}: {ex.Message}");
            return null;
        }

        lock (Gate) Cache[file] = (info.LastWriteTimeUtc, info.Length, table);
        return table;
    }

    internal static SptLevelTable? FromGlobals(JsonElement root)
    {
        var table = ProfileJson.Object(ProfileJson.Object(ProfileJson.Object(ProfileJson.Object(root, "config"), "exp"), "level"), "exp_table");
        if (table.ValueKind != JsonValueKind.Array) return null;

        var perLevel = table.EnumerateArray().Select(e => ProfileJson.Int64(e, "exp")).ToList();
        return perLevel.Count == 0 ? null : new SptLevelTable(perLevel);
    }
}

internal static class ProfileJson
{
    public static JsonElement Object(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    public static string Text(JsonElement element, string name) =>
        Object(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";

    public static long Int64(JsonElement element, string name)
    {
        var value = Object(element, name);
        if (value.ValueKind != JsonValueKind.Number) return 0;
        if (value.TryGetInt64(out var whole)) return whole;
        return value.TryGetDouble(out var real) ? (long)real : 0;
    }
}
