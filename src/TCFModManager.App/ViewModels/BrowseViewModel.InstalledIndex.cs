using TCFModManager.App.Services;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel
{
    // Which RefreshInstalledIndexAsync is the newest - see there.
    private int _installedIndexGeneration;

    /// <summary>Scans the configured SPT install folder and matches it against the cached catalog to drive the
    /// install/update status dot on Browse's cards. Best-effort: no install path or nothing found just means
    /// no dot shows, not an error.</summary>
    // The filter runs again over the refreshed index, on the same page.
    private async Task RefreshAfterUpdateCheckAsync()
    {
        await RefreshInstalledIndexAsync();
        if (!HasLoadedResults) return;

        var page = CurrentPage;
        ApplyFilter();
        if (page > 1) GoToPage(page);
    }

    private async Task RefreshInstalledIndexAsync()
    {
        //
        // Every install in a run fires one of these, and each scans off the UI thread, so they can
        // finish out of order. Only the newest one started is kept: an older scan landing last
        // would put back marks from before the latest install.
        //
        var generation = ++_installedIndexGeneration;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _installedByGuid = new(StringComparer.OrdinalIgnoreCase);
            _installedByName = new(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var catalog = AppServices.ModCache.AllMods;
        var addons = AppServices.Addons.AllAddons;
        var sptVersion = AppServices.SptEnvironment.InstalledVersion;
        var records = AppServices.InstallManifest.Load().Mods;

        // Scan and match together off the UI thread. The match is the slow half - InstalledViewModel
        // has run it this way since the matching rewrite, and doing it inline here was the last
        // thing left holding the window blank while Browse loaded.
        var matched = await Task.Run(() =>
        {
            var scanned = InstalledScan.Run(installPath, catalog, records);

            // The manifest is passed even though Browse ignores IsAppManaged: it's what lets an
            // app-installed mod resolve to its exact catalog listing rather than a folder-name guess.
            return InstalledModCardViewModel.BuildFrom(scanned, catalog, sptVersion, records, addons)
                // Browse's cards are mods, so addon cards are left out of both indexes below - an
                // addon sharing a mod's name would otherwise mark that mod as installed.
                .Where(m => !m.IsAddon)
                .ToList();
        });

        if (generation != _installedIndexGeneration) return;

        // Keyed by Guid when available, MatchedModName as a fallback. Only matched entries are indexed.
        _installedByGuid = matched
            .Where(m => !string.IsNullOrWhiteSpace(m.Guid))
            .GroupBy(m => m.Guid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        _installedByName = matched
            .Where(m => !string.IsNullOrWhiteSpace(m.MatchedModName))
            .GroupBy(m => m.MatchedModName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    //
    // After the installed index changes. With Hide installed ticked the result set itself has
    // changed - a mod just installed has to leave it - so the filter runs again, on the same page
    // where there still is one.
    //
    private void ShowInstalledChange()
    {
        if (!IsOn(ModAttributeFilter.HideInstalled) || !HasLoadedResults)
        {
            GoToPage(CurrentPage);
            return;
        }

        var page = CurrentPage;
        ApplyFilter();
        if (page > 1) GoToPage(page);
    }

    private void ShowIgnoredChange()
    {
        if (!HasLoadedResults) return;

        var page = CurrentPage;
        ApplyFilter();
        if (page > 1) GoToPage(page);
    }

    // Re-reads the pins for the cards on screen, for a pin made on another page since.
    public void RefreshPins()
    {
        var pins = AppServices.ModLists.GetPins();
        foreach (var card in Results) card.RefreshPin(pins);
    }

    // Ticks "By authors you follow" (the notification's Open); the tick box re-filters as usual.
    private void ShowFollowed()
    {
        foreach (var option in AttributeOptions)
            if (option.Value == ModAttributeFilter.ByFollowedAuthors) option.IsSelected = true;
    }

    // Whether this listing is installed, as far as the last scan Browse made knows (OPEN-12 A4).
    public bool IsInstalled(Mod mod) => FindInstalledMatch(mod) is not null;

    private InstalledModCardViewModel? FindInstalledMatch(Mod mod)
    {
        if (!string.IsNullOrWhiteSpace(mod.Guid) && _installedByGuid.TryGetValue(mod.Guid, out var byGuid))
            return byGuid;

        if (!string.IsNullOrWhiteSpace(mod.Name) && _installedByName.TryGetValue(mod.Name, out var byName))
            return byName;

        return null;
    }
}
