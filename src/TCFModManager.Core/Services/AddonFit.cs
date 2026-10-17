namespace TCFModManager.Core.Services;

//
// Whether an addon version fits the installed version of its parent mod (OPEN-31, Chris 2026-10-17).
// True or false as ModVersionMatcher reads the constraint, except one case read as "couldn't check"
// (null): a constraint the parent's version fails but this install's SPT version satisfies. That is
// an SPT range typed into the parent box - ORBIT - Fika Addon 1.1.0 asks for ORBIT "~4.1.6", ORBIT's
// own SPT range, with ORBIT at 2.1.1 - and like any constraint that can't be read it is let through
// with a note rather than blocked.
//
public static class AddonFit
{
    public static bool? Check(string? constraint, string? parentVersion, string? sptVersion) =>
        IsSptRangeInParentBox(constraint, parentVersion, sptVersion)
            ? null
            : ModVersionMatcher.IsSatisfiedBy(constraint, parentVersion);

    //
    // For the places that only take a version that fits (the addon card's update, the sp-mod list
    // import's version update): a requirement the parent satisfies, or an SPT range in the parent box.
    // A requirement that can't be read at all still isn't taken there.
    //
    public static bool Accepts(string? constraint, string? parentVersion, string? sptVersion) =>
        ModVersionMatcher.IsSatisfiedBy(constraint, parentVersion) == true
        || IsSptRangeInParentBox(constraint, parentVersion, sptVersion);

    public static bool IsSptRangeInParentBox(string? constraint, string? parentVersion, string? sptVersion) =>
        ModVersionMatcher.IsSatisfiedBy(constraint, parentVersion) == false
        && !string.IsNullOrWhiteSpace(sptVersion)
        && SptVersionMatcher.IsSatisfiedBy(constraint, sptVersion) == true;
}
