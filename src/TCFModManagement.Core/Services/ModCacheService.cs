using TCFModManagement.Core.SpModApi;
using TCFModManagement.Core.Models;

namespace TCFModManagement.Core.Services;

// 
// Fetches the entire sp-mod.com catalog page by page. Caching/reuse of the result is the caller's
// responsibility; this just performs the paginated fetch.
// 
public sealed class ModCacheService(SpModApiClient spModApi)
{
    private const int PageSize = 50; // sp-mod.com API's per_page max.

    public async Task<List<Mod>> FetchAllAsync(
        IProgress<(int Loaded, int? Total)>? progress = null,
        CancellationToken ct = default)
    {
        var all = new List<Mod>();
        var page = 1;
        var dropped = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            PagedResult<Mod> result;
            try
            {
                result = await spModApi.GetModsAsync(
                    new ModsQuery
                    {
                        // "versions" ensures each cached mod carries its latest version's SPT
                        // constraint/release number, used by Browse cards and the version filter.
                        Include = "category,versions",
                        Sort = "-downloads",
                        Page = page,
                        PerPage = PageSize,
                    },
                    ct).ConfigureAwait(false);
            }
            catch (SpModApiRateLimitedException ex)
            {
                // Back off as long as the API asks, then retry the same page.
                await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }

            var kept = result.Data.Where(StillRunsOnASupportedRelease).ToList();
            dropped += result.Data.Count - kept.Count;
            all.AddRange(kept);
            progress?.Report((all.Count, result.Meta?.Total));

            var isLastPage = result.Data.Count < PageSize
                || (result.Meta is { } meta && meta.LastPage > 0 && page >= meta.LastPage);
            if (isLastPage) break;

            page++;

            // Small pause between pages to stay under the API's rate limit.
            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        AppLog.Info("Catalog", $"fetched {all.Count} mod(s); dropped {dropped} below SPT {SptReleases.Floor}");
        return all;
    }

    // 
    // Whether any of a mod's cached versions targets an SPT release at or above
    // <see cref="SptReleases.Floor"/>. Mods that only ever supported older releases are left out of
    // the catalog: nobody can install them, and they only pad out search and the version filter.
    // 
    private static bool StillRunsOnASupportedRelease(Mod mod)
    {
        var versions = mod.Versions ?? [];
        if (versions.Count == 0) return true;

        return SptReleases.ReachesFloor(versions.Select(v => v.SptVersionConstraint));
    }
}
