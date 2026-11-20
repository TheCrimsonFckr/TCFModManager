using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TCFModManager.Core.Services;

public enum ProfileCleanKind
{
    Skill,
    BotRole,
    Item,
    Trader,
}

/// <summary>One thing taken out of a profile: a skill, a bot role in its cause of death, an item (counting every copy) or a trader.</summary>
public sealed record ProfileCleanThing(ProfileCleanKind Kind, string Value, int Count);

/// <summary>What a clean takes out: skills and bot roles by name, items and traders by id.</summary>
public sealed record ProfileCleanTargets(Func<string, bool> Skill, Func<string, bool> BotRole, IReadOnlySet<string> Ids)
{
    public static ProfileCleanTargets For(IEnumerable<PrepatchValue> values, IEnumerable<string> ids)
    {
        var list = values.ToList();
        var skills = list.Where(v => v.EnumType == ProfileCleaner.SkillTypes).Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        var roles = list.Where(v => v.EnumType == ProfileCleaner.WildSpawnType).Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        return new(skills.Contains, roles.Contains, ids.ToHashSet(StringComparer.Ordinal));
    }

    // Anything SPT itself doesn't have and no staying mod adds.
    public static ProfileCleanTargets Unknown(IReadOnlySet<string> knownSkills, IReadOnlySet<string> knownRoles) =>
        new(s => !knownSkills.Contains(s), r => !knownRoles.Contains(r), new HashSet<string>(StringComparer.Ordinal));
}

/// <summary>One profile file and what a clean would take out of it.</summary>
public sealed record ProfileCleanPlan(string ProfilePath, string ProfileName, IReadOnlyList<ProfileCleanThing> Things);

public enum ProfileCleanFailure
{
    BackupFailed,
    Unreadable,
    WriteFailed,
}

/// <summary>A clean's outcome: the profiles changed, or why none were.</summary>
public sealed record ProfileCleanResult(IReadOnlyList<ProfileCleanPlan> Cleaned, ProfileCleanFailure? Failure = null, string? FailedProfile = null);

//
// OPEN-60: takes out of SPT profiles what mods that are leaving (or gone) put there - the skills and
// cause-of-death bot roles their enum prepatches add (C1), and their items and traders (C5).
// Profiles are only written after a backup of all of them, and all of them or none (C2).
//
public static class ProfileCleaner
{
    public const string SkillTypes = "SkillTypes";
    public const string WildSpawnType = "WildSpawnType";

    private static readonly string[] Sides = ["pmc", "scav"];

    public static bool CanClean(string enumType) => enumType is SkillTypes or WildSpawnType;

    // Every profile in the install with something to take out.
    public static IReadOnlyList<ProfileCleanPlan> Plan(string? installPath, ProfileCleanTargets targets)
    {
        if (ProfileBackups.ProfilesFolder(installPath) is not { } folder) return [];

        var plans = new List<ProfileCleanPlan>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (Cleaned(File.ReadAllText(file), targets) is { Removed.Count: > 0 } cleaned)
                        plans.Add(new ProfileCleanPlan(file, cleaned.Name, cleaned.Removed));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Debug("Profiles", $"couldn't read profile {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Profiles", $"couldn't list {folder}: {ex.Message}");
        }

