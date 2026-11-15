using System.Net.Http;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Services;

// OPEN-44 R3: each entry's direct sp-mod dependencies, asked once per mod, version and SPT this session.
public sealed class PlacementDependencies
{
    private readonly Dictionary<string, IReadOnlyList<int>> _known = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _asked = new(StringComparer.OrdinalIgnoreCase);

    private static string? Key(ModListEntry entry, string spt) =>
        entry.ModId is { } id && !string.IsNullOrWhiteSpace(entry.Version)
            ? string.Join('|', entry.IsAddon ? 'a' : 'm', id, entry.Version.Trim(), spt)
            : null;

    public IReadOnlyList<int>? Known(ModListEntry entry, string spt) =>
        Key(entry, spt) is { } key && _known.TryGetValue(key, out var ids) ? ids : null;

    public List<ModListEntry> Unasked(IEnumerable<ModListEntry> entries, string spt) =>
        [.. entries.Where(e => Key(e, spt) is { } key && !_asked.Contains(key))];

    // True when anything new came back.
    public async Task<bool> ResolveAsync(IReadOnlyList<ModListEntry> entries, string spt, CancellationToken ct)
    {
        var keys = entries.Select(e => Key(e, spt)).OfType<string>().ToList();
        foreach (var key in keys) _asked.Add(key);

        try
        {
            var answer = await SpModListResolver.DirectDependenciesAsync(entries, spt, new SpModListApi(AppServices.SpModApi), ct);

            foreach (var (entry, ids) in answer)
                if (Key(entry, spt) is { } key) _known[key] = ids;

            AppLog.Info("ModLists", $"placement dependencies: {answer.Count} of {entries.Count} entries answered for SPT {spt}");
            return answer.Count > 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or SpModApiException or TaskCanceledException)
        {
            foreach (var key in keys) _asked.Remove(key);

            if (!ct.IsCancellationRequested) AppLog.Warn("ModLists", $"placement dependencies failed: {ex.Message}");
            return false;
        }
    }
}
