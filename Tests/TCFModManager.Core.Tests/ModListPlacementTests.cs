using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// OPEN-44 §1 replayed: Chris's 4.1 "Client + Headless playlist" as it stood on 2026-11-11, with the
// real sp-mod ids, and the facts his install held. Every L1-L6 row has to come out of the rules,
// and nothing else may.
//
public class ModListPlacementTests
{
    private const ModListEntryScope ClientAndHeadless = ModListEntryScope.Client | ModListEntryScope.Headless;

    private const ModListEntryScope ServerAndClient = ModListEntryScope.Server | ModListEntryScope.Client;

    private static ModListEntry Mod(int id, string name, ModListEntryScope? scope, params string[] folders) =>
        new() { Name = name, ModId = id, Scope = scope, Folders = [.. folders] };

    private static ModListEntry AddonEntry(int id, string name, ModListEntryScope scope, string folder) =>
        new() { Name = name, ModId = id, IsAddon = true, Scope = scope, Folders = [folder] };

    private static Addon Addon(int id, int parent, string name, string teaser) =>
        new() { Id = id, ModId = parent, Name = name, Teaser = teaser };

    // The list, 2026-11-11, before any of the fixes.
    private static readonly ModListEntry SkillsExtended = Mod(2383, "Skills Extended", null, "skillsextended");
    private static readonly ModListEntry ClimbableLadders = Mod(2649, "Climbable Ladders", ClientAndHeadless, "tarkin-ladders");
    private static readonly ModListEntry MergeConsumables = Mod(1657, "MergeConsumables", ServerAndClient, "mergeconsumables", "mergeconsumablesserver");
    private static readonly ModListEntry MergeConsumablesSync = AddonEntry(4, "MergeConsumables - Fika sync", ClientAndHeadless, "mergeconsumables-fika-sync");
    private static readonly ModListEntry StanceOverhaul = Mod(3136, "SPT Stance Overhaul", ModListEntryScope.Client, "realism");
    private static readonly ModListEntry StanceOverhaulSync = AddonEntry(163, "Stance Overhaul Fika Sync", ClientAndHeadless, "stanceoverhaulfika");
    private static readonly ModListEntry RealismCommonLib = Mod(3135, "SPT Realism Common Lib", ClientAndHeadless, "spt-realism-common-lib");
    private static readonly ModListEntry FovFix = Mod(701, "Fontaine's FOV Fix", ModListEntryScope.Client, "fovfix");
    private static readonly ModListEntry Emporium = Mod(2882, "Eco's Attachment Emporium", ClientAndHeadless, "eco-attachment-emporium");
    private static readonly ModListEntry Wa2000 = Mod(3063, "Walther WA 2000 Sniper Rifle Continued", ServerAndClient, "wa2000nativeaudio", "wa2000");
    private static readonly ModListEntry HalloweenMasks = Mod(2401, "WTT Halloween Masks", ServerAndClient, "wtthalloweenclient", "wtt_halloween");
    private static readonly ModListEntry Shotguns = Mod(1453, "Make Shotguns Great Again!", ServerAndClient, "makeshotgunsgreatagain");
    private static readonly ModListEntry Pbt = Mod(3261, "PBT - Progressive Bot Profiles", null, "pbt");
    private static readonly ModListEntry Apbs = Mod(1594, "APBS - Acid's Progressive Bot System", null, "acidphantasm-progressivebotsystem");
    private static readonly ModListEntry WttCommonLib = Mod(2310, "WTT - CommonLib", null, "wtt-clientcommonlib", "wtt-servercommonlib");
    private static readonly ModListEntry UseItemsAnywhere = Mod(2386, "Use Items Anywhere", ClientAndHeadless, "useitemsanywhere");
    private static readonly ModListEntry UseItemsAnywhereSync = AddonEntry(158, "Use Items Anywhere Fika Sync", ClientAndHeadless, "use-items-anywhere-fika-sync");
    private static readonly ModListEntry Sain = Mod(1, "SAIN", ClientAndHeadless, "sain");
    private static readonly ModListEntry UiFixes = Mod(2, "UI Fixes", ModListEntryScope.Client, "tyfon.uifixes");

    private static readonly ModListEntry[] Entries =
    [
        SkillsExtended, ClimbableLadders, MergeConsumables, MergeConsumablesSync, StanceOverhaul, StanceOverhaulSync,
        RealismCommonLib, FovFix, Emporium, Wa2000, HalloweenMasks, Shotguns, Pbt, Apbs, WttCommonLib,
        UseItemsAnywhere, UseItemsAnywhereSync, Sain, UiFixes,
    ];

