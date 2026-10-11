namespace TCFModManager.Core.Models;

//
// Named filter presets on Browse and Installed (OPEN-25). Separate from the page defaults (R2): a
// preset is an extra set the user switches to, and nothing about one changes what the page opens at.
// Kept in settings.json per page (R4), local only.
//
// Filters is the page's own defaults record, so a filter added to the page later is added once and
// both Save as default and presets carry it. On top of it a preset holds the search text and which
// filter sections are pinned (R3, R8), and on Browse the days of a custom Published / Updated range,
// which Save as default deliberately drops (R5).
//
public interface IFilterPreset
{
    Guid Id { get; }

    string Name { get; set; }

    // What was in the search box when it was saved - "" when it was empty. Null only in a
    // hand-edited file: the preset then leaves the search box alone.
    string? Search { get; set; }

    // The pinned section names, as AppSettings keeps them. Null only in a hand-edited file: the
    // preset then leaves the pins alone.
    List<string>? Pinned { get; set; }
}

public sealed class BrowseFilterPreset : IFilterPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public BrowsePageDefaults Filters { get; set; } = new();

    // The days of a Custom range (R5); null unless Filters.Published / Updated is Custom.
    public DateTime? PublishedFrom { get; set; }

    public DateTime? PublishedUntil { get; set; }

    public DateTime? UpdatedFrom { get; set; }

    public DateTime? UpdatedUntil { get; set; }

    public string? Search { get; set; }

    public List<string>? Pinned { get; set; }
}

public sealed class InstalledFilterPreset : IFilterPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    // Includes the view mode - Cards, Groups or List (R3).
    public InstalledPageDefaults Filters { get; set; } = new();

    public string? Search { get; set; }

    public List<string>? Pinned { get; set; }
}
