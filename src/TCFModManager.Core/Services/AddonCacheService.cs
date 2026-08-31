using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Fetches every addon published on sp-mod.com page by page. Caching/reuse of the result is the
// caller's responsibility; this just performs the paginated fetch.
// 
public sealed class AddonCacheService(SpModApiClient spModApi)
{
    private const int PageSize = 50; // sp-mod.com API's per_page max.

    public async Task<List<Addon>> FetchAllAsync(
        IProgress<(int Loaded, int? Total)>? progress = null,
        CancellationToken ct = default)
    {
        var all = new List<Addon>();
        var page = 1;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            PagedResult<Addon> result;
            try
            {
                result = await spModApi.GetAddonsAsync(
                    new AddonsQuery
                    {
                        // Each addon's latest versions carry the download link, size and the
                        // parent-mod constraint, so the whole install path works off this one fetch.
                        Include = "versions",
                        Sort = "-downloads",
                        Page = page,
                        PerPage = PageSize,
                    },
                    ct).ConfigureAwait(false);
            }
            catch (SpModApiRateLimitedException ex)
            {
                await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }

            all.AddRange(result.Data);
            progress?.Report((all.Count, result.Meta?.Total));

            var isLastPage = result.Data.Count < PageSize
                || (result.Meta is { } meta && meta.LastPage > 0 && page >= meta.LastPage);
            if (isLastPage) break;

            page++;

            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        AppLog.Info("Addons", $"fetched {all.Count} addon(s)");
        return all;
    }
}
