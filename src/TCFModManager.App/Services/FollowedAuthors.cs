using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The sp-mod authors the user follows (OPEN-12 A4) - read from settings.json once, written back on
// every change. Followed and unfollowed from the author page, any Installed mod's right-click menu
// and Options; read by Browse's filter and the update watcher's new-mod check (R7, R23).
//
public sealed class FollowedAuthors
{
    private readonly SettingsService _settings = new();
    private List<FollowedAuthor> _all;

    public FollowedAuthors()
    {
        _all = [.. _settings.Load().FollowedAuthors];
    }

    // Raised on the UI thread after anyone was followed or unfollowed.
    public event EventHandler? Changed;

    public IReadOnlyList<FollowedAuthor> All => _all;

    public bool IsFollowing(int? authorId) => authorId is { } id && _all.Any(a => a.Id == id);

    public bool IsByFollowed(Mod mod) => _all.Count > 0 && NewModAnnouncer.AuthorsOf(mod).Any(a => IsFollowing(a.Id));

    public void Set(int authorId, string? name, bool follow)
    {
        if (follow == IsFollowing(authorId)) return;

        if (follow)
            _all = [.. _all, new FollowedAuthor { Id = authorId, Name = name, FollowedAt = DateTimeOffset.Now }];
        else
            _all = [.. _all.Where(a => a.Id != authorId)];

        var settings = _settings.Load();
        settings.FollowedAuthors = [.. _all];
        _settings.Save(settings);

        AppLog.Info("Authors", $"{(follow ? "following" : "stopped following")} {name} ({authorId})");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle(int authorId, string? name) => Set(authorId, name, !IsFollowing(authorId));
}
