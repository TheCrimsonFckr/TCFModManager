using TCFModManager.Core.Models;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.Services;

//
// Checks sp-mod.com for a newer release of this app.
//
// This reads the app's own public mod listing (SelfMod.ModId) through the same read-only
// /api/v0 endpoints Browse uses for every other mod. There is no private channel, no separate
// release feed and no direct-from-GitHub fetch: an update exists here exactly when a version has
// been published on sp-mod.com, and the file it downloads is the one that listing's own download
// link resolves to.
//
public sealed class AppUpdateService(SpModApiClient spModApi)
{
    // Plenty for a listing that has published five versions in its lifetime, and small enough that
    // this stays one page. Newest-first ordering is requested as well, so even a listing that
    // outgrows this still has its newest release on the page that gets fetched.
    private const int VersionsToInspect = 25;

    //
    // Returns the newest published version of this app alongside the running one, or null if the
    // listing carries no parsable version at all. A result whose IsUpdate is false means the check
    // ran fine and there's nothing newer - callers distinguish that from a failed check, which
    // surfaces as an exception.
    //
    public async Task<AppUpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var currentVersion = AppVersion.Current;

        // /mod/{id}/versions rather than /mod/{id}?include=versions: the embedded summaries the
        // latter returns are mapped to ModVersionSummary, which has no download link, changelog or
        // content length on it. All three are needed here.
        var versions = await spModApi
            .GetModVersionsAsync(
                SelfMod.ModId,
                new ModVersionsQuery { Sort = "-published_at", PerPage = VersionsToInspect },
                ct)
            .ConfigureAwait(false);

        // Ordered by version number rather than by publish date, so a corrective re-publish of an
        // older release can't present itself as the newest one.
        var newest = versions.Data
            .Select(v => (Version: v, Parsed: SemanticVersion.TryParse(v.Version, out var p) ? p : null))
            .Where(x => x.Parsed is not null)
            .OrderByDescending(x => x.Parsed!.Value)
            .Select(x => x.Version)
            .FirstOrDefault();

        if (newest is null)
        {
            AppLog.Info("AppUpdate", $"listing {SelfMod.ModId} returned no parsable version; running {currentVersion}");
            return null;
        }

        var changeKind = SemanticVersion.Classify(currentVersion, newest.Version);
        AppLog.Info("AppUpdate", $"running {currentVersion}, newest published {newest.Version} ({changeKind?.ToString() ?? "unknown"})");

        return new AppUpdateInfo
        {
            CurrentVersion = currentVersion,
            LatestVersion = newest.Version ?? "unknown",
            ChangeKind = changeKind,
            DownloadUrl = newest.Link,
            ModPageUrl = await ResolveModPageUrlAsync(ct).ConfigureAwait(false),
            Changelog = newest.Description,
            DownloadSizeBytes = newest.ContentLength,
            PublishedAt = newest.PublishedAt,
        };
    }

    // The mod page the user is sent to before an update installs. Asked for live so a slug change
    // on sp-mod.com doesn't leave the app pointing at a dead link, but a failure here must not fail
    // the whole check - the hardcoded fallback is the same page.
    private async Task<string> ResolveModPageUrlAsync(CancellationToken ct)
    {
        try
        {
            var mod = await spModApi.GetModAsync(SelfMod.ModId, fields: "id,detail_url", ct: ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(mod.DetailUrl) ? SelfMod.ModPageUrl : mod.DetailUrl;
        }
        catch (Exception ex) when (ex is SpModApiException or HttpRequestException or OperationCanceledException)
        {
            AppLog.Debug("AppUpdate", $"couldn't resolve the live mod page URL ({ex.Message}); using the built-in one");
            return SelfMod.ModPageUrl;
        }
    }
}
