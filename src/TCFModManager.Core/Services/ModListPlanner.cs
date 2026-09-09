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
    //
    // alsoRequiredBy is the server list this install is following, when it is following one.
    //
    // Its mods are spared by the Exclusive sweep below, which is what lets a player follow their
    // own list AND a server's at the same time: without it, applying a personal list would set
    // aside every mod the server requires and the next raid would be a version mismatch. Only the
    // entries that apply here are spared - a server-scoped entry was never installed to begin with.
    //
    //
    // machine is what this install actually is, from InstallRole.ScopeFor - Client for an ordinary
    // player, Headless for a box running a Fika headless client, both for one that does each.
    // It only ever narrows a SERVED list; your own lists describe your own install and apply whole.
    //
    // Defaulted to Client so every existing caller and test keeps the behaviour it had, and so the
    // one value that is wrong in the dangerous direction - a headless quietly planned as a player,
    // stripped of the bot mods it hosts with - has to be asked for rather than fallen into.
    //
    public static ModListPlan Build(
        ModList list,
        IEnumerable<ModListCandidate> installed,
        IReadOnlySet<string>? neverAutoDisable = null,
        ModList? alsoRequiredBy = null,
        ModListEntryScope machine = ModListEntryScope.Client)
    {
        neverAutoDisable = Protecting(neverAutoDisable, alsoRequiredBy, machine);

        var applying = list.EntriesApplyingTo(machine).ToList();

        var candidates = installed.ToList();
        var actions = new List<ModListAction>();
        var matched = new bool[candidates.Count];

        // Shared with the refresh that re-reads installed versions into a list - see ModListMatch
        // for why the join keys live in one place.
        var match = new ModListMatch(candidates);

        //
        // The entries applying to THIS machine, not every entry: a served list's server-only entries
        // are not a player's to install, and its player-only ones are not a headless box's. They are
        // still matched against what is installed, so a mod you happen to have is not then disabled
        // as "not on the list" - see the sweep below.
        //
        foreach (var entry in applying)
        {
            var found = match.IndexOf(entry);
            if (found >= 0) matched[found] = true;
            actions.Add(ActionFor(entry, found >= 0 ? candidates[found] : null));
        }

        // Skipped entries still claim their installed mod, so the Exclusive sweep does not offer to
        // disable something purely because this machine was not the one meant to install it.
        foreach (var entry in list.Entries.Except(applying))
        {
            var found = match.IndexOf(entry);
            if (found >= 0) matched[found] = true;
        }

        //
        // A list a server handed you never disables anything, whatever policy its author chose.
        //
        // An operator writing an Exclusive list is describing THEIR install; applying that verbatim
        // on someone else's machine would disable mods the server has never heard of and has no
        // opinion about - a HUD tweak, a sound pack. The server says what you need, not what you may
        // not have. Your own lists keep working exactly as before.
        //
        if (list.Policy == ModListPolicy.Exclusive && list.Origin != ModListOrigin.Server)
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (matched[index] || candidate.IsDisabled) continue;
                if (!candidate.CanBeDisabled) continue;
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

    //
    // The user's own pins, plus everything the followed server list names, as one set of folder and
    // mod names. Built here rather than by the caller so every call site protects the server's mods
    // the same way - forgetting it at one of them is a mod disabled behind the user's back.
    //
    private static IReadOnlySet<string>? Protecting(IReadOnlySet<string>? pinned, ModList? serverList,
        ModListEntryScope machine)
    {
        if (serverList is null) return pinned;

        var protectedNames = new HashSet<string>(pinned ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);

        //
        // Only the entries the server expects of THIS machine. Protection is for mods the server
        // needs you to be running - on a headless box, a player-only entry is not one of those, and
        // spreading the protection wider would pin a HUD mod on a machine nobody looks at.
        //
        foreach (var entry in serverList.EntriesApplyingTo(machine))
        {
            protectedNames.Add(entry.Name.Trim().ToLowerInvariant());

            foreach (var folder in entry.Folders.Where(f => !string.IsNullOrWhiteSpace(f)))
                protectedNames.Add(folder.Trim().ToLowerInvariant());
        }

        return protectedNames;
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
