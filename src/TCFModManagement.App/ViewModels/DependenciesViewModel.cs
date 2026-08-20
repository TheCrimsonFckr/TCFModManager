using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManagement.App.Views;
using TCFModManagement.Core.Models;
using TCFModManagement.Core.Services;
using TCFModManagement.Core.SpModApi;

namespace TCFModManagement.App.ViewModels;

// 
// Resolves the dependency tree of every installed mod that has one, and reports each dependency's
// status against what's actually on disk.
// 
public partial class DependenciesViewModel : ObservableObject
{
    private readonly SpModApiClient _spModApi;

    // How many identifier:version pairs go in one request. The endpoint takes many at once,
    // which is what keeps this to a couple of calls instead of one per installed mod.
    private const int BatchSize = 25;

    public DependenciesViewModel() : this(AppServices.SpModApi)
    {
    }

    public DependenciesViewModel(SpModApiClient spModApi)
    {
        _spModApi = spModApi;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasLoaded;

    public ObservableCollection<DependencyTreeViewModel> Trees { get; } = [];

    // True when nothing installed declares a dependency - distinct from "not loaded yet".
    public bool IsEmpty => HasLoaded && Trees.Count == 0;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        var sptVersion = AppServices.SptEnvironment.InstalledVersion;
        if (string.IsNullOrWhiteSpace(sptVersion))
        {
            StatusMessage = "Couldn't detect your SPT version, which is needed to resolve dependencies.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Scanning installed mods...";
        try
        {
            await AppServices.ModCache.EnsureLoadedAsync();

            var scanned = await Task.Run(() => InstalledModScanner.Scan(installPath));
            var installed = InstalledModCardViewModel.BuildFrom(
                scanned, AppServices.ModCache.AllMods, sptVersion, AppServices.InstallManifest.Load().Mods);

            // Only mods that matched the catalog can be asked about; a hand-installed mod we
            // couldn't identify has no identifier to query with.
            var queryable = installed
                .Where(m => m.ModId is not null)
                .Select(m => (Card: m, Mod: AppServices.ModCache.AllMods.FirstOrDefault(c => c.Id == m.ModId)))
                .Where(x => x.Mod is not null)
                .Select(x => (x.Card, Mod: x.Mod!, Version: ResolveQueryVersion(x.Card, x.Mod!)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Version))
                .ToList();

            if (queryable.Count == 0)
            {
                Trees.Clear();
                HasLoaded = true;
                OnPropertyChanged(nameof(IsEmpty));
                StatusMessage = "None of your installed mods could be matched to sp-mod.com.";
                return;
            }

            StatusMessage = $"Resolving dependencies for {queryable.Count} mod(s)...";

            var installedByModId = installed
                .Where(m => m.ModId is not null)
                .GroupBy(m => m.ModId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var trees = new List<DependencyTreeViewModel>();

            foreach (var batch in Chunk(queryable, BatchSize))
            {
                var pairs = string.Join(",", batch.Select(x => $"{Identifier(x.Mod)}:{x.Version}"));
                AppLog.Debug("Dependencies", $"resolving against SPT {sptVersion}: {pairs}");

                var resolved = await _spModApi.GetModDependenciesAsync(pairs, sptVersion!);

                foreach (var missing in batch.Where(x => !resolved.ContainsKey($"{Identifier(x.Mod)}:{x.Version}")))
                {
                    AppLog.Warn("Dependencies",
                        $"no tree returned for {Identifier(missing.Mod)}:{missing.Version} ({missing.Card.DisplayTitle})");
                }

                foreach (var (card, mod, version) in batch)
                {
                    var key = $"{Identifier(mod)}:{version}";
                    if (!resolved.TryGetValue(key, out var nodes) || nodes.Count == 0) continue;

                    var tree = new DependencyTreeViewModel
                    {
                        ModName = card.DisplayTitle,
                        ModVersion = version!,
                        Mod = mod,
                    };

                    foreach (var row in Flatten(nodes, 0, installedByModId)) tree.Rows.Add(row);

                    tree.Refresh();
                    trees.Add(tree);
                }
            }

            Trees.Clear();
            // Mods needing attention first, so the page opens on the problems.
            foreach (var tree in trees
                         .OrderByDescending(t => t.NeedsAttention)
                         .ThenBy(t => t.ModName, StringComparer.OrdinalIgnoreCase))
            {
                Trees.Add(tree);
            }

            HasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));

            var attention = Trees.Count(t => t.NeedsAttention);
            AppLog.Info("Dependencies",
                $"queried {queryable.Count} mod(s); {Trees.Count} have dependencies, {attention} need attention");
            StatusMessage = Trees.Count == 0
                ? "None of your installed mods declare dependencies."
                : attention == 0
                    ? $"{Trees.Count} mod(s) have dependencies - all satisfied."
                    : $"{Trees.Count} mod(s) have dependencies; {attention} need attention.";
        }
        catch (SpModApiRateLimitedException ex)
        {
            StatusMessage = $"Rate limited by sp-mod.com - try again in {ex.RetryAfter?.TotalSeconds ?? 30:N0}s.";
        }
        catch (SpModApiException ex)
        {
            StatusMessage = $"sp-mod.com error: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Network error: {ex.Message}";
        }
        catch (Exception ex)
        {
            AppLog.Error("Dependencies", "Resolve failed", ex);
            StatusMessage = $"Unexpected error resolving dependencies: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Queues a missing or outdated dependency, behind the same read-the-mod-page gate Browse uses.
    [RelayCommand]
    private void Install(DependencyRow? row)
    {
        if (row?.CatalogMod is null || string.IsNullOrWhiteSpace(row.RequiredVersion)) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        var mod = row.CatalogMod;
        if (!ReadModPageConfirmationWindow.Confirm(mod.Name ?? row.Name, mod.DetailUrl))
        {
            StatusMessage = $"Install cancelled - {row.Name}'s page wasn't confirmed as read.";
            return;
        }

        var version = row.RequiredVersion!;

        // checkDependencies stays on: a dependency can have dependencies of its own.
        AppServices.DownloadQueue.Enqueue(mod, version, installPath, () => ResolveVersionLinkAsync(mod, version));

        row.IsQueued = true;
        StatusMessage = $"Queued {row.Name} {version} - see the Downloads page for progress.";
    }

    // Resolves the full ModVersion (with its download link) for exactly one version string,
    // lazily, so queueing never waits on a network call.
    private async Task<ModVersion?> ResolveVersionLinkAsync(Mod mod, string version)
    {
        var versions = await _spModApi.GetModVersionsAsync(
            mod.Id.ToString(), new ModVersionsQuery { FilterVersion = version, PerPage = 5 });

        return versions.Data.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase))
               ?? versions.Data.FirstOrDefault();
    }

