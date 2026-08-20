namespace TCFModManager.Core.Models;

// A mod as returned by /mods and /mod/{modId}. Most fields are nullable since the API's "fields" parameter can request a subset; only "id" is guaranteed.
public sealed class Mod
{
    public int Id { get; set; }
    public int? HubId { get; set; }
    public string? Guid { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Teaser { get; set; }

    // Only present on /mod/{modId} (not in the /mods list).
    public string? Description { get; set; }

    public string? Thumbnail { get; set; }
    public int? Downloads { get; set; }
    public int? FavouritesCount { get; set; }
    public Owner? Owner { get; set; }
    public List<Owner>? AdditionalAuthors { get; set; }
    public List<SourceCodeLink>? SourceCodeLinks { get; set; }
    public string? DetailUrl { get; set; }
    public bool? FikaCompatibility { get; set; }
    public bool? Featured { get; set; }
    public bool? ContainsAds { get; set; }
    public bool? ContainsAiContent { get; set; }
    public string? CustomAiDisclosure { get; set; }
    public bool? ShowsProfileBindingNotice { get; set; }
    public bool? CheatNotice { get; set; }
    public int? CategoryId { get; set; }

    // Present when requested via include=category.
    public ModCategory? Category { get; set; }

    // Present when requested via include=license.
    public License? License { get; set; }

    // The mod's latest versions (present only when requested via include=versions; limited to 6).
    public List<ModVersionSummary>? Versions { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

// The limited version info embedded on a Mod via include=versions.
public sealed class ModVersionSummary
{
    public int Id { get; set; }
    public string? Version { get; set; }
    public string? SptVersionConstraint { get; set; }
    public int? Downloads { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
