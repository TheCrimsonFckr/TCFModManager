namespace TCFModManager.Core.Models;

// A sub-mod attached to a parent Mod; versions are constrained against the parent mod's version, not an SPT version.
public sealed class Addon
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Teaser { get; set; }
    public string? Description { get; set; }
    public string? Thumbnail { get; set; }
    public int? Downloads { get; set; }
    public Owner? Owner { get; set; }
    public List<Owner>? AdditionalAuthors { get; set; }
    public List<SourceCodeLink>? SourceCodeLinks { get; set; }
    public string? DetailUrl { get; set; }
    public bool? ContainsAds { get; set; }
    public bool? ContainsAiContent { get; set; }
    public string? CustomAiDisclosure { get; set; }
    public int? ModId { get; set; }
    public bool? IsDetached { get; set; }

    // Present when requested via include=license.
    public License? License { get; set; }

    // Present when requested via include=mod - the parent mod.
    public Mod? Mod { get; set; }

    // The addon's latest versions (present only when requested via include=versions; limited to 6).
    public List<AddonVersionSummary>? Versions { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class AddonVersionSummary
{
    public int Id { get; set; }
    public string? Version { get; set; }
    public string? ModVersionConstraint { get; set; }
    public int? Downloads { get; set; }

    // The embedded version objects are fuller than the name suggests: include=versions on /addons
    // carries the changelog, download link and archive size too, so an addon can be installed
    // straight from the cached catalog without a per-addon version lookup.
    public string? Description { get; set; }
    public string? Link { get; set; }
    public long? ContentLength { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
}

// A full addon version as returned by /addon/{addonId}/versions.
public sealed class AddonVersion
{
    public int Id { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? Link { get; set; }
    public long? ContentLength { get; set; }
    public string? ModVersionConstraint { get; set; }
    public int? Downloads { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
