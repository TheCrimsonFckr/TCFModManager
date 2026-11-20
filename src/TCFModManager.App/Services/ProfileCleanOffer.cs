using System.Text.RegularExpressions;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-60: what mods about to go leave in this install's profiles - the items and traders (OPEN-53
// N7), the values their prepatches add (N8) - the question's part about it, and the clean of the
// profiles the user leaves ticked, run once the mods have gone.
//
internal sealed partial class ProfileCleanOffer
{
    public static readonly ProfileCleanOffer None = new();

    private string? _installPath;
    private ProfileCleanTargets? _targets;
    private bool _now;

    public IReadOnlyList<ProfileLoss> Losses { get; private init; } = [];

    public IReadOnlyList<string> AddingValues { get; private init; } = [];

    public IReadOnlyList<(string Mod, IReadOnlyList<string> Values)> CantClean { get; private init; } = [];

    public IReadOnlyList<AskPick> Picks { get; private init; } = [];

    public bool Removing { get; private init; }

    public bool TurnsOnSptFix { get; private init; }

    public bool IsEmpty => Losses.Count == 0 && AddingValues.Count == 0 && Picks.Count == 0;

    public static async Task<ProfileCleanOffer> ForCardsAsync(IReadOnlyList<InstalledModCardViewModel> cards, bool removing)
    {
        var losses = await ProfileItemsWarning.ForCardsAsync(cards);
        var mods = cards
            .Select(c => (c.DisplayTitle, (IReadOnlyList<string>)c.Entries
                .Where(e => e.Target == InstalledModTarget.Server && (removing || !e.IsDisabled))
                .SelectMany(e => e.AllGuids)
                .ToList()))
            .ToList();
        return await BuildAsync(losses, mods, removing);
    }

    public static async Task<ProfileCleanOffer> ForListAsync(string installPath, IReadOnlyList<ModListAction> disabling)
    {
        var losses = await ProfileItemsWarning.ForListAsync(installPath, disabling);
        var mods = disabling
            .Where(a => a.Installed is { CanBeDisabled: true, IsDisabled: false, Guid.Length: > 0 })
            .Select(a => (a.Name, (IReadOnlyList<string>)[a.Installed!.Guid!]))
            .ToList();
        return await BuildAsync(losses, mods, removing: false);
    }

    private static Task<ProfileCleanOffer> BuildAsync(
        IReadOnlyList<ProfileLoss> losses, IReadOnlyList<(string Mod, IReadOnlyList<string> Guids)> mods, bool removing)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return Task.FromResult(new ProfileCleanOffer { Losses = losses, Removing = removing });

