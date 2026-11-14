namespace TCFModManager.Core.Models;

//
// What this machine does with the install it manages.
//
// A set rather than a mode, because "dedicated headless" is not a third kind of thing - it is a
// machine that hosts and that nobody plays on, and the machine that does both is ordinary enough
// that it should not need a fourth value inventing for it. Dedicated is Headless without Player.
//
// Not derived from the folder, and deliberately so. Every SPT install has SPT.Server.exe, a
// headless one included, so the folder cannot say whether anyone sits at this machine. The headless
// launcher answers half of it - the machine can host - and only the person setting the app up knows
// the other half. See SptLaunchService.TryFindHeadlessLauncherExe for the detected half and
// AppSettings for the answered one.
//
[Flags]
public enum InstallRoles
{
    None = 0,

    // Someone plays the game on this machine.
    Player = 1,

    // This machine runs a Fika headless client, hosting raids for other people.
    Headless = 2,
}

public static class InstallRole
{
    //
    // The entry scopes a served list should be filtered to for a machine in these roles.
    //
    // A player takes client entries and a headless box takes headless ones. A headless ALSO takes
    // an entry scoped to the server ALONE - it is a full SPT install and a server-only entry is a
    // whole mod rather than half of one - but that is a rule about the entry, not a flag this
    // machine carries: see ModList.ScopeApplies.
    //
    // It used to be a flag here, and that is what made "Server + Client" impossible to say. An
    // entry naming the server for any reason reached the headless, so an author who deliberately
    // left the headless out of a mod the server and the players both need had no way to be heard.
    //
    // None reads as Player. An install that has never been asked is overwhelmingly someone's own
    // game, and guessing headless would strip the client mods off a machine that wanted them.
    //
    // Player + Headless also reads as Player (OPEN-44 Q12). A Fika headless is a copy of the install
    // in its own folder, so one install is never both; both switches on is a misconfiguration the
    // Options page points out, and the safe reading of it is the one that keeps the player's mods.
    //
    public static ModListEntryScope ScopeFor(InstallRoles roles) =>
        roles.HasFlag(InstallRoles.Headless) && !roles.HasFlag(InstallRoles.Player)
            ? ModListEntryScope.Headless
            : ModListEntryScope.Client;

    //
    // Nobody plays here and something else does - the case the whole feature exists for, and the
    // one worth naming so the UI does not keep re-deriving it from two booleans.
    //
    public static bool IsDedicatedHeadless(InstallRoles roles) =>
        roles.HasFlag(InstallRoles.Headless) && !roles.HasFlag(InstallRoles.Player);

    // The same question asked of a machine scope: a headless with no player.
    public static bool IsDedicatedHeadless(ModListEntryScope machine) =>
        machine.HasFlag(ModListEntryScope.Headless) && !machine.HasFlag(ModListEntryScope.Client);
}
