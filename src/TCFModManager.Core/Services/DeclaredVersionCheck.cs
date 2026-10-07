using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// OPEN-23 S0: whether an installed dependency meets the version range a dependent's own files ask
// for - what SPT checks for a server mod's ModDependencies and BepInEx for a [BepInDependency]
// minimum, at load time. A miss here is an error (R1: files decide errors, sp-mod listings only ever
// warn). Compared against the dependency's declared version, because that is what the loader reads.
//
public static class DeclaredVersionCheck
{
    // The first range a loaded entry of <paramref name="dependent"/> declares that a loaded entry of
    // <paramref name="dependency"/> on the same side doesn't meet, or null when every declared range
    // is met or nothing is declared. Soft dependencies and unreadable versions never miss.
    public static DeclaredVersionMiss? FindMiss(IEnumerable<InstalledMod> dependent, IEnumerable<InstalledMod> dependency)
    {
        var targets = dependency.Where(d => !d.IsDisabled).ToList();
        if (targets.Count == 0) return null;

        foreach (var mod in dependent.Where(d => !d.IsDisabled))
        {
            foreach (var declared in mod.Dependencies)
            {
                if (declared.IsSoft || string.IsNullOrWhiteSpace(declared.VersionRange)) continue;

                foreach (var target in targets)
                {
                    if (target.Target != mod.Target) continue;
                    if (!ModDependencyGraph.Identifiers(target).Contains(declared.Identifier, StringComparer.OrdinalIgnoreCase)) continue;

                    if (ModVersionMatcher.IsSatisfiedBy(declared.VersionRange, target.Version) == false)
                        return new DeclaredVersionMiss(declared.VersionRange!, target.Version!, mod.Target);
                }
            }
        }

        return null;
    }
}

// A declared range the installed dependency doesn't meet: the range as written and the version found.
public sealed record DeclaredVersionMiss(string Range, string Found, InstalledModTarget Target);
