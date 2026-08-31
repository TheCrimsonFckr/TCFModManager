using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// What applying a list would do to one mod.
public enum ModListActionKind
{
    // On the list, not installed at all.
    Install,

    // Installed, but at a different version than the list names.
    Update,

    // On the list and installed, but currently sitting in a .disabled container.
    Enable,

    // Installed and enabled, not on the list, and the list's policy is Exclusive.
    Disable,

    // On the list, not installed, and not fetchable - no Forge listing to download from.
    Manual,

    // On the list, installed, enabled, at the version the list names. Nothing to do.
    Keep,
}

// One mod's line in a plan.
public sealed record ModListAction
{
    public required ModListActionKind Kind { get; init; }

    // The name to show - the list entry's name, or the installed mod's for a Disable.
    public required string Name { get; init; }

    // The list entry this came from. Null for a Disable, which by definition isn't on the list.
    public ModListEntry? Entry { get; init; }

    // What's installed now. Null for Install and Manual.
    public ModListCandidate? Installed { get; init; }

    public int? ModId { get; init; }

    // True when ModId is an addon id. What tells the fetch half which catalog and which endpoint
    // this action belongs to.
    public bool IsAddon { get; init; }

    // The version id to fetch. Null when the list couldn't pin one, which is what
    // NeedsVersionLookup reports.
    public int? VersionId { get; init; }

    // The version string the list names.
    public string? TargetVersion { get; init; }

    public string? InstalledVersion { get; init; }

    // True when the list names an older version than the one installed, so applying it moves
    // backwards. Worth showing differently in a diff; not an error.
    public bool IsDowngrade { get; init; }

    //
    // Set on an Enable whose installed version isn't the one the list names. The move has to
    // happen first - a mod can't be updated while it sits in a .disabled container - so both
    // steps are carried on the one action and the apply step does them in that order, rather
    // than leaving the mod enabled at a stale version until the list is applied a second time.
    //
    public bool NeedsUpdateAfterEnable { get; init; }

    // True for anything this action will fetch from The Forge.
    public bool IsFetch =>
        Kind is ModListActionKind.Install or ModListActionKind.Update || NeedsUpdateAfterEnable;

    // A fetch that has no version id to fetch, so the apply step has to ask The Forge for the
    // mod's versions and either find TargetVersion or fall back with the user's say-so.
    public bool NeedsVersionLookup => IsFetch && VersionId is null;
}

//
// The result of resolving a list against what's installed: one action per mod, and the questions
// the UI needs answered before it can ask the user anything.
//
public sealed class ModListPlan
{
    public required Guid ListId { get; init; }
    public required string ListName { get; init; }
    public required ModListPolicy Policy { get; init; }
    public required IReadOnlyList<ModListAction> Actions { get; init; }

    public IEnumerable<ModListAction> Install => Of(ModListActionKind.Install);
    public IEnumerable<ModListAction> Update => Of(ModListActionKind.Update);
    public IEnumerable<ModListAction> Enable => Of(ModListActionKind.Enable);
    public IEnumerable<ModListAction> Disable => Of(ModListActionKind.Disable);
    public IEnumerable<ModListAction> Manual => Of(ModListActionKind.Manual);
    public IEnumerable<ModListAction> Keep => Of(ModListActionKind.Keep);

    // Fetches that couldn't be pinned to a version id and need a live lookup first.
    public IEnumerable<ModListAction> NeedingVersionLookup => Actions.Where(a => a.NeedsVersionLookup);

    // Nothing to fetch and nothing to move - the install already matches the list, give or take
    // whatever the user has to fetch by hand.
    public bool IsNoOp => Actions.All(a => a.Kind is ModListActionKind.Keep or ModListActionKind.Manual);

    public bool RequiresDownloads => Actions.Any(a => a.IsFetch);

    //
    // BepInEx holds open handles on loaded plugin DLLs, so moving a mod into its .disabled
    // container fails while the game is running. Enabling doesn't hit that - nothing has the
    // disabled copy open - it just doesn't take effect until the client restarts.
    //
    public bool RequiresGameClosed => Disable.Any();

    private IEnumerable<ModListAction> Of(ModListActionKind kind) => Actions.Where(a => a.Kind == kind);
}

