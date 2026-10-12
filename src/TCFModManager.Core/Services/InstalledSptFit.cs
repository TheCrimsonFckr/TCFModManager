using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Where an answer about an installed version's SPT came from, strongest first (OPEN-13 §2, R2).
public enum SptFitSource
{
    // sp-mod's update check about exactly this installed version.
    SpMod,

    // The installed release's SPT range in the cached catalog.
    Catalog,

    // The range an SPT 4.x server mod declares in its own DLL.
    Files,
}

//
// Whether the installed version of a mod is made for the installed SPT. ForConstraint is the
// installed release's own SPT range when known. FittingVersion is the newest published release that
// is for this SPT (null when none is); FittingIsNewer says whether it is an update or an older release.
// DeclaredDisagrees: sp-mod or the catalog decided, and the mod's own files say the opposite (R2 -
// sp-mod wins, both shown).
//
public sealed record SptFit(
    bool Fits,
    SptFitSource Source,
    string? ForConstraint,
    string? FittingVersion,
    bool FittingIsNewer,
    string? DeclaredConstraint,
    bool DeclaredDisagrees);

//
// OPEN-13: judges an installed mod's version against the installed SPT. First source that answers
// wins: sp-mod's "not for this SPT", the catalog release's range, sp-mod's "up to date", then the
// range a 4.x server mod declares. Null when nothing answers - say nothing.
//
public static class InstalledSptFit
{
    // spMod: true when sp-mod lists the installed version as not for this SPT, false when it lists it
    // up to date, null when it said neither or wasn't asked.
    public static SptFit? Judge(
        string? installedVersion,
        string? sptVersion,
        IReadOnlyList<ModVersionSummary>? published,
        bool? spMod,
        string? declaredConstraint)
    {
        if (string.IsNullOrWhiteSpace(sptVersion)) return null;

        var installed = Release(installedVersion, published);
        var catalogFits = SptVersionMatcher.IsSatisfiedBy(installed?.SptVersionConstraint, sptVersion);
        var declaredFits = SptVersionMatcher.IsSatisfiedBy(declaredConstraint, sptVersion);

        (bool Fits, SptFitSource Source)? verdict =
            spMod == true ? (false, SptFitSource.SpMod)
            : catalogFits is { } c ? (c, SptFitSource.Catalog)
            : spMod == false ? (true, SptFitSource.SpMod)
            : declaredFits is { } d ? (d, SptFitSource.Files)
            : null;

        if (verdict is not { } v) return null;

        var fitting = (published ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Version) && SptVersionMatcher.IsSatisfiedBy(p.SptVersionConstraint, sptVersion) == true)
            .OrderByDescending(p => p.Version, Comparer<string?>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0))
            .FirstOrDefault()?.Version;

        return new SptFit(
            v.Fits,
            v.Source,
            installed?.SptVersionConstraint,
            fitting,
            fitting is not null && ModVersionComparer.IsUpdateAvailable(installedVersion, fitting) == true,
            declaredConstraint,
            v.Source != SptFitSource.Files && declaredFits is { } df && df != v.Fits);
    }

    // The published release that is the installed version - exactly, or the same numbers ("1.2.0"
    // installed as "1.2.0.0").
    public static ModVersionSummary? Release(string? installedVersion, IReadOnlyList<ModVersionSummary>? published)
    {
        if (string.IsNullOrWhiteSpace(installedVersion) || published is null) return null;

        return published.FirstOrDefault(p => string.Equals(p.Version, installedVersion, StringComparison.OrdinalIgnoreCase))
            ?? published.FirstOrDefault(p => ModVersionComparer.SameNumbers(installedVersion, p.Version));
    }
}
