namespace TCFModManager.Core.Services;

// 
// Answers whether an installed mod's version satisfies a constraint written against that mod -
// sp-mod.com's mod_version_constraint, which every addon version carries in place of an SPT one.
// 
// Same syntax as an SPT constraint and the same parser, but read literally: SptVersionMatcher
// deliberately ignores a constraint's patch-level floor because SPT itself doesn't break mod
// compatibility within a release line, and that reasoning does not carry over to one mod's API
// against another. An addon asking for "^1.5.3" of its parent means 1.5.3.
// 
public static class ModVersionMatcher
{
    // 
    // True if <paramref name="modVersion"/> satisfies every clause in <paramref name="constraint"/>.
    // Returns null when either input is missing or unparsable - for an addon that means "the parent
    // isn't installed, or its version can't be read", which is a different answer from "doesn't fit".
    // 
    public static bool? IsSatisfiedBy(string? constraint, string? modVersion)
    {
        if (string.IsNullOrWhiteSpace(constraint) || string.IsNullOrWhiteSpace(modVersion)) return null;

        var version = SptVersionMatcher.ParseVersion(modVersion);
        if (version is null) return null;

        return SptVersionRange.TryParse(constraint, out var bounds) ? bounds.Allows(version) : null;
    }
}
