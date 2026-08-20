namespace TCFModManagement.Core.Models;

// Result of GET /mods/updates: a categorized comparison of installed mod versions against what's available for a target SPT version.
public sealed class ModUpdateCheckResult
{
    public string? SptVersion { get; set; }
    public List<ModUpdateEntry> Updates { get; set; } = [];
    public List<ModBlockedUpdateEntry> BlockedUpdates { get; set; } = [];
    public List<ModUpToDateEntry> UpToDate { get; set; } = [];
    public List<ModIncompatibleEntry> IncompatibleWithSpt { get; set; } = [];
}

public sealed class ModUpdateCurrentVersionRef
{
    public int Id { get; set; }
    public int ModId { get; set; }
    public string? Guid { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Version { get; set; }
}

public sealed class ModUpdateRecommendedVersionRef
{
    public int Id { get; set; }
    public string? Version { get; set; }
    public string? Link { get; set; }
    public long? ContentLength { get; set; }
    public string? FikaCompatibility { get; set; }
    public List<string> SptVersions { get; set; } = [];
}

public sealed class ModUpdateEntry
{
    public ModUpdateCurrentVersionRef? CurrentVersion { get; set; }
    public ModUpdateRecommendedVersionRef? RecommendedVersion { get; set; }
    public string? UpdateReason { get; set; }
}

public sealed class ModLatestVersionRef
{
    public int Id { get; set; }
    public string? Version { get; set; }
    public List<string> SptVersions { get; set; } = [];
}

public sealed class BlockingModRef
{
    public int ModId { get; set; }
    public string? ModGuid { get; set; }
    public string? ModName { get; set; }
    public string? CurrentVersion { get; set; }
    public string? Constraint { get; set; }
    public string? IncompatibleWith { get; set; }
}

public sealed class ModBlockedUpdateEntry
{
    public ModUpdateCurrentVersionRef? CurrentVersion { get; set; }
    public ModLatestVersionRef? LatestVersion { get; set; }
    public string? BlockReason { get; set; }
    public List<BlockingModRef> BlockingMods { get; set; } = [];
}

public sealed class ModUpToDateEntry
{
    public int Id { get; set; }
    public int ModId { get; set; }
    public string? Guid { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
    public List<string> SptVersions { get; set; } = [];
}

public sealed class ModIncompatibleEntry
{
    public int Id { get; set; }
    public int ModId { get; set; }
    public string? Guid { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Reason { get; set; }
    public string? LatestCompatibleVersion { get; set; }
}