    // The addon cache, as far as these parents go. Fika Bot Possession is the Q5 odd one out.
    private static readonly Addon[] Cache =
    [
        Addon(11, 2383, "Skills Extended Fika Sync", "Skills Extended fika sync"),
        Addon(39, 2649, "Climbable Ladders - Fika sync", "Fika Ladders"),
        Addon(4, 1657, "MergeConsumables - Fika sync", "Adds Fika compatibility to merge consumables."),
        Addon(163, 3136, "Stance Overhaul Fika Sync", "It syncs"),
        Addon(158, 2386, "Use Items Anywhere Fika Sync", "Syncs UIA for coffee enjoyers"),
        Addon(178, 1, "Fika Bot Possession", "Multiplayer bot possession for Fika. Die or extract, take over a living bot."),
        Addon(40, 2383, "Essential Texture Pack", "Textures"),
    ];

    private static PlacementFacts Facts() => new()
    {
        Entries = Entries,
        Addons = Cache,
        Halves = new Dictionary<ModListEntry, PlacementHalves>
        {
            [SkillsExtended] = new(HasClient: true, HasServer: true),
            [ClimbableLadders] = new(HasClient: true, HasServer: false),
            [MergeConsumables] = new(HasClient: true, HasServer: true),
            [StanceOverhaul] = new(HasClient: true, HasServer: false),
            [RealismCommonLib] = new(HasClient: true, HasServer: false),
            [FovFix] = new(HasClient: true, HasServer: false),
            [Emporium] = new(HasClient: false, HasServer: true),
            [Wa2000] = new(HasClient: true, HasServer: true, ShipsItems: true),
            [HalloweenMasks] = new(HasClient: true, HasServer: true, ShipsItems: true),
            [Shotguns] = new(HasClient: true, HasServer: true, ShipsItems: true),
            [Pbt] = new(HasClient: true, HasServer: true),
            [Apbs] = new(HasClient: false, HasServer: true),
            [WttCommonLib] = new(HasClient: true, HasServer: true),
            [UseItemsAnywhere] = new(HasClient: true, HasServer: false),
            [Sain] = new(HasClient: true, HasServer: true),
            [UiFixes] = new(HasClient: true, HasServer: true),
        },
        Dependencies =
        [
            new(Pbt, 1594),
            new(StanceOverhaul, 3135),
            new(HalloweenMasks, 2310),
            new(Wa2000, 2310),
        ],
        Advice = new Dictionary<ModListEntry, HeadlessAdvice>
        {
            [StanceOverhaul] = new(HeadlessVerdict.Mentions, ["Only install SO and the common lib on the player's machines."]),
            [StanceOverhaulSync] = HeadlessAdviceReader.Read("<p>Headless client : ONLY FIKA SYNC</p><p>Player client : everything</p>"),
            [SkillsExtended] = new(HeadlessVerdict.Needs, ["The Fika integration is needed on the host, every player and the headless."]),
        },
    };

    private static IReadOnlyList<PlacementFinding> Findings() => ModListPlacement.Check(Facts());

    private static IEnumerable<PlacementFinding> Of(PlacementRule rule) => Findings().Where(f => f.Rule == rule);

    // L1: the four sync addons whose parents are on the list, on no list. Two of the four are on
    // this one already (the fixture keeps MergeConsumables and Stance Overhaul syncs on), so the
    // other two come out, at the parent's scope minus the server.
    [Fact]
    public void L1_SyncAddonsOnNoListAreOffered()
    {
        var offered = Of(PlacementRule.SyncAddonAvailable).ToList();

        var skills = Assert.Single(offered, f => f.Addon!.Id == 11);
        Assert.Same(SkillsExtended, skills.Entry);
        Assert.Equal(ClientAndHeadless, skills.AddonScope);
        Assert.Equal(PlacementSeverity.Hint, skills.Severity);

        var ladders = Assert.Single(offered, f => f.Addon!.Id == 39);
        Assert.Same(ClimbableLadders, ladders.Entry);
        Assert.Equal(ClientAndHeadless, ladders.AddonScope);

        Assert.Equal(2, offered.Count);
    }