        return plans;
    }

    // Backs up every profile, then cleans the chosen ones - all or none.
    public static ProfileCleanResult Clean(string installPath, IReadOnlyList<ProfileCleanPlan> chosen, ProfileCleanTargets targets, ProfileBackups backups)
    {
        if (chosen.Count == 0) return new([]);
        if (!backups.EnsureBackupBefore(installPath, ProfileBackups.BeforeClean)) return new([], ProfileCleanFailure.BackupFailed);

        var work = new List<(ProfileCleanPlan Plan, string Before, string After, IReadOnlyList<ProfileCleanThing> Removed)>();
        foreach (var plan in chosen)
        {
            string before;
            try
            {
                before = File.ReadAllText(plan.ProfilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Profiles", $"couldn't read {plan.ProfilePath} to clean it: {ex.Message}");
                return new([], ProfileCleanFailure.Unreadable, plan.ProfileName);
            }

            if (Cleaned(before, targets) is not { } cleaned)
                return new([], ProfileCleanFailure.Unreadable, plan.ProfileName);
            if (cleaned.Removed.Count > 0) work.Add((plan, before, cleaned.Json, cleaned.Removed));
        }

        var written = new List<(ProfileCleanPlan Plan, string Before)>();
        foreach (var (plan, before, after, _) in work)
        {
            try
            {
                SafeFile.WriteText(plan.ProfilePath, after);
                written.Add((plan, before));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Profiles", $"couldn't write {plan.ProfilePath}: {ex.Message} - putting back the {written.Count} already cleaned");
                foreach (var (done, original) in written)
                {
                    try { SafeFile.WriteText(done.ProfilePath, original); }
                    catch (Exception again) when (again is IOException or UnauthorizedAccessException)
                    {
                        AppLog.Warn("Profiles", $"couldn't put back {done.ProfilePath}: {again.Message}");
                    }
                }

                return new([], ProfileCleanFailure.WriteFailed, plan.ProfileName);
            }
        }

        foreach (var (plan, _, _, removed) in work)
            AppLog.Info("Profiles", $"cleaned {plan.ProfileName} ({Path.GetFileName(plan.ProfilePath)}): "
                + string.Join(", ", removed.Select(r => $"{r.Kind} {r.Value}" + (r.Count > 1 ? $" x{r.Count}" : ""))));

        return new([.. work.Select(w => w.Plan with { Things = w.Removed })]);
    }

    // The profile with the targets taken out, its display name, and what went. Null when it isn't a profile that can be read.
    public static (string Json, string Name, IReadOnlyList<ProfileCleanThing> Removed)? Cleaned(string json, ProfileCleanTargets targets)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject { } root && root["info"] is JsonObject ? Take(json, root, targets) : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            AppLog.Debug("Profiles", $"couldn't read a profile: {ex.Message}");
            return null;
        }
    }

    private static (string Json, string Name, IReadOnlyList<ProfileCleanThing> Removed) Take(string json, JsonObject root, ProfileCleanTargets targets)
    {

        var removed = new Dictionary<(ProfileCleanKind, string), int>();
        void Count(ProfileCleanKind kind, string value, int by = 1) => removed[(kind, value)] = removed.GetValueOrDefault((kind, value)) + by;

        var characters = root["characters"] as JsonObject;
        foreach (var side in Sides)
        {
            if (characters?[side] is not JsonObject character) continue;

            if (Get(Get(character, "Skills"), "Common") is JsonArray skills)
            {
                foreach (var skill in skills.ToList())
                {
                    if (Text(Get(skill, "Id")) is { } id && targets.Skill(id))
                    {
                        skills.Remove(skill);
                        Count(ProfileCleanKind.Skill, id);
                    }
                }
            }

            if (Get(Get(Get(character, "Stats"), "Eft"), "DeathCause") is JsonObject death && Text(death["Role"]) is { } role && targets.BotRole(role))
            {
                death["Role"] = null;
                Count(ProfileCleanKind.BotRole, role);
            }

            if (character["TradersInfo"] is JsonObject traders)
            {
                foreach (var trader in traders.Select(t => t.Key).Where(targets.Ids.Contains).ToList())
                {
                    traders.Remove(trader);
                    if (!removed.ContainsKey((ProfileCleanKind.Trader, trader))) Count(ProfileCleanKind.Trader, trader);
                }
            }

            if (character["WishList"] is JsonObject wishes)
            {
                foreach (var wish in wishes.Select(w => w.Key).Where(targets.Ids.Contains).ToList()) wishes.Remove(wish);
            }
        }

        if (root["traderPurchases"] is JsonObject purchases)
        {
            foreach (var trader in purchases.Select(p => p.Key).Where(targets.Ids.Contains).ToList())
            {
                purchases.Remove(trader);
                if (!removed.ContainsKey((ProfileCleanKind.Trader, trader))) Count(ProfileCleanKind.Trader, trader);
            }
        }

        if (targets.Ids.Count > 0)
        {
            RemoveBuilds(root["userbuilds"] as JsonObject, targets.Ids, Count);

            var goneIds = new HashSet<string>(StringComparer.Ordinal);
            var emptied = new HashSet<JsonArray>(ReferenceEqualityComparer.Instance);
            RemoveItems(root, targets.Ids, goneIds, emptied, Count);

            foreach (var side in Sides)
            {
                if (characters?[side] is not JsonObject character) continue;
                Unlink(character, goneIds);
            }

            SettleMessages(root["dialogues"] as JsonObject, emptied);
            SettleInsurance(root["insurance"] as JsonArray, emptied);
        }

        var name = DisplayName(root);
        if (removed.Count == 0) return (json, name, []);

        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = IsIndented(json),
        };

        IReadOnlyList<ProfileCleanThing> things = [.. removed.Select(r => new ProfileCleanThing(r.Key.Item1, r.Key.Item2, r.Value))
            .OrderBy(t => t.Kind).ThenBy(t => t.Value, StringComparer.Ordinal)];
        return (root.ToJsonString(options), name, things);
    }

    // A build holding a leaving item goes whole.
    private static void RemoveBuilds(JsonObject? builds, IReadOnlySet<string> ids, Action<ProfileCleanKind, string, int> count)
    {
        if (builds is null) return;

        foreach (var (kind, key) in new[] { ("weaponBuilds", "_tpl"), ("equipmentBuilds", "_tpl"), ("magazineBuilds", "TemplateId") })
        {
            if (builds[kind] is not JsonArray list) continue;
            foreach (var build in list.ToList())
            {
                var hits = (Get(build, "Items") as JsonArray ?? [])
                    .Select(i => Text(Get(i, key)))
                    .OfType<string>()
                    .Where(ids.Contains)
                    .ToList();
                if (hits.Count == 0) continue;

                list.Remove(build);
                foreach (var tpl in hits) count(ProfileCleanKind.Item, tpl, 1);
            }
        }
    }

    //
    // Every list of items anywhere in the profile (inventory, mail, insurance, hideout slots): an item
    // whose _tpl is leaving goes, with everything inside or attached to it.
    //
    private static void RemoveItems(JsonNode? node, IReadOnlySet<string> ids, HashSet<string> goneIds, HashSet<JsonArray> emptied,
        Action<ProfileCleanKind, string, int> count)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj.ToList()) RemoveItems(value, ids, goneIds, emptied, count);
                break;

            case JsonArray array:
                if (array.Any(e => e is JsonObject o && o["_tpl"] is JsonValue && o["_id"] is JsonValue))
                {
                    var gone = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in array.OfType<JsonObject>())
                    {
                        if (Text(item["_tpl"]) is { } tpl && ids.Contains(tpl) && Text(item["_id"]) is { } id)
                        {
                            gone.Add(id);
                            count(ProfileCleanKind.Item, tpl, 1);
                        }
                    }

                    if (gone.Count > 0)
                    {
                        bool grew;
                        do
                        {
                            grew = false;
                            foreach (var item in array.OfType<JsonObject>())
                            {
                                if (Text(item["parentId"]) is { } parent && gone.Contains(parent) && Text(item["_id"]) is { } child && gone.Add(child))
                                    grew = true;
                            }
                        }
                        while (grew);

                        foreach (var item in array.OfType<JsonObject>().Where(i => Text(i["_id"]) is { } id && gone.Contains(id)).ToList())
                            array.Remove(item);

                        goneIds.UnionWith(gone);
                        if (array.Count == 0) emptied.Add(array);
                    }
                }

                foreach (var element in array.ToList()) RemoveItems(element, ids, goneIds, emptied, count);
                break;
        }
    }

    // What pointed at an item that went: insurance on it, a quick slot, a favourite.
    private static void Unlink(JsonObject character, HashSet<string> goneIds)
    {
        if (goneIds.Count == 0) return;

        if (character["InsuredItems"] is JsonArray insured)
        {
            foreach (var entry in insured.ToList())
                if (Text(Get(entry, "itemId")) is { } id && goneIds.Contains(id)) insured.Remove(entry);
        }

        if (character["Inventory"] is not JsonObject inventory) return;

        if (inventory["fastPanel"] is JsonObject panel)
        {
            foreach (var slot in panel.Where(s => Text(s.Value) is { } id && goneIds.Contains(id)).Select(s => s.Key).ToList()) panel.Remove(slot);
        }

        if (inventory["favoriteItems"] is JsonArray favourites)
        {
            foreach (var favourite in favourites.ToList())
                if ((Text(favourite) ?? Text(Get(favourite, "_id"))) is { } id && goneIds.Contains(id)) favourites.Remove(favourite);
        }
    }

    // A message whose attachments all went has nothing left to collect.
    private static void SettleMessages(JsonObject? dialogues, HashSet<JsonArray> emptied)
    {
        if (dialogues is null || emptied.Count == 0) return;

        foreach (var (_, dialogue) in dialogues)
        {
            if (Get(dialogue, "messages") is not JsonArray messages) continue;
            foreach (var message in messages.OfType<JsonObject>())
            {
                if (Get(message["items"], "data") is not JsonArray data || !emptied.Contains(data)) continue;
                if (message.ContainsKey("hasRewards")) message["hasRewards"] = false;
                if (message.ContainsKey("rewardCollected")) message["rewardCollected"] = true;
            }
        }
    }

    // Insurance due back with nothing left in it is dropped.
    private static void SettleInsurance(JsonArray? insurance, HashSet<JsonArray> emptied)
    {
        if (insurance is null || emptied.Count == 0) return;

        foreach (var entry in insurance.ToList())
            if (Get(entry, "items") is JsonArray items && emptied.Contains(items)) insurance.Remove(entry);
    }

    private static string DisplayName(JsonObject root)
    {
        var nickname = Text(Get(Get(Get(root["characters"], "pmc"), "Info"), "Nickname"));
        return string.IsNullOrEmpty(nickname) ? Text(Get(root["info"], "username")) ?? string.Empty : nickname;
    }

    private static JsonNode? Get(JsonNode? node, string key) => node is JsonObject obj ? obj[key] : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool IsIndented(string json) => json.AsSpan(0, Math.Min(256, json.Length)).Contains('\n');
}
