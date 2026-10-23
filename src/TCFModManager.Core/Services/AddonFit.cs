using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Whether an addon version fits the installed version of its parent mod (OPEN-31, Chris 2026-10-17).
// As ModVersionMatcher reads the constraint, except that a constraint the parent's version fails but
// this install's SPT version satisfies counts as a fit: an SPT range typed into the parent box.
// ORBIT - Fika Addon 1.1.0 asks for ORBIT "~4.1.6", ORBIT's own SPT range, with ORBIT at 2.1.1, and
// sp-mod's own page lists it as compatible with ORBIT 2.1.0 and 2.1.1 - so the app agrees with sp-mod.
//
public static class AddonFit
{
    public static bool? Check(string? constraint, string? parentVersion, string? sptVersion) =>
        IsSptRangeInParentBox(constraint, parentVersion, sptVersion)
            ? true
            : ModVersionMatcher.IsSatisfiedBy(constraint, parentVersion);

    //
    // For the places that only take a version that fits (the addon card's update, the sp-mod list
    // import's version update). A requirement that can't be read at all isn't taken there.
    //
    public static bool Accepts(string? constraint, string? parentVersion, string? sptVersion) =>
        Check(constraint, parentVersion, sptVersion) == true;

    //
    // OPEN-32 D9: the newest published release of the parent, newer than the installed one and not
    // ruled out for this SPT, that the addon's requirement accepts. Null when there is none.
    //
    public static ModVersionSummary? ParentUpdateUnlocking(
        string? constraint, string? parentVersion, string? sptVersion, IEnumerable<ModVersionSummary>? parentVersions) =>
        string.IsNullOrWhiteSpace(parentVersion)
            ? null
            : NewestFittingVersion.Newest(parentVersions ?? [], v => v.PublishedAt, v => v.Version)
                .FirstOrDefault(v => ModVersionComparer.IsUpdateAvailable(parentVersion, v.Version) == true
                    && (string.IsNullOrWhiteSpace(sptVersion) || SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, sptVersion) != false)
                    && Accepts(constraint, v.Version, sptVersion));

    public static bool IsSptRangeInParentBox(string? constraint, string? parentVersion, string? sptVersion) =>
        ModVersionMatcher.IsSatisfiedBy(constraint, parentVersion) == false
        && !string.IsNullOrWhiteSpace(sptVersion)
        && SptVersionMatcher.IsSatisfiedBy(constraint, sptVersion) == true;
}