    // Q5: Fika Bot Possession matches the words but is a feature, so it is shown and not offered;
    // a texture pack addon says nothing about Fika and does not appear at all.
    [Fact]
    public void Q5_AFikaAddonWithoutSyncIsInformationOnly()
    {
        var info = Assert.Single(Of(PlacementRule.FikaAddonAvailable));

        Assert.Equal(178, info.Addon!.Id);
        Assert.Same(Sain, info.Entry);
        Assert.Null(info.AddonScope);
        Assert.Equal(PlacementSeverity.Info, info.Severity);

        Assert.DoesNotContain(Findings(), f => f.Addon?.Id == 40);
    }

    // L2: the MergeConsumables sync reaches the headless, its parent does not. A sync addon widens
    // the parent (Q3) and offers nothing else.
    [Fact]
    public void L2_AnAddonWiderThanItsParentWidensTheParent()
    {
        var wider = Assert.Single(Of(PlacementRule.AddonWiderThanParent));

        Assert.Same(MergeConsumables, wider.Entry);
        Assert.Same(MergeConsumablesSync, wider.Other);
        Assert.Equal(ModListEntryScope.Everyone, wider.SuggestedScope);
        Assert.Null(wider.AlternativeScope);
        Assert.Equal(PlacementSeverity.Warning, wider.Severity);
    }

    // L3 + L10: Stance Overhaul's sync says the headless wants only the sync, so the parent is left
    // off the headless (no R2 on it) and the common lib it depends on is told to come off too.
    [Fact]
    public void L10_OnlyTheSyncKeepsTheParentAndItsLibraryOffTheHeadless()
    {
        var only = Of(PlacementRule.OnlyTheSync).ToList();

        var lib = Assert.Single(only, f => ReferenceEquals(f.Entry, RealismCommonLib));
        Assert.Equal(PlacementSeverity.Warning, lib.Severity);
        Assert.Equal(ModListEntryScope.Client, lib.SuggestedScope);
        Assert.Same(StanceOverhaulSync, lib.Other);

        var parent = Assert.Single(only, f => ReferenceEquals(f.Entry, StanceOverhaul));
        Assert.Equal(PlacementSeverity.Info, parent.Severity);
        Assert.Null(parent.SuggestedScope);

        Assert.DoesNotContain(Of(PlacementRule.AddonWiderThanParent), f => ReferenceEquals(f.Entry, StanceOverhaul));
        Assert.DoesNotContain(Of(PlacementRule.LibraryCanLeaveHeadless), f => ReferenceEquals(f.Entry, RealismCommonLib));
    }

    // L3: the quote on Stance Overhaul's page names the common lib, so the lib's row carries it too.
    [Fact]
    public void L3_AdviceNamingAnotherEntryIsAttachedToIt()
    {
        var named = Assert.Single(Of(PlacementRule.AdviceNamesEntry));

        Assert.Same(RealismCommonLib, named.Entry);
        Assert.Same(StanceOverhaul, named.Other);
        Assert.Contains("common lib", named.Quote, StringComparison.OrdinalIgnoreCase);
    }

    // L4: a server-only mod scoped to players and the headless has nothing to load there.
    [Fact]
    public void L4_AServerOnlyModScopedOffTheServerGoesBackToIt()
    {
        var nothing = Assert.Single(Of(PlacementRule.NothingToLoad));

        Assert.Same(Emporium, nothing.Entry);
        Assert.Equal(ModListEntryScope.Server, nothing.SuggestedScope);
    }

    // L5: three item mods narrowed to Server + Client, while the headless runs the bots.
    [Fact]
    public void L5_ItemModsTakenOffTheHeadlessWarn()
    {
        var bots = Of(PlacementRule.BotsCarryItems).Select(f => f.Entry).ToHashSet();

        Assert.Equal(new HashSet<ModListEntry> { Wa2000, HalloweenMasks, Shotguns }, bots);
        Assert.All(Of(PlacementRule.BotsCarryItems), f => Assert.Equal(ModListEntryScope.Everyone, f.SuggestedScope));
    }

    // L6: PBT needs APBS, but APBS has no client half, so nothing is asked of the headless or the
    // players for it. WTT-CommonLib does have one, and the two item mods are on every machine it
    // is, so nothing is asked there either.
    [Fact]
    public void L6_AServerOnlyDependencyIsMetByTheServer()
    {
        Assert.Empty(Of(PlacementRule.DependencyUncovered));
        Assert.Empty(Of(PlacementRule.LibraryCanLeaveHeadless));
    }

