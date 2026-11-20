using System.Text.Json.Nodes;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public enum ProfileCleanerTestEnum
{
    Strength,
    Hacking,
}

public class ProfileCleanerTests : IDisposable
{
    private const string Gun = "6a0000000000000000000001";
    private const string Scope = "6a0000000000000000000002";
    private const string Ammo = "5447a9cd4bdc2dbd208b4567";
    private const string Trader = "6a00000000000000000000aa";
    private const string BaseTrader = "54cb50c76803fa8b248b4571";

    private readonly string _install = Path.Combine(Path.GetTempPath(), "TcfProfileClean_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_install)) Directory.Delete(_install, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static readonly string Profile = """
        {"info":{"id":"p1","username":"user"},
         "characters":{
          "pmc":{
           "Info":{"Nickname":"Trapp"},
           "Skills":{"Common":[{"Id":"Strength","Progress":10.750},{"Id":"Hacking","Progress":250.5},{"Id":"SignalsIntelligence","Progress":1}]},
           "Stats":{"Eft":{"DeathCause":{"DamageType":"Bullet","Side":"Savage","Role":"ISBScout","WeaponId":"x"},"Victims":[{"Role":"ISBScout"}]}},
           "Inventory":{
            "items":[
             {"_id":"stash","_tpl":"@AMMO@"},
             {"_id":"gun1","_tpl":"@GUN@","parentId":"stash","slotId":"hideout"},
             {"_id":"mag1","_tpl":"@AMMO@","parentId":"gun1","slotId":"mod_magazine"},
             {"_id":"round1","_tpl":"@AMMO@","parentId":"mag1","slotId":"cartridges"},
             {"_id":"keep1","_tpl":"@AMMO@","parentId":"stash","slotId":"hideout"},
             {"_id":"scope1","_tpl":"@SCOPE@","parentId":"stash","slotId":"hideout"}
            ],
            "fastPanel":{"Item4":"gun1","Item5":"keep1"},
            "favoriteItems":["scope1","keep1"]
           },
           "InsuredItems":[{"tid":"@BASETRADER@","itemId":"gun1"},{"tid":"@BASETRADER@","itemId":"keep1"}],
           "TradersInfo":{"@BASETRADER@":{"unlocked":true},"@TRADER@":{"unlocked":true}},
           "WishList":{"@GUN@":1,"@AMMO@":2}
          },
          "scav":{
           "Skills":{"Common":[{"Id":"Hacking","Progress":5}]},
           "TradersInfo":{"@TRADER@":{}},
           "Inventory":{"items":[]}
          }
         },
         "userbuilds":{
          "weaponBuilds":[{"Id":"b1","Items":[{"_id":"r","_tpl":"@GUN@"}]},{"Id":"b2","Items":[{"_id":"r","_tpl":"@AMMO@"}]}],
          "equipmentBuilds":[],
          "magazineBuilds":[{"Id":"m1","Items":[{"TemplateId":"@SCOPE@","Count":30}]}]
         },
         "dialogues":{"d":{"messages":[
          {"_id":"m1","hasRewards":true,"rewardCollected":false,"items":{"stash":"s","data":[{"_id":"x","_tpl":"@GUN@"}]}},
          {"_id":"m2","hasRewards":true,"rewardCollected":false,"items":{"stash":"s","data":[{"_id":"y","_tpl":"@GUN@"},{"_id":"z","_tpl":"@AMMO@"}]}}
         ]}},
         "insurance":[{"traderId":"t","items":[{"_id":"i1","_tpl":"@GUN@"}]},{"traderId":"t","items":[{"_id":"i2","_tpl":"@AMMO@"}]}],
         "traderPurchases":{"@TRADER@":{},"@BASETRADER@":{}}}
        """.ReplaceLineEndings("").Replace("  ", "")
        .Replace("@GUN@", Gun).Replace("@SCOPE@", Scope).Replace("@AMMO@", Ammo).Replace("@BASETRADER@", BaseTrader).Replace("@TRADER@", Trader);

    private static ProfileCleanTargets Leaving(params string[] ids) => ProfileCleanTargets.For(
        [
            new PrepatchValue("com.cj.skillsextended", ProfileCleaner.SkillTypes, "Hacking"),
            new PrepatchValue("com.cj.skillsextended", ProfileCleaner.SkillTypes, "SignalsIntelligence"),
            new PrepatchValue("com.samc137.isbspecialforces", ProfileCleaner.WildSpawnType, "ISBScout"),
            new PrepatchValue("com.other", "BonusType", "Hacking"),
        ],
        ids);

    private static JsonObject Pmc(JsonNode root) => root["characters"]!["pmc"]!.AsObject();

    [Fact]
    public void Skills_and_the_bot_role_a_leaving_mod_adds_come_out_of_both_characters()
    {
        var (json, name, removed) = ProfileCleaner.Cleaned(Profile, Leaving())!.Value;
        var root = JsonNode.Parse(json)!;

        Assert.Equal("Trapp", name);
        Assert.Equal(["Strength"], Pmc(root)["Skills"]!["Common"]!.AsArray().Select(s => (string)s!["Id"]!));
        Assert.Empty(root["characters"]!["scav"]!["Skills"]!["Common"]!.AsArray());

        var death = Pmc(root)["Stats"]!["Eft"]!["DeathCause"]!.AsObject();
        Assert.True(death.ContainsKey("Role"));
        Assert.Null(death["Role"]);
        Assert.Equal("Bullet", (string)death["DamageType"]!);
        Assert.Equal("ISBScout", (string)Pmc(root)["Stats"]!["Eft"]!["Victims"]![0]!["Role"]!);

        Assert.Equal(
            [
                new ProfileCleanThing(ProfileCleanKind.Skill, "Hacking", 2),
                new ProfileCleanThing(ProfileCleanKind.Skill, "SignalsIntelligence", 1),
                new ProfileCleanThing(ProfileCleanKind.BotRole, "ISBScout", 1),
            ],
            removed);
    }

    [Fact]
    public void Leaving_items_go_with_everything_inside_them_and_what_pointed_at_them()
    {
        var (json, _, removed) = ProfileCleaner.Cleaned(Profile, Leaving(Gun, Scope, Trader))!.Value;
        var root = JsonNode.Parse(json)!;
        var inventory = Pmc(root)["Inventory"]!;

        Assert.Equal(["stash", "keep1"], inventory["items"]!.AsArray().Select(i => (string)i!["_id"]!));
        Assert.Equal(["Item5"], inventory["fastPanel"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["keep1"], inventory["favoriteItems"]!.AsArray().Select(f => (string)f!));
        Assert.Equal(["keep1"], Pmc(root)["InsuredItems"]!.AsArray().Select(i => (string)i!["itemId"]!));
        Assert.Equal([Ammo], Pmc(root)["WishList"]!.AsObject().Select(w => w.Key));

        Assert.Equal([BaseTrader], Pmc(root)["TradersInfo"]!.AsObject().Select(t => t.Key));
        Assert.Empty(root["characters"]!["scav"]!["TradersInfo"]!.AsObject());
        Assert.Equal([BaseTrader], root["traderPurchases"]!.AsObject().Select(t => t.Key));

        Assert.Equal(["b2"], root["userbuilds"]!["weaponBuilds"]!.AsArray().Select(b => (string)b!["Id"]!));
        Assert.Empty(root["userbuilds"]!["magazineBuilds"]!.AsArray());

        var messages = root["dialogues"]!["d"]!["messages"]!.AsArray();
        Assert.Empty(messages[0]!["items"]!["data"]!.AsArray());
        Assert.False((bool)messages[0]!["hasRewards"]!);
        Assert.True((bool)messages[0]!["rewardCollected"]!);
        Assert.Equal(["z"], messages[1]!["items"]!["data"]!.AsArray().Select(i => (string)i!["_id"]!));
        Assert.True((bool)messages[1]!["hasRewards"]!);

        Assert.Equal(["i2"], root["insurance"]!.AsArray().Select(e => (string)e!["items"]![0]!["_id"]!));

        Assert.Contains(new ProfileCleanThing(ProfileCleanKind.Item, Gun, 5), removed);
        Assert.Contains(new ProfileCleanThing(ProfileCleanKind.Item, Scope, 2), removed);
        Assert.Contains(new ProfileCleanThing(ProfileCleanKind.Trader, Trader, 1), removed);
    }

    [Fact]
    public void A_profile_with_nothing_to_take_out_comes_back_as_it_was()
    {
        var targets = ProfileCleanTargets.For([new PrepatchValue("g", ProfileCleaner.SkillTypes, "Lockpicking")], ["6b0000000000000000000009"]);

        var (json, _, removed) = ProfileCleaner.Cleaned(Profile, targets)!.Value;

        Assert.Empty(removed);
        Assert.Same(Profile, json);
    }

    [Fact]
    public void The_rest_of_the_profile_is_written_as_it_was()
    {
        var (json, _, _) = ProfileCleaner.Cleaned(Profile, Leaving())!.Value;

        Assert.DoesNotContain('\n', json);
        Assert.Contains("\"Progress\":10.750", json);
        Assert.Contains("\"Role\":null", json);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(Profile)!["dialogues"], JsonNode.Parse(json)!["dialogues"]));
    }

    [Fact]
    public void Values_nothing_knows_any_more_are_found_without_naming_a_mod()
    {
        var targets = ProfileCleanTargets.Unknown(new HashSet<string> { "Strength", "SignalsIntelligence" }, new HashSet<string> { "assault" });

        var (_, _, removed) = ProfileCleaner.Cleaned(Profile, targets)!.Value;

        Assert.Equal(
            [new ProfileCleanThing(ProfileCleanKind.Skill, "Hacking", 2), new ProfileCleanThing(ProfileCleanKind.BotRole, "ISBScout", 1)],
            removed);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"characters":{}}""")]
    [InlineData("""{"info":{},"a":1,"a":2}""")]
    public void Something_that_isnt_a_profile_is_left_alone(string json)
    {
        Assert.Null(ProfileCleaner.Cleaned(json, Leaving(Gun)));
    }

    [Fact]
    public void Only_values_of_kinds_it_knows_are_cleaned()
    {
        Assert.True(ProfileCleaner.CanClean(ProfileCleaner.SkillTypes));
        Assert.True(ProfileCleaner.CanClean(ProfileCleaner.WildSpawnType));
        Assert.False(ProfileCleaner.CanClean("BonusType"));
        Assert.False(ProfileCleaner.CanClean("HideoutAreas"));
    }

    private string MakeInstall(params (string File, string Json)[] profiles)
    {
        var server = Path.Combine(_install, "SPT_Runtime");
        Directory.CreateDirectory(Path.Combine(server, "user", "profiles"));
        File.WriteAllText(Path.Combine(server, "SPT.Server.exe"), "");
        foreach (var (file, json) in profiles) File.WriteAllText(Path.Combine(server, "user", "profiles", file), json);
        return Path.Combine(server, "user", "profiles");
    }

    [Fact]
    public void Plan_lists_only_profiles_with_something_to_take_out_and_Clean_backs_up_first()
    {
        var other = Profile.Replace("Hacking", "Strength").Replace("SignalsIntelligence", "Vitality").Replace("ISBScout", "assault").Replace("Trapp", "Other");
        var folder = MakeInstall(("a.json", Profile), ("b.json", other));
        var backups = new ProfileBackups(Path.Combine(_install, "backups"));

        var plans = ProfileCleaner.Plan(_install, Leaving());
        var plan = Assert.Single(plans);
        Assert.Equal(("Trapp", Path.Combine(folder, "a.json")), (plan.ProfileName, plan.ProfilePath));

        var result = ProfileCleaner.Clean(_install, plans, Leaving(), backups);

        Assert.Null(result.Failure);
        Assert.Single(result.Cleaned);
        Assert.DoesNotContain("Hacking", File.ReadAllText(Path.Combine(folder, "a.json")));
        Assert.Equal(other, File.ReadAllText(Path.Combine(folder, "b.json")));

        var backup = Assert.Single(backups.List(_install));
        Assert.Equal(ProfileBackups.BeforeClean, backup.Reason);
    }

    [Fact]
    public void A_profile_that_cant_be_read_stops_the_clean_before_anything_is_written()
    {
        var folder = MakeInstall(("a.json", Profile), ("b.json", Profile.Replace("Trapp", "Second")));
        var plans = ProfileCleaner.Plan(_install, Leaving());
        Assert.Equal(2, plans.Count);

        File.WriteAllText(Path.Combine(folder, "b.json"), "{ broken");

        var result = ProfileCleaner.Clean(_install, plans, Leaving(), new ProfileBackups(Path.Combine(_install, "backups")));

        Assert.Equal(ProfileCleanFailure.Unreadable, result.Failure);
        Assert.Equal("Second", result.FailedProfile);
        Assert.Empty(result.Cleaned);
        Assert.Equal(Profile, File.ReadAllText(Path.Combine(folder, "a.json")));
    }

    [Fact]
    public void Prepatch_values_carry_their_enum_type()
    {
        var patchers = Path.Combine(_install, "SPT_Runtime", "user", "patchers", "com.cj.skillsextended");
        Directory.CreateDirectory(patchers);
        Directory.CreateDirectory(Path.Combine(_install, "SPT_Runtime", "user", "mods"));
        File.WriteAllText(Path.Combine(patchers, "EnumExtensions.json"), """
            [ { "enumType": "SPTarkov.Server.Core.Models.Enums.SkillTypes", "constantName": "Hacking", "constantValue": 200 },
              { "enumType": "SPTarkov.Server.Core.Models.Eft.Common.WildSpawnType", "constantName": "ISBScout", "constantValue": 13711 } ]
            """);

        Assert.Equal(
            [
                new PrepatchValue("com.cj.skillsextended", ProfileCleaner.SkillTypes, "Hacking"),
                new PrepatchValue("com.cj.skillsextended", ProfileCleaner.WildSpawnType, "ISBScout"),
            ],
            ServerPrepatches.Values(_install));
    }

    [Fact]
    public void An_enums_names_are_read_from_the_assemblys_metadata()
    {
        var names = ServerEnums.NamesIn(typeof(ProfileCleanerTestEnum).Assembly.Location, typeof(ProfileCleanerTestEnum).FullName!);

        Assert.Equal(new HashSet<string> { "Strength", "Hacking" }, names);
        Assert.Null(ServerEnums.NamesIn(typeof(ProfileCleanerTestEnum).Assembly.Location, "No.Such.Enum"));
        Assert.Null(ServerEnums.Names(_install, ServerEnums.SkillTypes));
    }
}
