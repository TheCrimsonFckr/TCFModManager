using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Decides a dependency's status, and how severe it is relative to others.
public static class DependencyStatusResolver
{
    //
    // Resolves one dependency's status. <paramref name="installedVersion"/> is null when the dependency
    // isn't on disk. <paramref name="requiredVersion"/> is the newest version sp-mod resolves for it,
    // null when nothing published fits the installed SPT.
    //
    // OPEN-23 S0: sp-mod's per-request "conflict" flag and the "too new" read off requiredVersion are
    // gone - the first changed with whichever mods shared a request, the second came from a list sp-mod
    // seems to freeze when the dependent is published. A dependent's own files decide an error
    // (<paramref name="filesMiss"/>); a range sp-mod gives (<paramref name="spModConstraint"/>, from its
    // held-back answer) only ever warns.
    //
    public static ModStatus Resolve(
        string? installedVersion, string? requiredVersion,
        bool installedButDisabled = false, bool installedVersionFromFiles = false,
        DeclaredVersionMiss? filesMiss = null, string? spModConstraint = null)
    {
        // A disabled dependency is on disk but isn't loaded, so nothing depending on it works.
        if (installedButDisabled) return ModStatus.Disabled;

        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            // With no compatible version published there's nothing to install either, which is more
            // useful to say than a plain "missing".
            return string.IsNullOrWhiteSpace(requiredVersion)
                ? ModStatus.NoCompatibleVersion
                : ModStatus.NotInstalled;
        }

        // The loader itself will refuse this pairing.
        if (filesMiss is not null) return ModStatus.Conflict;

        var newer = installedVersionFromFiles
            ? ModVersionComparer.IsUpdateAvailableByNumbers(installedVersion, requiredVersion)
            : ModVersionComparer.IsUpdateAvailable(installedVersion, requiredVersion);

        if (newer == true) return ModStatus.UpdateAvailable;

        // A version read off a DLL isn't kept in step with sp-mod by every author, so sp-mod's range
        // is only held against a version this app installed.
        if (!installedVersionFromFiles && ModVersionMatcher.IsSatisfiedBy(spModConstraint, installedVersion) == false)
            return ModStatus.TooNew;

        return ModStatus.Installed;
    }

    // Sort key for "worst" - lower is more severe. Drives the per-mod header icon.
    public static int Severity(ModStatus status) => status switch
    {
        ModStatus.Conflict => 0,
        ModStatus.NotInstalled => 1,
        ModStatus.Disabled => 2,
        ModStatus.TooNew => 3,
        ModStatus.NoCompatibleVersion => 4,
        ModStatus.UpdateAvailable => 5,
        _ => 6,
    };

    // The most severe status in a set, or Installed when empty.
    public static ModStatus Worst(IEnumerable<ModStatus> statuses)
    {
        var worst = ModStatus.Installed;
        var best = Severity(worst);

        foreach (var status in statuses)
        {
            var severity = Severity(status);
            if (severity >= best) continue;

            best = severity;
            worst = status;
        }

        return worst;
    }
}
