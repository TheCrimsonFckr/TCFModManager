namespace TCFModManager.Core.Models;

// 
// What the download queue and the install pipeline are installing: a catalog mod, or an addon
// attached to one. Addon ids and mod ids are separate sequences on sp-mod.com - addon 116 and mod
// 116 are unrelated objects - so IsAddon travels with the id everywhere it is compared or recorded.
// 
public sealed record InstallTarget(
    int Id,
    bool IsAddon,
    string Name,
    string? Guid,
    string? Thumbnail,
    string? DetailUrl)
{
    public static InstallTarget For(Mod mod) => new(
        mod.Id,
        IsAddon: false,
        mod.Name ?? $"Mod {mod.Id}",
        mod.Guid,
        mod.Thumbnail,
        mod.DetailUrl);

    // Addons carry no GUID on sp-mod.com, so identity here is the id/IsAddon pair alone.
    public static InstallTarget For(Addon addon) => new(
        addon.Id,
        IsAddon: true,
        addon.Name ?? $"Addon {addon.Id}",
        Guid: null,
        addon.Thumbnail,
        addon.DetailUrl);

    // True when this target and a manifest record describe the same thing.
    public bool Matches(InstalledModRecord record) => record.ModId == Id && record.IsAddon == IsAddon;
}
