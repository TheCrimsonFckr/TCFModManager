using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The one way the App scans an install (OPEN-28): InstalledModScanner with the install records and
// the cached sp-mod listings, so a folder several mods share is split the same way on every page.
//
public static class InstalledScan
{
    public static List<InstalledMod> Run(string? installPath) =>
        Run(installPath, AppServices.ModCache.AllMods, AppServices.InstallManifest.Load().Mods);

    public static List<InstalledMod> Run(
        string? installPath, IReadOnlyList<Mod> catalog, IReadOnlyList<InstalledModRecord>? records) =>
        InstalledModScanner.Scan(installPath, Ownership(catalog, records));

    public static ModOwnership Ownership(IReadOnlyList<Mod> catalog, IReadOnlyList<InstalledModRecord>? records) =>
        new(
            records ?? [],
            catalog
                .Where(m => !string.IsNullOrWhiteSpace(m.Guid))
                .Select(m => new KeyValuePair<string, int>(m.Guid!, m.Id)));
}