    // And nothing else: every finding is one of the rows above.
    [Fact]
    public void NothingElseComesOut()
    {
        var expected = new Dictionary<PlacementRule, int>
        {
            [PlacementRule.SyncAddonAvailable] = 2,
            [PlacementRule.FikaAddonAvailable] = 1,
            [PlacementRule.AddonWiderThanParent] = 1,
            [PlacementRule.OnlyTheSync] = 2,
            [PlacementRule.AdviceNamesEntry] = 1,
            [PlacementRule.NothingToLoad] = 1,
            [PlacementRule.BotsCarryItems] = 3,
        };

        var counts = Findings().GroupBy(f => f.Rule).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(expected, counts);
    }

    //
    // The rules on their own.
    //

    [Fact]
    public void R2_ANonSyncAddonOffersBothWaysOut()
    {
        var parent = Mod(10, "Parent", ModListEntryScope.Client, "parent");
        var addon = AddonEntry(20, "Parent - Extra Maps", ClientAndHeadless, "parent-extra-maps");

        var finding = Assert.Single(ModListPlacement.Check(new PlacementFacts
        {
            Entries = [parent, addon],
            Addons = [Addon(20, 10, "Parent - Extra Maps", "More maps")],
        }));

        Assert.Equal(PlacementRule.AddonWiderThanParent, finding.Rule);
        Assert.Equal(ClientAndHeadless, finding.SuggestedScope);
        Assert.Equal(ModListEntryScope.Client, finding.AlternativeScope);
    }

    [Fact]
    public void R3_ADependencyWithAClientHalfMustReachEveryMachineTheDependentDoes()
    {
        var mod = Mod(10, "Mod", ClientAndHeadless, "mod");
        var lib = Mod(11, "Lib", ModListEntryScope.Client, "lib");

        var finding = Assert.Single(ModListPlacement.Check(new PlacementFacts
        {
            Entries = [mod, lib],
            Halves = new Dictionary<ModListEntry, PlacementHalves> { [lib] = new(true, false), [mod] = new(true, false) },
            Dependencies = [new(mod, 11)],
        }));

        Assert.Equal(PlacementRule.DependencyUncovered, finding.Rule);
        Assert.Same(lib, finding.Entry);
        Assert.Same(mod, finding.Other);
        Assert.Equal(ClientAndHeadless, finding.SuggestedScope);
    }

    [Fact]
    public void R3_ALibraryNoDependentKeepsOnTheHeadlessCanLeaveIt()
    {
        var mod = Mod(10, "Mod", ModListEntryScope.Client, "mod");
        var lib = Mod(11, "Lib", ClientAndHeadless, "lib");

        var finding = Assert.Single(ModListPlacement.Check(new PlacementFacts
        {
            Entries = [mod, lib],
            Halves = new Dictionary<ModListEntry, PlacementHalves> { [lib] = new(true, false), [mod] = new(true, false) },
            Dependencies = [new(mod, 11)],
        }));

        Assert.Equal(PlacementRule.LibraryCanLeaveHeadless, finding.Rule);
        Assert.Equal(PlacementSeverity.Hint, finding.Severity);
        Assert.Equal(ModListEntryScope.Client, finding.SuggestedScope);
    }

    [Fact]
    public void R4_APluginOnlyModScopedToTheServerAloneGoesToTheMachinesThatLoadIt()
    {
        var mod = Mod(10, "Mod", ModListEntryScope.Server, "mod");

        var finding = Assert.Single(ModListPlacement.Check(new PlacementFacts
        {
            Entries = [mod],
            Halves = new Dictionary<ModListEntry, PlacementHalves> { [mod] = new(true, false) },
        }));

        Assert.Equal(PlacementRule.NothingToLoad, finding.Rule);
        Assert.Equal(ClientAndHeadless, finding.SuggestedScope);
    }

    // A server-only mod that does name the server is never flagged - Server alone reaches a headless.
    [Fact]
    public void R4_AServerOnlyModOnEveryMachineIsLeftAlone()
    {
        var mod = Mod(10, "Mod", null, "mod");

        Assert.Empty(ModListPlacement.Check(new PlacementFacts
        {
            Entries = [mod],
            Halves = new Dictionary<ModListEntry, PlacementHalves> { [mod] = new(false, true) },
        }));
    }

