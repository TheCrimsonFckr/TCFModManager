using System.Globalization;
using TCFModManager.Core.Models;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.Services;

//
// The network half of an update check (D1-D3): ask The Forge only about what is installed, then
// fetch fresh listings for just the mods it says moved, and for every installed addon.
//
// It decides nothing about what counts as an update. The listings it returns are patched into the
// catalog and addon caches, and the Installed page's own rule (BuildFrom) makes the call - so a
// toast can never claim an update the Installed page doesn't show (D2).
//
public sealed class UpdateCheckService(SpModApiClient api)
{
    //
    // /mods/updates takes every installed mod in one "id:version" list. The spike sent 80 of them in
    // 877 characters; longer lists are split well before any server or proxy would object.
    //
    internal const int MaxModsQueryLength = 1500;

    // The catalog's page size for a filter[id] lookup.
    private const int PageSize = 50;

    //
    // The ids /mods/updates lists under "updates" for <paramref name="installed"/>, on
    // <paramref name="sptVersion"/>. Blocked and incompatible entries are not updates: incompatible
    // is about what is installed not fitting this SPT, which the Installed page already shows.
    //
    public async Task<IReadOnlyList<int>> FindUpdatedModsAsync(
        IReadOnlyList<(int ModId, string Version)> installed, string sptVersion, CancellationToken ct = default)
    {
        var updated = new List<int>();

        foreach (var chunk in ModsQueryChunks(installed))
        {
            var result = await api.GetModUpdatesAsync(chunk, sptVersion, ct).ConfigureAwait(false);
            updated.AddRange(result.Updates
                .Select(u => u.CurrentVersion?.ModId)
                .Where(id => id is not null)
                .Select(id => id!.Value));
        }

        return [.. updated.Distinct()];
    }

    // Fresh listings, with categories and versions, shaped like the catalog cache's own.
    public async Task<IReadOnlyList<Mod>> FetchModsAsync(IEnumerable<int> modIds, CancellationToken ct = default)
    {
        var mods = new List<Mod>();

        foreach (var ids in modIds.Distinct().Chunk(PageSize))
        {
            var page = await api.GetModsAsync(new ModsQuery
            {
                FilterId = string.Join(',', ids.Select(i => i.ToString(CultureInfo.InvariantCulture))),
                Include = "category,versions",
                PerPage = PageSize,
            }, ct).ConfigureAwait(false);

            mods.AddRange(page.Data);
        }

        return mods;
    }

    //
    // Addons are not covered by /mods/updates (D3). Few are ever installed, so each check simply
    // re-reads their listings, versions included, a page at a time.
    //
    public async Task<IReadOnlyList<Addon>> FetchAddonsAsync(IEnumerable<int> addonIds, CancellationToken ct = default)
    {
        var addons = new List<Addon>();

        foreach (var ids in addonIds.Distinct().Chunk(PageSize))
        {
            var page = await api.GetAddonsAsync(new AddonsQuery
            {
                FilterId = string.Join(',', ids.Select(i => i.ToString(CultureInfo.InvariantCulture))),
                Include = "versions",
                PerPage = PageSize,
            }, ct).ConfigureAwait(false);

            addons.AddRange(page.Data);
        }

        return addons;
    }

    // "id:version,id:version..." lists no longer than MaxModsQueryLength, in the order given.
    internal static IEnumerable<string> ModsQueryChunks(IEnumerable<(int ModId, string Version)> installed)
    {
        var current = new List<string>();
        var length = 0;

        foreach (var (modId, version) in installed)
        {
            if (string.IsNullOrWhiteSpace(version)) continue;

            var item = $"{modId.ToString(CultureInfo.InvariantCulture)}:{version.Trim()}";
            var added = item.Length + (current.Count > 0 ? 1 : 0);

            if (current.Count > 0 && length + added > MaxModsQueryLength)
            {
                yield return string.Join(',', current);
                current.Clear();
                length = 0;
                added = item.Length;
            }

            current.Add(item);
            length += added;
        }

        if (current.Count > 0) yield return string.Join(',', current);
    }
}