    // The endpoint wants a version that matches a published one exactly. The manifest holds
    // that string verbatim for anything this app installed; otherwise the closest published version
    // to what's on disk is used, since a scanned version often carries an extra ".0".
    private static string? ResolveQueryVersion(InstalledModCardViewModel card, Mod mod)
    {
        var published = mod.Versions ?? [];

        var exact = published.FirstOrDefault(v =>
            string.Equals(v.Version, card.InstalledVersion, StringComparison.OrdinalIgnoreCase));
        if (exact?.Version is not null) return exact.Version;

        var equivalent = published.FirstOrDefault(v =>
            ModVersionComparer.IsUpdateAvailable(card.InstalledVersion, v.Version) == false
            && ModVersionComparer.IsUpdateAvailable(v.Version, card.InstalledVersion) == false);
        if (equivalent?.Version is not null) return equivalent.Version;

        return ModCardViewModel.LatestVersion(mod)?.Version;
    }

    // The endpoint accepts a GUID or a numeric id; GUID is preferred when the mod has one.
    private static string Identifier(Mod mod) =>
        string.IsNullOrWhiteSpace(mod.Guid) ? mod.Id.ToString() : mod.Guid!;

    // Walks a resolved tree depth-first into indented rows, tagging each with its status
    // against what's installed.
    private static IEnumerable<DependencyRow> Flatten(
        IEnumerable<DependencyNode> nodes,
        int depth,
        IReadOnlyDictionary<int, InstalledModCardViewModel> installedByModId)
    {
        foreach (var node in nodes)
        {
            installedByModId.TryGetValue(node.Id, out var installed);

            var required = node.LatestCompatibleVersion?.Version;
            var status = DependencyStatusResolver.Resolve(node, installed?.InstalledVersion, required);

            yield return new DependencyRow
            {
                Name = node.Name ?? node.Guid ?? "(unknown)",
                Depth = depth,
                Status = status,
                InstalledVersion = installed?.InstalledVersion,
                RequiredVersion = required,
                CatalogMod = AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == node.Id),
            };

            foreach (var child in Flatten(node.Dependencies, depth + 1, installedByModId))
                yield return child;
        }
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }
}
