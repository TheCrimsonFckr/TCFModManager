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
    public static bool? Check(string? constraint, string? parentVersion, string? sptVersion)
    {
        var fit = ModVersionMatcher.IsSatisfiedBy(constraint, parentVersion);

        return fit == false
            && !string.IsNullOrWhiteSpace(sptVersion)
            && SptVersionMatcher.IsSatisfiedBy(constraint, sptVersion) == true
                ? null
                : fit;
    }
}
