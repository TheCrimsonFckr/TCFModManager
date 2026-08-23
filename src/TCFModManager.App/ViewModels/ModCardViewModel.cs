using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// Display wrapper around a Mod for the Browse results grid. Precomputes the fields of the version this card represents.
public sealed class ModCardViewModel
{
    public required Mod Mod { get; init; }

    public string? Name => Mod.Name;
    public string? Guid => Mod.Guid;
    public string? Teaser => Mod.Teaser;
    public string? Thumbnail => Mod.Thumbnail;
    public int? Downloads => Mod.Downloads;

    // The mod's primary owner/author.
    public string? Author => Mod.Owner?.Name;

    // The mod's category title, shown as a tag chip.
    public string? CategoryTag => Mod.Category?.Title;

    public bool IsFikaCompatible => Mod.FikaCompatibility == true;
    public bool ContainsAds => Mod.ContainsAds == true;

    // The raw SPT version constraint of the version this card represents, e.g. "^3.9.0". Shown in the tooltip.
    public string? SptVersionConstraint { get; private init; }

    // The card's SPT line, e.g. "✓ SPT 4.0.13 - 4.0.x". Falls back to the raw constraint when it can't be parsed.
    public string SptVersionDisplay { get; private init; } = "SPT version unknown";

    // Tooltip spelling out the requirement, the installed version, and the raw constraint.
    public string SptVersionTooltip { get; private init; } = "";

    // The release number of the version this card represents, e.g. "1.4.2".
    public string? DisplayReleaseVersion { get; private init; }

    // True if the version this card represents runs on the installed SPT, false if not, null if unknown.
    public bool? IsCompatibleWithInstalledSpt { get; private init; }

    // True when the card is showing an older release because the newest one doesn't run on the installed SPT.
    public bool IsOlderCompatibleVersion { get; private init; }

    // True when this catalog mod matched an installed mod. Drives whether the card shows an install/update status dot.
    public bool IsInstalled { get; private init; }

    // The card's install status, using the same vocabulary and icons as the Installed and
    // Dependencies pages.
    public ModStatus Status => (IsInstalled, UpdateAvailable) switch
    {
        (false, _) => ModStatus.NotInstalled,
        (true, true) => ModStatus.UpdateAvailable,
        (true, false) => ModStatus.Installed,
        _ => ModStatus.Unknown,
    };

    public string StatusGlyph => ModStatusDisplay.Glyph(Status);

    public string StatusTooltip => ModStatusDisplay.Tooltip(Status);

    // Only meaningful when IsInstalled is true. True if a compatible newer version is published, false if up to date, null if unknown.
    public bool? UpdateAvailable { get; private init; }

    // 
    // Builds a card. <paramref name="selectedLines"/> is the SPT release lines currently ticked in
    // Browse's filter; the card's SPT text describes what the mod supports on exactly those lines,
    // so filtering by 4.0 and 4.1 shows both. With none ticked it falls back to describing the
    // version that would be installed.
    // 
    public static ModCardViewModel From(
        Mod mod,
        string? installedSptVersion,
        InstalledModCardViewModel? installedMatch = null,
        IReadOnlyList<(int Major, int Minor)>? selectedLines = null,
        IReadOnlyList<SptRelease>? releases = null)
    {
        var newest = LatestVersion(mod);
        var shown = PickDisplayVersion(mod, installedSptVersion) ?? newest;
        var constraints = (mod.Versions ?? []).Select(v => v.SptVersionConstraint).ToList();

        // The glyph answers "does what's shown here actually run on my SPT". When a filter is
        // ticked, that has to look only at the constraints relevant to the ticked lines - not the
        // mod's whole history - or filtering to 4.0 while running SPT 4.1.1 could show a green
        // check next to "SPT 4.0.13" just because the mod separately publishes an unrelated
        // 4.1-compatible version elsewhere. Chris: "if you're running 4.1 and you have 4.0 filter
        // you should be seeing red because this are incompatible... this applies to them all".
        // With no filter ticked the text describes every line the mod supports, so the glyph stays
        // mod-wide too ("can I install ANY version of this on my SPT").
        var relevantConstraints = selectedLines is { Count: > 0 }
            ? constraints.Where(c => selectedLines.Any(line => SptVersionRange.IntersectsReleaseLine(c, line.Major, line.Minor))).ToList()
            : constraints;

        var compatible = relevantConstraints.Count == 0
            ? null
            : relevantConstraints.Any(c => SptVersionMatcher.IsSatisfiedBy(c, installedSptVersion) == true)
                ? true
                : relevantConstraints.Any(c => SptVersionMatcher.IsSatisfiedBy(c, installedSptVersion) == false)
                    ? false
                    : (bool?)null;

        var isOlder = shown is not null && newest is not null && shown.Id != newest.Id;

        return new ModCardViewModel
        {
            Mod = mod,
            SptVersionConstraint = shown?.SptVersionConstraint,
            SptVersionDisplay = BuildDisplay(constraints, shown?.SptVersionConstraint, selectedLines, releases, compatible),
            SptVersionTooltip = BuildTooltip(shown, newest, installedSptVersion, isOlder, constraints, releases),
            DisplayReleaseVersion = shown?.Version,
            IsCompatibleWithInstalledSpt = compatible,
            IsOlderCompatibleVersion = isOlder,
            IsInstalled = installedMatch is not null,
            UpdateAvailable = installedMatch?.UpdateAvailable,
        };
    }

