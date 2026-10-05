using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// The newest published version a mod list entry should get when it names no version, or when the
// version it names is gone: newest by publish date among the ones that fit. Null when versions were
// published but none fits, so the caller can say why rather than install one that won't load.
//
public static class NewestFittingVersion
{
    // A mod: the newest whose SPT constraint this install's SPT satisfies - the same pick Browse and
    // the Installed cards make. With the SPT version unknown, simply the newest.
    public static ModVersion? ForSpt(IEnumerable<ModVersion> versions, string? sptVersion) =>
        Newest(versions, v => v.PublishedAt, v => v.Version)
            .FirstOrDefault(v => string.IsNullOrWhiteSpace(sptVersion)
                || SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, sptVersion) == true);

    // An addon: the newest its parent's version satisfies. A version with no readable constraint
    // isn't ruled out, and with the parent's version unknown, simply the newest.
    public static AddonVersion? ForParent(IEnumerable<AddonVersion> versions, string? parentVersion) =>
        Newest(versions, v => v.PublishedAt, v => v.Version)
            .FirstOrDefault(v => string.IsNullOrWhiteSpace(parentVersion)
                || ModVersionMatcher.IsSatisfiedBy(v.ModVersionConstraint, parentVersion) != false);

    private static IEnumerable<T> Newest<T>(
        IEnumerable<T> versions, Func<T, DateTimeOffset?> published, Func<T, string?> version) =>
        versions
            .OrderByDescending(v => published(v) ?? DateTimeOffset.MinValue)
            .ThenByDescending(v => version(v), Comparer<string?>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0));
}