        return Task.Run(() =>
        {
            try
            {
                var values = ServerPrepatches.Values(installPath);
                var byMod = mods
                    .Select(m => (m.Mod, Values: values.Where(v => m.Guids.Contains(v.Guid, StringComparer.OrdinalIgnoreCase)).ToList()))
                    .Where(m => m.Values.Count > 0)
                    .ToList();

                var cleanable = byMod.SelectMany(m => m.Values).Where(v => ProfileCleaner.CanClean(v.EnumType)).ToList();
                var ids = losses.SelectMany(l => l.Things).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
                var targets = ProfileCleanTargets.For(cleanable, ids);
                var plans = cleanable.Count > 0 || ids.Count > 0 ? ProfileCleaner.Plan(installPath, targets) : [];

                return new ProfileCleanOffer
                {
                    _installPath = installPath,
                    _targets = targets,
                    Losses = losses,
                    AddingValues = [.. byMod.Select(m => m.Mod).Distinct()],
                    CantClean = [.. byMod
                        .Select(m => (m.Mod, (IReadOnlyList<string>)[.. m.Values.Where(v => !ProfileCleaner.CanClean(v.EnumType)).Select(v => v.Name).Distinct()]))
                        .Where(m => m.Item2.Count > 0)],
                    Picks = [.. plans.Select(p => new AskPick { Text = p.ProfileName, Detail = Describe(p.Things), Tag = p })],
                    Removing = removing,
                    TurnsOnSptFix = plans.Count > 0 && ProfileFixActions.StateFor(installPath) == SptProfileFixState.Off,
                };
            }
            catch (Exception ex)
            {
                AppLog.Warn("Profiles", $"profile clean check failed: {ex.Message}");
                return new ProfileCleanOffer { Losses = losses, Removing = removing };
            }
        });
    }

    //
    // OPEN-60 C6: every profile against what is installed and turned on now - skills and bot roles
    // that neither SPT nor a mod still on adds. Null when SPT's own list of them can't be read.
    //
    public static Task<ProfileCleanOffer?> ForWhatIsGoneAsync(string installPath) => Task.Run(() =>
    {
        try
        {
            if (ServerEnums.Names(installPath, ServerEnums.SkillTypes) is not { } skills
                || ServerEnums.Names(installPath, ServerEnums.WildSpawnType) is not { } roles)
            {
                return null;
            }

            var on = InstalledScan.Run(installPath)
                .Where(m => m.Target == InstalledModTarget.Server && !m.IsDisabled)
                .SelectMany(m => m.AllGuids)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = ServerPrepatches.Values(installPath).Where(v => on.Contains(v.Guid)).ToList();

            var targets = ProfileCleanTargets.Unknown(
                skills.Concat(added.Where(v => v.EnumType == ProfileCleaner.SkillTypes).Select(v => v.Name)).ToHashSet(StringComparer.Ordinal),
                roles.Concat(added.Where(v => v.EnumType == ProfileCleaner.WildSpawnType).Select(v => v.Name)).ToHashSet(StringComparer.Ordinal));
            var plans = ProfileCleaner.Plan(installPath, targets);

            return new ProfileCleanOffer
            {
                _installPath = installPath,
                _targets = targets,
                _now = true,
                Picks = [.. plans.Select(p => new AskPick { Text = p.ProfileName, Detail = Describe(p.Things), Tag = p })],
                TurnsOnSptFix = plans.Count > 0 && ProfileFixActions.StateFor(installPath) == SptProfileFixState.Off,
            };
        }
        catch (Exception ex)
        {
            AppLog.Warn("Profiles", $"profile clean check failed: {ex.Message}");
            return (ProfileCleanOffer?)null;
        }
    });

    public string? AddingNote => AddingValues.Count == 0
        ? null
        : LocalizationService.Text(Removing ? Strings.ServerValues_RemoveNoteFormat : Strings.ServerValues_NoteFormat, TextLists.Join(AddingValues));

    public string? Warning => Losses.Count > 0 ? Strings.ProfileItems_Intro : AddingNote;

    public IReadOnlyList<AskSection> Sections => Losses.Count > 0 ? [ProfileItemsWarning.Section(Losses)] : [];

    public IReadOnlyList<string> Notes
    {
        get
        {
            var notes = new List<string>();
            if (Losses.Count > 0 && AddingNote is { } adding) notes.Add(adding);
            notes.AddRange(CantClean.Select(c => LocalizationService.Text(Strings.ProfileClean_CantCleanFormat, c.Mod, TextLists.Join(c.Values))));
            if (TurnsOnSptFix) notes.Add(Strings.ProfileClean_SptFixNote);
            return notes;
        }
    }

    public AskPicks? PickList => Picks.Count == 0
        ? null
        : new AskPicks(_now ? Strings.ProfileClean_HeadingNow : Strings.ProfileClean_Heading, Strings.ProfileClean_Explain, Picks);

    //
    // After the mods have gone: cleans the ticked profiles, all or none, after a backup of all of
    // them. The sentence to add to the status line, or null when nothing was ticked.
    //
    public async Task<string?> CleanAsync()
    {
        var chosen = Picks.Where(p => p.IsChecked).Select(p => p.Tag).OfType<ProfileCleanPlan>().ToList();
        if (chosen.Count == 0 || _installPath is not { } installPath || _targets is not { } targets) return null;

        var result = await Task.Run(() =>
        {
            if (TurnsOnSptFix && SptProfileFixes.ConfigPath(installPath) is { } config) SptProfileFixes.TurnOn(config);
            return ProfileCleaner.Clean(installPath, chosen, targets, AppServices.ProfileBackups);
        });

        return result.Failure switch
        {
            null => Strings.ProfileClean_Done(result.Cleaned.Count, result.Cleaned.Count),
            ProfileCleanFailure.BackupFailed => Strings.ProfileClean_BackupFailed,
            _ => LocalizationService.Text(Strings.ProfileClean_FailedFormat, result.FailedProfile ?? string.Empty),
        };
    }

    private static string Describe(IReadOnlyList<ProfileCleanThing> things)
    {
        var parts = new List<string>();

        var skills = things.Where(t => t.Kind == ProfileCleanKind.Skill).Select(t => Spaced(t.Value)).ToList();
        if (skills.Count > 0) parts.Add(LocalizationService.Text(Strings.ProfileClean_SkillsFormat, TextLists.Join(skills)));

        var roles = things.Where(t => t.Kind == ProfileCleanKind.BotRole).Select(t => Spaced(t.Value)).ToList();
        if (roles.Count > 0) parts.Add(LocalizationService.Text(Strings.ProfileClean_DeathCauseFormat, TextLists.Join(roles)));

        var items = things.Where(t => t.Kind == ProfileCleanKind.Item).Sum(t => t.Count);
        if (items > 0) parts.Add(Strings.ProfileItems_Items(items, items));

        var traders = things.Count(t => t.Kind == ProfileCleanKind.Trader);
        if (traders > 0) parts.Add(Strings.ProfileItems_Traders(traders, traders));

        return string.Join(Strings.Common_ListSeparator, parts);
    }

    private static string Spaced(string name) => WordBreak().Replace(name, " ");

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex WordBreak();
}