    // 
    // The newest cached version that runs on <paramref name="installedSptVersion"/>, or the newest
    // version overall when none of them do. Shared with Browse's Install command so the card always
    // describes the version that installing would actually fetch.
    // 
    // Only sees the versions the catalog carries (the API embeds the latest few), so a
    // compatible release older than that window won't be found.
    public static ModVersionSummary? PickDisplayVersion(Mod mod, string? installedSptVersion)
    {
        var candidates = (mod.Versions ?? [])
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();

        return candidates.FirstOrDefault(v => SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, installedSptVersion) == true)
            ?? candidates.FirstOrDefault();
    }

    // Returns the mod's most recently published version.
    public static ModVersionSummary? LatestVersion(Mod mod) => mod.Versions?
        .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
        .FirstOrDefault();

    private static string BuildDisplay(
        List<string?> constraints,
        string? pickedConstraint,
        IReadOnlyList<(int Major, int Minor)>? selectedLines,
        IReadOnlyList<SptRelease>? releases,
        bool? compatible)
    {
        var glyph = compatible switch
        {
            true => "✓ ",
            false => "✗ ",
            _ => "",
        };

        // Name the newest release that actually shipped on each ticked line, rather than the
        // boundary the constraint is written against - "~4.0.4" is 4.0.13 in practice.
        if (releases is { Count: > 0 })
        {
            var lines = selectedLines is { Count: > 0 }
                ? selectedLines
                : SptReleases.Lines(SptReleases.Supported(constraints, releases));

            var named = lines
                .Select(line => SptReleases.NewestSupportedOnLine(constraints, releases, line.Major, line.Minor))
                .Where(release => release is not null)
                .Select(release => release!.Value.Label)
                .ToList();

            if (named.Count > 0) return $"{glyph}SPT {string.Join(", ", named)}";
        }

        // No release list yet (first run, offline) - fall back to describing the constraint itself.
        if (selectedLines is { Count: > 0 })
        {
            var perLine = selectedLines
                .Select(line => SptVersionRange.UnionForLine(constraints, line.Major, line.Minor))
                .Where(bounds => bounds is not null)
                .Select(bounds => SptVersionRangeFormatter.Format(bounds!.Value))
                .Distinct()
                .ToList();

            if (perLine.Count > 0) return $"{glyph}SPT {string.Join(", ", perLine)}";
        }

        if (string.IsNullOrWhiteSpace(pickedConstraint)) return "SPT version unknown";

        return $"{glyph}SPT {SptVersionRangeFormatter.Format(pickedConstraint) ?? pickedConstraint}";
    }

    private static string BuildTooltip(
        ModVersionSummary? shown,
        ModVersionSummary? newest,
        string? installedSptVersion,
        bool isOlder,
        List<string?> constraints,
        IReadOnlyList<SptRelease>? releases)
    {
        if (shown is null) return "This mod has no published version in the catalog.";

        var lines = new List<string>();

        var supported = releases is { Count: > 0 } ? SptReleases.Supported([shown.SptVersionConstraint], releases) : [];
        if (supported.Count > 0)
        {
            lines.Add(supported.Count == 1
                ? $"v{shown.Version} runs on SPT {supported[0].Label}."
                : $"v{shown.Version} runs on SPT {supported[^1].Label} to {supported[0].Label}.");
        }
        else
        {
            var range = SptVersionRangeFormatter.Format(shown.SptVersionConstraint);
            lines.Add(range is null
                ? $"v{shown.Version} has no readable SPT requirement."
                : $"v{shown.Version} needs SPT {range}.");
        }

        lines.Add(string.IsNullOrWhiteSpace(installedSptVersion)
            ? "No SPT install detected - set it on the Options page."
            : $"You have SPT {installedSptVersion}.");

        if (isOlder && newest is not null)
        {
            var newestRange = SptVersionRangeFormatter.Format(newest.SptVersionConstraint) ?? newest.SptVersionConstraint;
            lines.Add($"Showing this instead of the newest v{newest.Version}, which needs SPT {newestRange}.");
        }

        if (!string.IsNullOrWhiteSpace(shown.SptVersionConstraint))
            lines.Add($"Constraint: {shown.SptVersionConstraint}");

        return string.Join(Environment.NewLine, lines);
    }
}
