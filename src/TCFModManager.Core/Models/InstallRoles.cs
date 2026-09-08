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
    // A player takes client entries. A headless box takes the entries meant for a headless AND the
    // server-scoped ones: it is a full SPT install, and a server-scoped entry is a mod that only
    // ever had a server half, so taking it is an ordinary whole install. Nothing here ever installs
    // part of a mod - the archive that gets staged is always the whole thing.
    //
    // None reads as Player. An install that has never been asked is overwhelmingly someone's own
    // game, and guessing headless would strip the client mods off a machine that wanted them.
    //
    public static ModListEntryScope ScopeFor(InstallRoles roles)
    {
        if (roles == InstallRoles.None) return ModListEntryScope.Client;

        ModListEntryScope scope = 0;

        if (roles.HasFlag(InstallRoles.Player)) scope |= ModListEntryScope.Client;

        if (roles.HasFlag(InstallRoles.Headless))
        {
            scope |= ModListEntryScope.Headless | ModListEntryScope.Server;
        }

        return scope;
    }

    //
    // Nobody plays here and something else does - the case the whole feature exists for, and the
    // one worth naming so the UI does not keep re-deriving it from two booleans.
    //
    public static bool IsDedicatedHeadless(InstallRoles roles) =>
        roles.HasFlag(InstallRoles.Headless) && !roles.HasFlag(InstallRoles.Player);
}
