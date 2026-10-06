namespace TCFModManager.Core.Models;

//
// An sp-mod author the user follows (OPEN-12 A4). Kept in settings.json. The name is only for
// showing in Options while the catalog is still loading; the id is what everything matches on.
//
// FollowedAt is the baseline for new-mod notifications (R7): only a mod published after the author
// was followed is news, so following someone never announces their back catalogue.
//
public sealed class FollowedAuthor
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public DateTimeOffset FollowedAt { get; set; }
}
