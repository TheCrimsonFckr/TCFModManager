namespace TCFModManager.Core.Models;

// A mod from /mods?include=versions, read for its embedded versions in full.
public sealed class ModReleases
{
    public int Id { get; set; }
    public List<ModVersion>? Versions { get; set; }
}

// An addon from /addons?include=versions, read for its embedded versions in full.
public sealed class AddonReleases
{
    public int Id { get; set; }
    public List<AddonVersion>? Versions { get; set; }
}
