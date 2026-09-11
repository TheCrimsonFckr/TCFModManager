using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Entry scope and what a served list is allowed to do, tested against the two mods that forced the
// design: the Server Map mod itself and fika-server. Both live in user\mods on the server, neither
// is something a playing client installs, and one of them is the mod publishing the list.
//
// The catch-22 this resolves: put them on the published list and every client is told to hand-install
// a server mod; leave them off and the operator applying their own published list disables the very
// thing serving it. Scope means one list can be right for both.
//
public class ModListScopeTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private static ModListEntry Entry(string name, ModListEntryScope scope = ModListEntryScope.Everyone,
        int? modId = null, int? versionId = null, string? version = null) =>
        new()
        {
            Name = name,
            Scope = scope,
            ModId = modId,
            VersionId = versionId,
            Version = version,
            Folders = [name.ToLowerInvariant()],
        };

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

    private static ModListCandidate Installed(string name, string? version = null, int? modId = null) =>
        new()
        {
            Name = name,
            ModId = modId,
            Version = version,
            Folders = [name.ToLowerInvariant()],
        };

    // The server's own published list: what clients need, plus the two server mods it runs.
    private static ModList Published(ModListOrigin origin) => List(
        origin,
        Entry("SAIN", ModListEntryScope.Client, modId: 2426, versionId: 5, version: "3.2.1"),
        Entry("Fika.Core", ModListEntryScope.Client, modId: 2359, versionId: 9, version: "1.1.0"),
        Entry("fika-server", ModListEntryScope.Server),
        Entry("TCFMM.ServerMap", ModListEntryScope.Server));

    //
    // On a client, a server-only entry is not an errand. Not installed, not flagged as missing, not
    // mentioned at all.
    //
    [Fact]
    public void AClientIsNeverToldToInstallTheServersOwnMods()
    {
        var plan = ModListPlanner.Build(Published(ModListOrigin.Server), []);

        Assert.DoesNotContain(plan.Actions, a => a.Name is "fika-server" or "TCFMM.ServerMap");
        Assert.Equal(2, plan.Actions.Count(a => a.Kind == ModListActionKind.Install));
    }

    //
    // The other half of the catch-22. The operator's own copy of the same list describes their own
    // install, so all four entries apply - including the mod that serves the list.
    //
    [Fact]
    public void TheOperatorApplyingTheirOwnListStillActsOnTheServerMods()
    {
        var plan = ModListPlanner.Build(Published(ModListOrigin.Local), []);

        Assert.Equal(4, plan.Actions.Count);
        Assert.Contains(plan.Actions, a => a.Name == "TCFMM.ServerMap");
    }

    //
    // The failure that started this. Without scope, the ServerMap mod is either on the list (and
    // pushed at clients) or off it (and disabled the moment the operator applies the list).
    //
    [Fact]
    public void TheModServingTheListIsNotDisabledByApplyingIt()
    {
        var plan = ModListPlanner.Build(
            Published(ModListOrigin.Local),
            [Installed("TCFMM.ServerMap"), Installed("fika-server")]);

        Assert.Empty(plan.Disable);
    }

    //
    // A server-only entry the client happens to have installed anyway is left alone - neither acted
    // on nor swept up as "not on the list".
    //
    [Fact]
    public void AServerModAClientHappensToHaveIsLeftAlone()
    {
        var plan = ModListPlanner.Build(
            Published(ModListOrigin.Server),
            [Installed("fika-server")]);

        Assert.DoesNotContain(plan.Actions, a => a.Name == "fika-server");
    }

    //
    // A served list never disables, whatever policy its author wrote. An operator's Exclusive list
    // describes THEIR install; applied verbatim it would disable mods the server has never heard of.
    //
    [Fact]
    public void AServedListNeverDisablesAnything()
    {
        var plan = ModListPlanner.Build(
            Published(ModListOrigin.Server),
            [Installed("My HUD tweak"), Installed("A sound pack")]);

        Assert.Empty(plan.Disable);
        Assert.Equal(ModListPolicy.Exclusive, plan.Policy);
    }

    // An imported file from a person, though, is a list you chose to apply - it behaves as before.
    [Fact]
    public void AnImportedListStillHonoursItsPolicy()
    {
        var plan = ModListPlanner.Build(
            List(ModListOrigin.Imported, Entry("SAIN", modId: 2426)),
            [Installed("My HUD tweak")]);

        Assert.Single(plan.Disable);
        Assert.Equal("My HUD tweak", plan.Disable.Single().Name);
    }

    // And so does one of your own, which is the case every existing list is.
    [Fact]
    public void YourOwnListStillHonoursItsPolicy()
    {
        var plan = ModListPlanner.Build(
            List(ModListOrigin.Local, Entry("SAIN", modId: 2426)),
            [Installed("My HUD tweak")]);

        Assert.Single(plan.Disable);
    }

    //
    // An entry that says nothing about scope means every machine, so a list written before scope
    // existed - and before the headless did - means exactly what it always meant.
    //
    [Fact]
    public void AListWithNoScopesBehavesAsItAlwaysDid()
    {
        var list = List(ModListOrigin.Server, Entry("SAIN"), Entry("Fika.Core"));

        Assert.Equal(2, ModListPlanner.Build(list, []).Actions.Count);
        Assert.All(list.Entries, e => Assert.Equal(ModListEntryScope.Everyone, e.EffectiveScope));
    }

    // Client-scoped entries apply to a client, obviously - but also to the operator's own install,
    // which has a BepInEx half like any other.
    [Fact]
    public void ClientEntriesApplyOnBothSides()
    {
        Assert.Contains(
            ModListPlanner.Build(Published(ModListOrigin.Server), []).Actions, a => a.Name == "SAIN");

        Assert.Contains(
            ModListPlanner.Build(Published(ModListOrigin.Local), []).Actions, a => a.Name == "SAIN");
    }

    //
    // Scope has to survive the trip, or the whole thing is decoration. It also bumps the share-file
    // schema - an app that does not know Scope reads a server-only entry as one to install, which is
    // precisely what the version exists to prevent.
    //
    [Fact]
    public void ScopeTravelsInTheShareFileAndBumpsTheSchemaWhenUsed()
    {
        var json = ModListFile.Write(Published(ModListOrigin.Local));

        Assert.Contains($"\"SchemaVersion\": {ModListFile.ScopeSchemaVersion}", json);

        var restored = ModListFile.Read(json, "127.0.0.1:6969", ModListOrigin.Server).List!;

        Assert.Equal(ModListEntryScope.Server, restored.Entries.Single(e => e.Name == "fika-server").EffectiveScope);

        //
        // Client, WIDENED to Client|Headless on the way in. The document is a schema 3, written when
        // "Client" was the only way to say "not the server" - so read literally it would now mean
        // "players and not the headless", a distinction its author had no way to make. See
        // ModListFile.WidenForHeadless.
        //
        Assert.Equal(
            ModListEntryScope.Client | ModListEntryScope.Headless,
            restored.Entries.Single(e => e.Name == "SAIN").EffectiveScope);
    }

    //
    // A list that does not use scope stays readable by an older app. Pinning every export to the
    // newest version would break sharing between versions to describe a feature the file never uses.
    //
    [Fact]
    public void AListWithoutScopesIsNotStampedWithTheNewerSchema()
    {
        var json = ModListFile.Write(List(ModListOrigin.Local, Entry("SAIN", modId: 2426)));

        Assert.Contains($"\"SchemaVersion\": {ModListFile.BaseSchemaVersion}", json);
        Assert.DoesNotContain("Scope", json);
    }

    //
    // Which list this machine publishes is local bookkeeping and has no business travelling - a list
    // you receive is never your published one, whatever the sender had marked.
    //
    // It lives on ModListData as a pointer rather than on the list, so this is now structural: there
    // is no field for a share file to carry. Asserted anyway, because the first version DID put it
    // on the list and the [JsonIgnore] that kept it out of this file is exactly what kept it out of
    // mod_lists.json too - see ModListStoreTests.SetPublished_SurvivesAReload.
    //
    [Fact]
    public void WhichListIsPublishedDoesNotTravel()
    {
        var json = ModListFile.Write(Published(ModListOrigin.Local));

        Assert.DoesNotContain("Purpose", json);
        Assert.DoesNotContain("Published", json);
    }

    //
    // Scope is inferred at capture from where a mod's files land, so an operator publishing their
    // install does not tag twenty mods by hand.
    //
    [Fact]
    public void CaptureCarriesTheScopeTheScannerAlreadyKnows()
    {
        var entries = ModListCapture.BuildEntries(
        [
            new ModListCandidate { Name = "SAIN", Scope = ModListEntryScope.Client },
            new ModListCandidate { Name = "fika-server", Scope = ModListEntryScope.Server },
            new ModListCandidate { Name = "Fika", Scope = ModListEntryScope.Everyone },
        ]);

        Assert.Equal(ModListEntryScope.Client, entries.Single(e => e.Name == "SAIN").EffectiveScope);
        Assert.Equal(ModListEntryScope.Server, entries.Single(e => e.Name == "fika-server").EffectiveScope);
        Assert.Equal(ModListEntryScope.Everyone, entries.Single(e => e.Name == "Fika").EffectiveScope);

        // Everyone is stored as nothing at all, so an entry that concerns every machine leaves no
        // trace in the file and a list that does not use scope is byte-identical to one from before
        // scope existed.
        Assert.Null(entries.Single(e => e.Name == "Fika").Scope);
    }
}

