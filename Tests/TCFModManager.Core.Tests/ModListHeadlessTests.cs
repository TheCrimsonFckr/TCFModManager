using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// A Fika headless client is a third kind of machine on a server's list.
//
// It runs the game, so the mods that decide how a raid plays are its business - bots, items,
// locations. It has nobody sitting at it, so the ones that draw things at a player are not. And
// nothing on disk separates the two: SAIN and a HUD widget are both a DLL in BepInEx\plugins.
//
// So capture gives the headless every plugin and the operator prunes. These tests pin down that
// direction, because the two ways of being wrong are not equal - a headless carrying a mod it did
// not need costs nothing anyone can see, and one missing a mod it did is felt by everybody in the
// raid it is hosting.
//
public class ModListHeadlessTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    private const ModListEntryScope ClientsAndHeadless =
        ModListEntryScope.Client | ModListEntryScope.Headless;

    private static ModListEntry Entry(string name, ModListEntryScope scope) =>
        new() { Name = name, Scope = scope, Folders = [name.ToLowerInvariant()] };

    private static ModList List(ModListOrigin origin, params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "The server",
            Origin = origin,
            Policy = ModListPolicy.Exclusive,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        list.Entries.AddRange(entries);
        return list;
    }

    private static ModListCandidate Installed(string name) =>
        new() { Name = name, Folders = [name.ToLowerInvariant()] };

    //
    // The server's list, as an operator ends up with it: capture tagged every plugin
    // Clients + headless, they pruned the HUD mod down to players, and the server mods stayed put.
    //
    private static ModList Served() => List(
        ModListOrigin.Server,
        Entry("SAIN", ClientsAndHeadless),
        Entry("Waypoints", ClientsAndHeadless),
        Entry("Tyfon.UIFixes", ModListEntryScope.Client),
        Entry("fika-server", ModListEntryScope.Server),
        Entry("HeadlessTweaks", ModListEntryScope.Headless));

    private static readonly ModListEntryScope Player = InstallRole.ScopeFor(InstallRoles.Player);

    private static readonly ModListEntryScope Dedicated = InstallRole.ScopeFor(InstallRoles.Headless);

    private static readonly ModListEntryScope PlaysAndHosts =
        InstallRole.ScopeFor(InstallRoles.Player | InstallRoles.Headless);

    // The whole feature in one assertion.
    [Fact]
    public void ADedicatedHeadlessTakesTheRaidModsAndSkipsThePlayerOnes()
    {
        var names = ModListPlanner.Build(Served(), [], machine: Dedicated).Actions.Select(a => a.Name).ToList();

        Assert.Contains("SAIN", names);
        Assert.Contains("Waypoints", names);
        Assert.Contains("HeadlessTweaks", names);

        Assert.DoesNotContain("Tyfon.UIFixes", names);
    }

    //
    // Server-scoped entries reach a headless box, which is the one place this design deliberately
    // differs from a player.
    //
    // The box is a full SPT install, and a server-only entry is a whole mod rather than half of one:
    // taking it is an ordinary install. The alternative - leaving the server half off a mod that has
    // both - would mean pulling an archive apart after it was staged, which is where unforeseen
    // problems live.
    //
    [Fact]
    public void AHeadlessBoxTakesTheServerModsToo()
    {
        Assert.Contains(
            ModListPlanner.Build(Served(), [], machine: Dedicated).Actions,
            a => a.Name == "fika-server");
    }

    // Nothing about the headless changes what a player does. This is the regression guard on the
    // shipped behaviour.
    [Fact]
    public void APlayerSeesExactlyWhatTheySawBefore()
    {
        var names = ModListPlanner.Build(Served(), [], machine: Player).Actions.Select(a => a.Name).ToList();

        Assert.Contains("SAIN", names);
        Assert.Contains("Tyfon.UIFixes", names);

        Assert.DoesNotContain("fika-server", names);
        Assert.DoesNotContain("HeadlessTweaks", names);
    }

    // A machine somebody plays on that also hosts is both roles at once, not a third mode.
    [Fact]
    public void AMachineThatPlaysAndHostsTakesTheLot()
    {
        Assert.Equal(5, ModListPlanner.Build(Served(), [], machine: PlaysAndHosts).Actions.Count);
    }

    //
    // A skipped entry still claims its installed mod, or the Exclusive sweep would offer to disable
    // a mod purely because this machine was not the one meant to install it. Doubly important here:
    // a headless box may well have a HUD mod sitting in its plugins folder from before it was a
    // headless box.
    //
    [Fact]
    public void AServedListNeverDisablesWhatItSkipped()
    {
        var plan = ModListPlanner.Build(
            Served(),
            [Installed("Tyfon.UIFixes"), Installed("SAIN")],
            machine: Dedicated);

        Assert.DoesNotContain(plan.Actions, a => a.Kind == ModListActionKind.Disable);
    }

    //
    // Protection follows the same filter. A player-only entry on the server's list is not something
    // the server needs THIS machine to be running, so it does not pin a mod on a box nobody looks
    // at - while everything the server does expect of it stays safe from the user's own sweep.
    //
    [Fact]
    public void ProtectionCoversOnlyWhatTheServerExpectsOfThisMachine()
    {
        var mine = List(ModListOrigin.Local, Entry("A sound pack", ModListEntryScope.Everyone));

        var plan = ModListPlanner.Build(
            mine,
            [Installed("SAIN"), Installed("Tyfon.UIFixes")],
            alsoRequiredBy: Served(),
            machine: Dedicated);

        // Required of this machine by the server, so the sweep leaves it alone.
        Assert.DoesNotContain(plan.Actions, a => a.Name == "SAIN" && a.Kind == ModListActionKind.Disable);

        // Not required of this machine, and not on the user's own list either.
        Assert.Contains(plan.Actions, a => a.Name == "Tyfon.UIFixes" && a.Kind == ModListActionKind.Disable);
    }

    // Dedicated is Headless without Player, rather than a value of its own.
    [Fact]
    public void RolesMapToScopes()
    {
        Assert.Equal(ModListEntryScope.Client, Player);
        Assert.Equal(ModListEntryScope.Headless | ModListEntryScope.Server, Dedicated);
        Assert.Equal(ModListEntryScope.Everyone, PlaysAndHosts);

        Assert.True(InstallRole.IsDedicatedHeadless(InstallRoles.Headless));
        Assert.False(InstallRole.IsDedicatedHeadless(InstallRoles.Player | InstallRoles.Headless));

        // An install nobody has been asked about is somebody's own game, not a headless box.
        Assert.Equal(ModListEntryScope.Client, InstallRole.ScopeFor(InstallRoles.None));
    }

    //
    // The schema moves only for a list that says something an older app could not read. A list using
    // nothing but the old three values is still a 3, and still opens in a build from last week.
    //
    [Fact]
    public void TheSchemaOnlyMovesWhenAnEntryNamesTheHeadless()
    {
        var oldShape = List(
            ModListOrigin.Local,
            Entry("SAIN", ModListEntryScope.Client),
            Entry("fika-server", ModListEntryScope.Server));

        Assert.Equal(ModListFile.ScopeSchemaVersion, ModListFile.SchemaVersionFor(oldShape));

        var withHeadless = List(ModListOrigin.Local, Entry("SAIN", ClientsAndHeadless));

        Assert.Equal(ModListFile.HeadlessSchemaVersion, ModListFile.SchemaVersionFor(withHeadless));

        // An entry that says nothing at all still says nothing, so a plain list stays at 1.
        var plain = List(ModListOrigin.Local, new ModListEntry { Name = "SAIN" });

        Assert.Equal(ModListFile.BaseSchemaVersion, ModListFile.SchemaVersionFor(plain));
    }

    //
    // A list published before this existed keeps working, and keeps meaning what its author meant.
    //
    // At schema 3, Client meant "not the server" - the only two machines the format could describe.
    // Read literally now it would mean "players and not the headless", and an operator's published
    // list would quietly stop delivering the bot mods to the machine hosting the raid.
    //
    [Fact]
    public void AListFromBeforeTheHeadlessStillReachesOne()
    {
        var restored = ModListFile.Read(Document(ModListFile.ScopeSchemaVersion, "Client"),
            "127.0.0.1:6969", ModListOrigin.Server).List!;

        Assert.Equal(ClientsAndHeadless, restored.Entries.Single().EffectiveScope);
    }

    // The old spelling of the whole set. Every file already exported says this, and they are on other
    // people's machines and inside servers' config folders - it has to keep reading forever.
    [Fact]
    public void TheOldBothStillReadsAsEveryone()
    {
        var restored = ModListFile.Read(Document(ModListFile.ScopeSchemaVersion, "Both"),
            "127.0.0.1:6969", ModListOrigin.Server).List!;

        Assert.Equal(ModListEntryScope.Everyone, restored.Entries.Single().EffectiveScope);
    }

    //
    // The other side of the widening, and the reason it is keyed off the document's version rather
    // than the entry's shape: at schema 4, Client is a choice somebody made on purpose, and it has
    // to survive.
    //
    [Fact]
    public void AtSchemaFourClientMeansPlayersOnly()
    {
        var restored = ModListFile.Read(Document(ModListFile.HeadlessSchemaVersion, "Client"),
            "127.0.0.1:6969", ModListOrigin.Server).List!;

        Assert.Equal(ModListEntryScope.Client, restored.Entries.Single().EffectiveScope);
    }

    //
    // Everyone and "said nothing" are one state, not two.
    //
    // They were two in the first cut, and it cost a real bug: an entry built with Scope = Everyone
    // wrote "Scope": "Everyone" into the file, which stamped an ordinary list at schema 4 - refused
    // outright by an older app - in order to record a value that means exactly what silence means.
    //
    [Fact]
    public void EveryoneIsStoredAsSilence()
    {
        var entry = new ModListEntry { Name = "SAIN", Scope = ModListEntryScope.Everyone };

        Assert.Null(entry.Scope);
        Assert.Equal(ModListEntryScope.Everyone, entry.EffectiveScope);
    }

    //
    // The scope converter is actually the one in use, in both directions.
    //
    // It was not, to begin with. The attribute sat on the enum type, and both ModListFile and
    // ModListStore register a plain JsonStringEnumConverter in their options - which outranks a
    // type-level attribute. So the custom converter never ran, and every list already exported, all
    // of which say "Both", came back as an unreadable file. The attribute now sits on the property,
    // which outranks both; this test is what would notice it moving back.
    //
    [Fact]
    public void TheScopeConverterIsTheOneInUse()
    {
        var list = List(ModListOrigin.Local, Entry("SAIN", ClientsAndHeadless));

        var json = ModListFile.Write(list);

        Assert.Contains("\"Scope\": \"Client, Headless\"", json);

        var restored = ModListFile.Read(json, "127.0.0.1:6969", ModListOrigin.Server).List!;

        Assert.Equal(ClientsAndHeadless, restored.Entries.Single().EffectiveScope);
    }

    //
    // Written by hand rather than by ModListFile.Write, because the whole point is a document this
    // build would not produce - one from an older app.
    //
    private static string Document(int schemaVersion, string scope) =>
        $$"""
          {
            "SchemaVersion": {{schemaVersion}},
            "App": "TCFModManager",
            "ExportedAt": "2026-09-08T10:00:00+00:00",
            "List": {
              "Id": "0f9d3f4e-1c2b-4a5d-9e8f-7a6b5c4d3e2f",
              "Name": "The server",
              "Revision": 4,
              "Policy": "Exclusive",
              "CreatedAt": "2026-09-08T10:00:00+00:00",
              "UpdatedAt": "2026-09-08T10:00:00+00:00",
              "Entries": [
                { "Name": "SAIN", "Scope": "{{scope}}", "Folders": [ "sain" ] }
              ]
            }
          }
          """;
}
