using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Decides a dependency's status, and how severe it is relative to others.
public static class DependencyStatusResolver
{
    // 
    // Resolves one node's status. <paramref name="installedVersion"/> is null when the dependency
    // isn't on disk. <paramref name="requiredVersion"/> is the node's latest compatible version,
    // which the API leaves null when nothing published fits the installed SPT.
    // 
    public static ModStatus Resolve(DependencyNode node, string? installedVersion, string? requiredVersion, bool installedButDisabled = false)
    {
        // A conflict is about the graph as a whole, so it outranks whatever is on disk.
        if (node.Conflict) return ModStatus.Conflict;

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

        return ModVersionComparer.IsUpdateAvailable(installedVersion, requiredVersion) == true
            ? ModStatus.UpdateAvailable
            : ModStatus.Installed;
    }

    // Sort key for "worst" - lower is more severe. Drives the per-mod header icon.
    public static int Severity(ModStatus status) => status switch
    {
        ModStatus.Conflict => 0,
        ModStatus.NotInstalled => 1,
        ModStatus.Disabled => 2,
        ModStatus.NoCompatibleVersion => 3,
        ModStatus.UpdateAvailable => 4,
        _ => 5,
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
