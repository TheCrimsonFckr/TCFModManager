using System.Diagnostics;
using System.Text.RegularExpressions;
using TCFModManager.App.Behaviors;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel
{
    private void ApplyFilter()
    {
        var sw = Stopwatch.StartNew();
        var query = SearchText.Trim();

        // A leading "@" switches the search text to matching against the mod's author(s) instead
        // of name/teaser/slug - e.g. "@Acidphantasm".
        var authorQuery = query.StartsWith('@') ? query[1..].Trim() : null;

        // Kept for GoToPage, so each card can describe what the mod supports on the ticked lines.
        _selectedLines = SptVersionOptions
            .Where(o => o.IsSelected)
            .Select(o => ExtractMajorMinor(o.Label))
            .Where(v => v is not null)
            .Select(v => (v!.Value.Major, v.Value.Minor))
            .ToList();
        var selectedLines = _selectedLines;
        var featured = SelectedFeaturedFilter.Value;
        var titleOnly = SelectedSearchScope.Value == SearchScope.TitleOnly;
        var (publishedFrom, publishedUntil) = DateRangeFilter.Window(SelectedPublishedRange.Value, DateTime.Today, PublishedFrom, PublishedUntil);
        var (updatedFrom, updatedUntil) = DateRangeFilter.Window(SelectedUpdatedRange.Value, DateTime.Today, UpdatedFrom, UpdatedUntil);
        var sptForDates = AppServices.SptEnvironment.InstalledVersion;

        var matched = AppServices.ModCache.AllMods
            // This app's own sp-mod.com listing is hidden here rather than dropped from the cached
            // catalog, which the self-updater still needs to be able to read. It just has no
            // business appearing among the mods this app installs into an SPT folder: it isn't a
            // mod, and installing it from here would drop a second copy of the manager into
            // BepInEx\plugins where SPT would try to load it.
            .Where(m => !string.Equals(m.Id.ToString(), SelfMod.ModId, StringComparison.Ordinal))
            .Where(m => authorQuery is not null
                ? MatchesAuthor(m, authorQuery)
                : query.Length == 0 || Matches(m.Name, query) || Matches(m.Slug, query) || (!titleOnly && Matches(m.Teaser, query)))
            // OPEN-12 F19. Published is when the listing first went up; Updated is when it last had
            // a release, the same date the Last updated sort uses.
            .Where(m => DateRangeFilter.Contains(m.PublishedAt ?? m.CreatedAt, publishedFrom, publishedUntil))
            .Where(m => DateRangeFilter.Contains(UpdatedDate(m, sptForDates), updatedFrom, updatedUntil))
            .Where(m => selectedLines.Count == 0 || MatchesSptVersionFilter(m, selectedLines))
            .Where(m => featured switch
            {
                FeaturedFilter.Only => m.Featured == true,
                FeaturedFilter.Exclude => m.Featured != true,
                _ => true, // Include - no restriction
            })
            .Where(m => SelectedCategory.Title is not { } category
                || string.Equals(m.Category?.Title, category, StringComparison.OrdinalIgnoreCase))
            .Where(m => !IsOn(ModAttributeFilter.FikaCompatible) || m.FikaCompatibility == true)
            .Where(m => !IsOn(ModAttributeFilter.HideAds) || m.ContainsAds != true)
            .Where(m => !IsOn(ModAttributeFilter.HasAddons) || AppServices.Addons.CountFor(m.Id) > 0)
            .Where(m => !IsOn(ModAttributeFilter.HideInstalled) || FindInstalledMatch(m) is null)
            .Where(m => !IsOn(ModAttributeFilter.ByFollowedAuthors) || AppServices.Followed.IsByFollowed(m))
            .Where(m => IsOn(ModAttributeFilter.IgnoredOnly) == AppServices.Ignored.IsIgnored(m.Id))
            // Only mods already known to have dependencies. A mod nobody has looked at yet is not
            // claimed either way, so it drops out of this filter rather than being asserted clean.
            .Where(m => !IsOn(ModAttributeFilter.HasDependencies)
                || DependencyBadgeLoader.KnownHasDependencies(m) == true);

        var installedSptVersion = AppServices.SptEnvironment.InstalledVersion;

        _filtered = SelectedSortOption.Value switch
        {
            ModSortOrder.LastUpdated => matched.OrderByDescending(m => LastUpdatedDate(m, installedSptVersion)).ToList(),
            ModSortOrder.MostDownloaded => matched.OrderByDescending(m => m.Downloads ?? 0).ToList(),
            ModSortOrder.MostFavourited => matched.OrderByDescending(m => m.FavouritesCount ?? 0).ToList(),
            // Endorsements are new enough that only a few dozen mods have any at all and the counts
            // are in the low tens, so the overwhelming majority tie on 0. Downloads break the tie to
            // keep that long tail in a sensible order instead of an arbitrary one.
            ModSortOrder.MostEndorsed => matched
                .OrderByDescending(m => m.EndorsementsCount ?? 0)
                .ThenByDescending(m => m.Downloads ?? 0)
                .ToList(),
            // Newest - by the most recent release that runs on the installed SPT, so a mod that
            // shipped an update today sorts above an older mod that happened to be created more
            // recently, while an update for an SPT line this install isn't on doesn't count as new.
            _ => matched.OrderByDescending(m => NewestReleaseDate(m, installedSptVersion)).ToList(),
        };
        AppLog.Debug("Browse", $"ApplyFilter: filter/sort took {sw.ElapsedMilliseconds}ms over {AppServices.ModCache.AllMods.Count} cached mods, {_filtered.Count} matched");

        // A fresh search always jumps back to page 1 of the new result set - for Endless, its first step.
        TotalPages = IsEndless ? 1 : Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
        _endlessShown = EndlessStep;
        GoToPage(1);

        StatusMessage = _filtered.Count switch
        {
            0 => Strings.Browse_NoMatches,
            _ => Strings.Browse_CountFound(_filtered.Count),
        };

        // Said plainly rather than left for someone to work out from a short list.
        if (IsOn(ModAttributeFilter.HasDependencies))
        {
            StatusMessage = string.Join(
                Strings.Common_SentenceSeparator,
                StatusMessage,
                Strings.Browse_DependencyNote);
        }
    }

    /// <summary>True if any of the mod's cached versions actually runs on one of the selected SPT
    /// release lines. Checking every cached version rather than only the newest is what keeps a mod
    /// whose latest release targets 4.1 visible under a 4.0 filter when it still has a 4.0 release.
    /// A mod with no cached version data is never hidden.</summary>
    /// <summary>The publish date of the version this mod's card represents - the newest cached
    /// version that runs on the installed SPT, or the newest overall when none of them do - falling
    /// back to the mod's own dates when it carries no usable version data. Dating a mod by a release
    /// that doesn't run on this install is what let a 4.1-only update sort above a mod that shipped
    /// a usable update more recently.</summary>
    private static DateTimeOffset NewestReleaseDate(Mod mod, string? installedSptVersion)
    {
        if (ModCardViewModel.PickDisplayVersion(mod, installedSptVersion)?.PublishedAt is { } shown)
            return shown;

        var newest = (mod.Versions ?? [])
            .Select(v => v.PublishedAt)
            .Where(d => d is not null)
            .DefaultIfEmpty(null)
            .Max();

        return newest ?? mod.PublishedAt ?? mod.CreatedAt ?? DateTimeOffset.MinValue;
    }

    /// <summary>The date "Recently updated" sorts by. The Forge's own record date stands while the
    /// mod's newest release is one that runs on the installed SPT - it moves for description and
    /// metadata edits too, which is part of what this sort is for - but when the newest release
    /// targets a line this install isn't on, that date is describing an update that can't be used
    /// here, so the newest usable release's date is used instead.</summary>
    private static DateTimeOffset? UpdatedDate(Mod mod, string? installedSptVersion) =>
        LastUpdatedDate(mod, installedSptVersion) is var when && when != DateTimeOffset.MinValue ? when : null;

    private static DateTimeOffset LastUpdatedDate(Mod mod, string? installedSptVersion)
    {
        var newest = ModCardViewModel.LatestVersion(mod);

        var newestRunsHere = newest is not null
            && SptVersionMatcher.IsSatisfiedBy(newest.SptVersionConstraint, installedSptVersion) == true;

        if (newest is null || newestRunsHere)
            return mod.UpdatedAt ?? NewestReleaseDate(mod, installedSptVersion);

        return NewestReleaseDate(mod, installedSptVersion);
    }

    private static bool MatchesSptVersionFilter(Mod mod, List<(int Major, int Minor)> selectedLines)
    {
        var versions = mod.Versions ?? [];
        if (versions.Count == 0 || selectedLines.Count == 0) return true;

        var readable = versions
            .Select(v => v.SptVersionConstraint)
            .Where(c => SptVersionRange.TryParse(c, out _))
            .ToList();

        // Only when nothing about this mod is readable do we give it the benefit of the doubt.
        // Doing that per-version let one blank constraint pull a mod through every filter.
        if (readable.Count == 0) return true;

        return selectedLines.Any(line =>
            readable.Any(c => SptVersionRange.IntersectsReleaseLine(c, line.Major, line.Minor)));
    }

    /// <summary>Builds the checkable SPT version list from the release lines sp-mod.com publishes,
    /// once per session. Using the published list rather than scraping mod constraints keeps
    /// boundary versions nobody ever shipped (e.g. "4.0.4" from a "~4.0.4" constraint) out of the
    /// dropdown. The option matching the detected install starts checked; the rest start unchecked.</summary>
    private void EnsureSptVersionOptionsBuilt()
    {
        if (_sptVersionOptionsBuilt) return;

        var lines = AppServices.SptCatalog.Lines;
        if (lines.Count == 0) return; // Release list not loaded yet; try again on the next search.

        _sptVersionOptionsBuilt = true;

        var installedVersion = AppServices.SptEnvironment.InstalledVersion;
        var installedMajorMinor = ExtractMajorMinor(installedVersion);

        var majorMinors = lines.Select(l => $"{l.Major}.{l.Minor}").ToList();

        if (installedMajorMinor is not null && !majorMinors.Contains(installedMajorMinor.Value.Label))
            majorMinors.Insert(0, installedMajorMinor.Value.Label);

        //
        // A saved default replaces the pre-ticking entirely rather than adding to it, which is what
        // lets an empty saved list mean "browse every version". Null means nothing was saved, and
        // the app's own behaviour - pre-tick whichever line this install is on - stands.
        //
        var saved = _defaults?.SptVersions;

        foreach (var label in majorMinors)
        {
            var isInstalled = label == installedMajorMinor?.Label;

            // Clear filters puts back isInstalled, the app's own; this is only what the page opens at.
            var isSelected = saved is null ? isInstalled : saved.Contains(label);

            // The installed option uses the exact detected version; every other option uses ".0"
            // as that release line's representative version.
            var value = isInstalled && !string.IsNullOrWhiteSpace(installedVersion) ? installedVersion! : $"{label}.0";
            var option = new SptVersionOption(label, value, isSelected, isInstalled);
            option.PropertyChanged += (_, _) =>
            {
                UpdateSptVersionFilterSummary();
                AutoApplyFilter();
            };
            SptVersionOptions.Add(option);
        }

        UpdateSptVersionFilterSummary();
    }

    // Ticking a box in the Filters panel changes the count on its button.
    private void UpdateAttributeFilterSummary() => OnPropertyChanged(nameof(MoreFiltersLabel));

    //
    // Rebuilt from the catalog rather than hardcoded, so the list is whatever The Forge is
    // currently using. Called after the catalog loads; the current selection is kept if it's still
    // a category that exists.
    //
    private void EnsureCategoryOptionsBuilt()
    {
        if (_categoryOptionsBuilt) return;
        _categoryOptionsBuilt = true;

        var categories = AppServices.ModCache.AllMods
            .Select(m => m.Category?.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories) CategoryOptions.Add(new CategoryFilterItem(category, category));

        // Now that the list exists, the saved default can be resolved against it. Only on the first
        // build, which is the only one there is - a category chosen since must not be overridden.
        if (!_categoryDefaultApplied)
        {
            _categoryDefaultApplied = true;
            SelectedCategory = CategoryOptions.FirstOrDefault(c => c.SameAs(DefaultCategory()))
                ?? CategoryOptions[0];
        }
    }

    private bool _categoryOptionsBuilt;

    // The SPT version pill's X: back to the line this install is on, filtered once.
    private void ResetSptVersions()
    {
        _suppressAutoApplyFilter = true;
        try
        {
            foreach (var option in SptVersionOptions) option.IsSelected = option.IsInstalledLine;
        }
        finally
        {
            _suppressAutoApplyFilter = false;
        }

        UpdateSptVersionFilterSummary();
        if (HasLoadedResults) ApplyFilter();
    }

    private void UpdateSptVersionFilterSummary()
    {
        // The count on Filters and view options, and the pills, follow the ticks.
        OnPropertyChanged(nameof(MoreFiltersLabel));

        // Carries its own "SPT" now: the leading label that used to supply it is gone, and
        // "All versions" on its own doesn't say versions of what.
        var selected = SptVersionOptions.Where(o => o.IsSelected).Select(o => o.Label).ToList();
        SptVersionFilterSummary = selected.Count switch
        {
            0 => Strings.Browse_AllSptVersions,
            <= 3 => Text(
                Strings.Browse_SptVersionsFormat,
                string.Join(Strings.Common_ListSeparator, selected)),
            _ => Text(Strings.Browse_SptVersionCountFormat, selected.Count),
        };
    }

    /// <summary>Pulls the major.minor out of a version or constraint string, ignoring any leading
    /// operator (^, ~, &gt;=, etc.) - e.g. "^3.9.0" and "3.9.4" both yield (3, 9).</summary>
    private static (int Major, int Minor, string Label)? ExtractMajorMinor(string? versionOrConstraint)
    {
        if (string.IsNullOrWhiteSpace(versionOrConstraint)) return null;

        var match = MajorMinorPattern.Match(versionOrConstraint);
        if (!match.Success) return null;

        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        return (major, minor, $"{major}.{minor}");
    }

    [GeneratedRegex(@"(\d+)\.(\d+)")]
    private static partial Regex MajorMinorPattern { get; }

    private static bool Matches(string? haystack, string needle) =>
        haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>Matches a "@name" query against the mod's owner and any additional authors. An
    /// empty query matches everything, same as an empty plain-text query.</summary>
    private static bool MatchesAuthor(Mod mod, string authorQuery) =>
        authorQuery.Length == 0
        || Matches(mod.Owner?.Name, authorQuery)
        || (mod.AdditionalAuthors?.Any(a => Matches(a.Name, authorQuery)) ?? false);
}
