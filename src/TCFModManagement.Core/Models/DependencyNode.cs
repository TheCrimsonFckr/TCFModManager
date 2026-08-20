namespace TCFModManagement.Core.Models;

// One node in a resolved dependency tree, as returned by GET /mods/dependencies and GET /addons/dependencies.
public sealed class DependencyNode
{
    public int Id { get; set; }
    public string? Guid { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }

    // Null when no published version satisfies both the constraint and the target SPT version.
    public DependencyVersionRef? LatestCompatibleVersion { get; set; }

    // True when different queried mods/addons require incompatible versions of this dependency.
    public bool Conflict { get; set; }

    public List<DependencyNode> Dependencies { get; set; } = [];
}

public sealed class DependencyVersionRef
{
    public int Id { get; set; }
    public string? Version { get; set; }
    public string? Link { get; set; }
    public long? ContentLength { get; set; }
    public string? FikaCompatibility { get; set; }
}
