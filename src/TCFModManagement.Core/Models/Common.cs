namespace TCFModManagement.Core.Models;

public sealed class Owner
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public string? CoverPhotoUrl { get; set; }
}

public sealed class SourceCodeLink
{
    public string? Url { get; set; }
    public string? Label { get; set; }
}

// A license (id, hub_id, name, link, timestamps).
public sealed class License
{
    public int Id { get; set; }
    public int? HubId { get; set; }
    public string? Name { get; set; }
    public string? Link { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

// A mod category (id, hub_id, title, slug, description).
public sealed class ModCategory
{
    public int Id { get; set; }
    public int? HubId { get; set; }
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
}

public sealed class SptVersion
{
    public int Id { get; set; }
    public string? Version { get; set; }
    public int? VersionMajor { get; set; }
    public int? VersionMinor { get; set; }
    public int? VersionPatch { get; set; }
    public string? VersionLabels { get; set; }
    public int? ModCount { get; set; }
    public string? Link { get; set; }
    public string? ColorClass { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class FileTree
{
    public DateTimeOffset? VerifiedAt { get; set; }
    public int FileCount { get; set; }
    public bool Truncated { get; set; }
    public List<string> Files { get; set; } = [];
}