//
// Following a server's list AND your own at the same time.
//
// These are two different questions - what the server needs, and what else you like running - and
// they used to share one slot, which forced a choice nobody should have to make: follow the server
// and have your own Exclusive list set aside every mod it requires, or keep your list and fall
// behind the server.
//
public class ModListCoexistenceTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private static ModListEntry Entry(string name, ModListEntryScope scope = ModListEntryScope.Everyone) =>
        new() { Name = name, Scope = scope, Folders = [name.ToLowerInvariant()] };

    private static ModList List(ModListOrigin origin, params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = origin == ModListOrigin.Server ? "The server" : "My mods",
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

    // What the server requires, including the two server-side mods no client installs.
    private static ModList ServerList() => List(
        ModListOrigin.Server,
        Entry("SAIN", ModListEntryScope.Client),
        Entry("Fika.Core", ModListEntryScope.Client),
        Entry("fika-server", ModListEntryScope.Server));

    // What the player likes running on top of it.
    private static ModList MyList() => List(ModListOrigin.Local, Entry("My HUD tweak"), Entry("A sound pack"));

    //
    // The failure this exists to prevent: applying your own Exclusive list while following a server
    // would set aside every mod that server requires, and the next raid is a version mismatch.
    //
    [Fact]
    public void MyOwnListDoesNotDisableWhatTheServerRequires()
    {
        var installed = new[]
        {
            Installed("SAIN"), Installed("Fika.Core"),
            Installed("My HUD tweak"), Installed("A sound pack"),
        };

        var plan = ModListPlanner.Build(MyList(), installed, alsoRequiredBy: ServerList());

        Assert.Empty(plan.Disable);
    }

    // Without the server list it behaves exactly as it always did - the server's mods are simply
    // mods this list does not name.
    [Fact]
    public void AndStillDisablesThemWhenNoServerIsBeingFollowed()
    {
        var plan = ModListPlanner.Build(MyList(), [Installed("SAIN"), Installed("My HUD tweak")]);

        Assert.Single(plan.Disable);
        Assert.Equal("SAIN", plan.Disable.Single().Name);
    }

    //
    // Protection is not a blanket amnesty. A mod on neither list is still swept, or "Exclusive"
    // would stop meaning anything the moment a server was followed.
    //
    [Fact]
    public void SomethingOnNeitherListIsStillSetAside()
    {
        var plan = ModListPlanner.Build(
            MyList(),
            [Installed("SAIN"), Installed("My HUD tweak"), Installed("Something else entirely")],
            alsoRequiredBy: ServerList());

        Assert.Single(plan.Disable);
        Assert.Equal("Something else entirely", plan.Disable.Single().Name);
    }

    // Protection matches the same way the planner matches anything else - by folder as well as name,
    // since a listing title and its folder are routinely nothing like each other.
    [Fact]
    public void ProtectionMatchesOnFolderNamesToo()
    {
        var server = List(ModListOrigin.Server, new ModListEntry
        {
            Name = "Solarint's Advanced AI",
            Scope = ModListEntryScope.Client,
            Folders = ["sain"],
        });

        var plan = ModListPlanner.Build(MyList(), [Installed("SAIN")], alsoRequiredBy: server);

        Assert.Empty(plan.Disable);
    }

    // The user's own pins survive alongside the server's mods - one set, not one replacing the other.
    [Fact]
    public void MyPinsAndTheServersModsAreBothProtected()
    {
        var plan = ModListPlanner.Build(
            MyList(),
            [Installed("SAIN"), Installed("Something I pinned")],
            neverAutoDisable: new HashSet<string> { "something i pinned" },
            alsoRequiredBy: ServerList());

        Assert.Empty(plan.Disable);
        Assert.Equal("Something I pinned", Assert.Single(plan.Pinned).Name);
    }

    //
    // A server-scoped entry protects nothing on a client, because it was never installed there. If
    // the client happens to have it, it is theirs to manage.
    //
    [Fact]
    public void AServerScopedEntryDoesNotProtectAClientSideMod()
    {
        var plan = ModListPlanner.Build(
            MyList(), [Installed("fika-server")], alsoRequiredBy: ServerList());

        Assert.Single(plan.Disable);
        Assert.Equal("fika-server", plan.Disable.Single().Name);
    }

    //
    // The operator's own machine is the exception to the line above: they follow their own list, not
    // a served one, so every entry applies and fika-server is protected like anything else.
    //
    [Fact]
    public void OnTheServersOwnMachineTheServerEntriesDoApply()
    {
        var own = List(ModListOrigin.Local,
            Entry("SAIN", ModListEntryScope.Client),
            Entry("fika-server", ModListEntryScope.Server));

        var plan = ModListPlanner.Build(own, [Installed("SAIN"), Installed("fika-server")]);

        Assert.Empty(plan.Disable);
        Assert.Equal(2, plan.Actions.Count);
    }
}