//
// Works out what applying a mod list would do, without doing any of it.
//
// Everything here is a pure comparison of a list against a scan - no network, no file moves, no
// disk reads. The plan it produces is what a diff dialog shows and what an apply step then walks;
// the moves themselves belong to ModDisableService and the download queue.
//
public static class ModListPlanner
{
    public static ModListPlan Build(
        ModList list,
        IEnumerable<ModListCandidate> installed,
        IReadOnlySet<string>? neverAutoDisable = null)
    {
        var candidates = installed.ToList();
        var actions = new List<ModListAction>();
        var matched = new bool[candidates.Count];

        // Keyed on the (id, IsAddon) pair - addon ids and mod ids are separate sequences on
        // sp-mod.com, so a bare id dictionary would let a list entry for addon 116 claim mod 116.
        var byModId = new Dictionary<(int Id, bool IsAddon), int>();
        var byGuid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byFolder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.ModId is { } id) byModId.TryAdd((id, candidate.IsAddon), index);
            if (!string.IsNullOrWhiteSpace(candidate.Guid)) byGuid.TryAdd(candidate.Guid.Trim(), index);
            foreach (var folder in candidate.Folders.Where(f => !string.IsNullOrWhiteSpace(f)))
                byFolder.TryAdd(folder.Trim(), index);
            byName.TryAdd(candidate.Name.Trim(), index);
        }

        foreach (var entry in list.Entries)
        {
            var found = Match(entry, byModId, byGuid, byFolder, byName);
            if (found >= 0) matched[found] = true;
            actions.Add(ActionFor(entry, found >= 0 ? candidates[found] : null));
        }

        if (list.Policy == ModListPolicy.Exclusive)
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (matched[index] || candidate.IsDisabled) continue;
                if (IsPinnedAgainstDisable(candidate, neverAutoDisable)) continue;

                actions.Add(new ModListAction
                {
                    Kind = ModListActionKind.Disable,
                    Name = candidate.Name.Trim(),
                    Installed = candidate,
                    ModId = candidate.ModId,
                    IsAddon = candidate.IsAddon,
                    InstalledVersion = candidate.Version,
                });
            }
        }

        return new ModListPlan
        {
            ListId = list.Id,
            ListName = list.Name,
            Policy = list.Policy,
            Actions = actions,
        };
    }

    // The index of the installed mod this entry refers to, or -1 when it isn't installed. Mod id
    // first, then plugin GUID, then folder name, then display name - most reliable join key down
    // to the loosest.
    private static int Match(
        ModListEntry entry,
        Dictionary<(int Id, bool IsAddon), int> byModId,
        Dictionary<string, int> byGuid,
        Dictionary<string, int> byFolder,
        Dictionary<string, int> byName)
    {
        if (entry.ModId is { } id && byModId.TryGetValue((id, entry.IsAddon), out var byId)) return byId;

        if (!string.IsNullOrWhiteSpace(entry.Guid) && byGuid.TryGetValue(entry.Guid.Trim(), out var guidMatch))
            return guidMatch;

        foreach (var folder in entry.Folders.Where(f => !string.IsNullOrWhiteSpace(f)))
            if (byFolder.TryGetValue(folder.Trim(), out var folderMatch))
                return folderMatch;

        return byName.TryGetValue(entry.Name.Trim(), out var nameMatch) ? nameMatch : -1;
    }

    private static ModListAction ActionFor(ModListEntry entry, ModListCandidate? installed)
    {
        if (installed is null)
        {
            return new ModListAction
            {
                Kind = entry.IsResolved ? ModListActionKind.Install : ModListActionKind.Manual,
                Name = entry.Name,
                Entry = entry,
                ModId = entry.ModId,
                IsAddon = entry.IsAddon,
                VersionId = entry.VersionId,
                TargetVersion = entry.Version,
            };
        }

        var sameVersion = SameVersion(entry.Version, installed.Version);

        //
        // A disabled mod is always an Enable, never an Update - updating one while it sits in a
        // .disabled container would place files where nothing loads them, the same reason Update
        // is blocked on a disabled mod everywhere else in the app. When it is also at the wrong
        // version the action carries NeedsUpdateAfterEnable, so one pass enables it and then
        // updates it rather than leaving it enabled at a stale version.
        //
        var kind = installed.IsDisabled
            ? ModListActionKind.Enable
            : sameVersion
                ? ModListActionKind.Keep
                : ModListActionKind.Update;

        return new ModListAction
        {
            Kind = kind,
            Name = entry.Name,
            Entry = entry,
            Installed = installed,
            ModId = entry.ModId ?? installed.ModId,
            IsAddon = entry.ModId is not null ? entry.IsAddon : installed.IsAddon,
            VersionId = entry.VersionId,
            TargetVersion = entry.Version,
            InstalledVersion = installed.Version,
            IsDowngrade = kind != ModListActionKind.Keep && IsOlder(entry.Version, installed.Version),
            NeedsUpdateAfterEnable = kind == ModListActionKind.Enable && !sameVersion,
        };
    }

    private static bool IsPinnedAgainstDisable(ModListCandidate candidate, IReadOnlySet<string>? pinned)
    {
        if (pinned is null || pinned.Count == 0) return false;

        return candidate.Folders.Any(f => pinned.Contains(f.Trim().ToLowerInvariant()))
            || pinned.Contains(candidate.Name.Trim().ToLowerInvariant());
    }

    //
    // An unknown version on either side counts as "the same", so a mod whose version couldn't be
    // read isn't endlessly offered as an update.
    //
    private static bool SameVersion(string? target, string? installed)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(installed)) return true;
        if (string.Equals(target.Trim(), installed.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

        return SemanticVersion.TryParse(target, out var a)
            && SemanticVersion.TryParse(installed, out var b)
            && a.Value.CompareTo(b.Value) == 0;
    }

    private static bool IsOlder(string? target, string? installed) =>
        SemanticVersion.TryParse(target, out var a)
        && SemanticVersion.TryParse(installed, out var b)
        && a.Value.CompareTo(b.Value) < 0;
}
