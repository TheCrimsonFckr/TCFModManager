using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// A mod one of the followed authors has published, for the notification (OPEN-12 A4).
public sealed record NewModCandidate(int ModId, string Name, int AuthorId, string? AuthorName);

//
// OPEN-12 A4, R7: a followed author's new mods raise a notification the way an update does. The
// update watcher fetches the newest listings on sp-mod and hands them here; this picks the ones that
// are news - by a followed author (owner or co-author), published since that author was followed,
// and not announced before. Pure, so it is tested without a timer.
//
public static class NewModAnnouncer
{
    // How many announced ids are remembered; the oldest go first. Far more than one check can see.
    public const int Remembered = 500;

    public static (IReadOnlyList<NewModCandidate> New, List<int> Announced) Pick(
        IEnumerable<Mod> recent,
        IReadOnlyList<FollowedAuthor> followed,
        IReadOnlyList<int> announced)
    {
        var seen = new HashSet<int>(announced);
        var kept = new List<int>(announced);
        var fresh = new List<NewModCandidate>();

        if (followed.Count == 0) return (fresh, kept);

        var byId = followed.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var mod in recent.DistinctBy(m => m.Id).OrderBy(m => m.PublishedAt ?? m.CreatedAt))
        {
            if (seen.Contains(mod.Id)) continue;

            var published = mod.PublishedAt ?? mod.CreatedAt;
            if (published is null) continue;

            var author = AuthorsOf(mod)
                .Select(a => byId.TryGetValue(a.Id, out var f) ? (Owner: a, Follow: f) : default)
                .FirstOrDefault(x => x.Follow is not null && published >= x.Follow.FollowedAt);
            if (author.Follow is null) continue;

            fresh.Add(new NewModCandidate(mod.Id, mod.Name ?? string.Empty, author.Owner.Id, author.Owner.Name ?? author.Follow.Name));
            seen.Add(mod.Id);
            kept.Add(mod.Id);
        }

        if (kept.Count > Remembered) kept.RemoveRange(0, kept.Count - Remembered);
        return (fresh, kept);
    }

    // The owner, then any co-authors.
    public static IEnumerable<Owner> AuthorsOf(Mod mod)
    {
        if (mod.Owner is { } owner) yield return owner;
        foreach (var other in mod.AdditionalAuthors ?? []) yield return other;
    }

    public static bool IsBy(Mod mod, int authorId) => AuthorsOf(mod).Any(a => a.Id == authorId);

    public static bool IsBy(Addon addon, int authorId) =>
        addon.Owner?.Id == authorId || (addon.AdditionalAuthors ?? []).Any(a => a.Id == authorId);
}
