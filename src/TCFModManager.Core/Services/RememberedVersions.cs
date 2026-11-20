using System.Globalization;
using TCFModManager.Core.Models;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.Services;

public static class RememberedVersions
{
    // The remembered version as sp-mod has it now: by id once it was resolved, else by its exact version string. Null when it is gone.
    public static async Task<ModVersion?> ResolveAsync(SpModApiClient api, RememberedDownload download, CancellationToken ct = default)
    {
        var id = download.Target.Id.ToString(CultureInfo.InvariantCulture);
        var versionId = download.VersionId?.ToString(CultureInfo.InvariantCulture);

        bool Matches(int candidateId, string? candidateVersion) => download.VersionId is { } wanted
            ? candidateId == wanted
            : string.Equals(candidateVersion, download.VersionLabel, StringComparison.Ordinal);

        if (download.Target.IsAddon)
        {
            var addons = await api.GetAddonVersionsAsync(id, versionId is null
                ? new AddonVersionsQuery { FilterVersion = download.VersionLabel }
                : new AddonVersionsQuery { FilterId = versionId }, ct).ConfigureAwait(false);

            return addons.Data.FirstOrDefault(v => Matches(v.Id, v.Version)) is { } addon
                ? new ModVersion
                {
                    Id = addon.Id,
                    Version = addon.Version,
                    Description = addon.Description,
                    Link = addon.Link,
                    ContentLength = addon.ContentLength,
                }
                : null;
        }

        var mods = await api.GetModVersionsAsync(id, versionId is null
            ? new ModVersionsQuery { FilterVersion = download.VersionLabel }
            : new ModVersionsQuery { FilterId = versionId }, ct).ConfigureAwait(false);

        return mods.Data.FirstOrDefault(v => Matches(v.Id, v.Version));
    }
}