    [Fact]
    public void R5_NeverFiresWithoutTheItemSignalOrWithTheHeadlessOn()
    {
        var plain = Mod(10, "Plain", ServerAndClient, "plain");
        var items = Mod(11, "Items", null, "items");

        Assert.Empty(ModListPlacement.Check(new PlacementFacts
        {
            Entries = [plain, items],
            Halves = new Dictionary<ModListEntry, PlacementHalves>
            {
                [plain] = new(true, true),
                [items] = new(true, true, ShipsItems: true),
            },
        }));
    }

    [Fact]
    public void R6_ShortAndGenericWordsNeverMatch()
    {
        var ui = Mod(10, "UI", ModListEntryScope.Client, "ui");
        var sync = Mod(11, "Fika Sync", ModListEntryScope.Client, "fs");
        var source = Mod(12, "Source", ModListEntryScope.Client, "source");

        Assert.Empty(ModListPlacement.Check(new PlacementFacts
        {
            Entries = [ui, sync, source],
            Advice = new Dictionary<ModListEntry, HeadlessAdvice>
            {
                [source] = new(HeadlessVerdict.Mentions, ["Works with the UI and any fika sync on the headless."]),
            },
        }));
    }

    [Fact]
    public void R1_AParentScopedToTheServerAloneHasNoMachineToOfferASyncFor()
    {
        var parent = Mod(10, "Parent", ModListEntryScope.Server, "parent");

        Assert.Empty(ModListPlacement.Check(new PlacementFacts
        {
            Entries = [parent],
            Addons = [Addon(20, 10, "Parent Fika Sync", "sync")],
        }));
    }

    [Fact]
    public void R1_ANeedsRatedAddonCountsAsASyncEvenWithoutTheWord()
    {
        var parent = Mod(10, "Parent", ClientAndHeadless, "parent");

        var finding = Assert.Single(ModListPlacement.Check(new PlacementFacts
        {
            Entries = [parent],
            Addons = [Addon(20, 10, "Parent Fika Compatibility", "Fika support")],
            AddonAdvice = new Dictionary<int, HeadlessAdvice>
            {
                [20] = new(HeadlessVerdict.Needs, ["Install this on the headless as well."]),
            },
        }));

        Assert.Equal(PlacementRule.SyncAddonAvailable, finding.Rule);
        Assert.Equal(ClientAndHeadless, finding.AddonScope);
    }

    [Fact]
    public void Halves_ItemFoldersUnderDbOrResourcesShipItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "tcf-placement-" + Guid.NewGuid().ToString("N"));
        try
        {
            var armory = Path.Combine(root, "armory");
            var sound = Path.Combine(root, "sound");
            var dwv = Path.Combine(root, "dwv");
            var skills = Path.Combine(root, "skills");
            Directory.CreateDirectory(Path.Combine(armory, "db", "CustomItems"));
            Directory.CreateDirectory(Path.Combine(sound, "bundles"));
            Directory.CreateDirectory(Path.Combine(dwv, "db", "02_Items"));
            Directory.CreateDirectory(Path.Combine(skills, "Resources", "Items"));

            Assert.True(ModListPlacement.ShipsItemFolders(armory));
            Assert.False(ModListPlacement.ShipsItemFolders(sound));
            Assert.True(ModListPlacement.ShipsItemFolders(dwv));
            Assert.True(ModListPlacement.ShipsItemFolders(skills));
            Assert.False(ModListPlacement.ShipsItemFolders(Path.Combine(root, "missing")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Halves_ReadFromTheScanAndCommonLibCountsAsItems()
    {
        static InstalledMod Half(InstalledModTarget target) =>
            new() { Name = "x", Target = target, FolderPath = Path.Combine(Path.GetTempPath(), "tcf-none-" + Guid.NewGuid().ToString("N")) };

        var both = new ModListCandidate { Name = "Both", Entries = [Half(InstalledModTarget.Client), Half(InstalledModTarget.Server)] };
        var clientOnly = new ModListCandidate { Name = "Client", Entries = [Half(InstalledModTarget.Client)] };

        Assert.Equal(new PlacementHalves(true, true, false), ModListPlacement.HalvesOf(both));
        Assert.Equal(new PlacementHalves(true, true, true), ModListPlacement.HalvesOf(both, dependsOnCommonLib: true));
        Assert.Equal(new PlacementHalves(true, false, false), ModListPlacement.HalvesOf(clientOnly, dependsOnCommonLib: true));
        Assert.Null(ModListPlacement.HalvesOf(new ModListCandidate { Name = "Unseen" }));
    }
}
