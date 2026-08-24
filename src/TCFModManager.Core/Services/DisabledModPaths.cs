namespace TCFModManager.Core.Services;

//
// The folders an SPT install loads mods from, and their ".disabled" siblings. A disabled mod is
// moved out of the container SPT reads into a sibling of the same name plus ".disabled"
// (user\mods -> user\mods.disabled), so nothing loads it and nothing is deleted. Whether a mod is
// disabled is always answered from where it sits on disk, never from a stored flag.
//
public static class DisabledModPaths
{
    public const string DisabledSuffix = ".disabled";

    // Container folders whose immediate children are mods, relative to the install root and
    // forward-slash separated. Matched anywhere in a path, since server content is remapped under
    // the install's own server root (e.g. "SPT_Runtime/user/mods/...").
    public static readonly string[] Containers =
    [
        "BepInEx/plugins",
        "BepInEx/patchers",
        "user/mods",
    ];

    // The three known server-content layouts, whichever of them exists in a given install.
    private static readonly string[][] ServerModsLayouts =
    [
        ["SPT_Runtime", "user", "mods"],
        ["SPT", "user", "mods"],
        ["user", "mods"],
    ];

    // Absolute paths of the client containers (BepInEx\plugins, BepInEx\patchers) in an install.
    public static IEnumerable<string> ClientContainers(string installPath)
    {
        yield return Path.Combine(installPath, "BepInEx", "plugins");
        yield return Path.Combine(installPath, "BepInEx", "patchers");
    }

    // Absolute paths of every server container layout in an install, existing or not.
    public static IEnumerable<string> ServerContainers(string installPath) =>
        ServerModsLayouts.Select(segments => Path.Combine([installPath, .. segments]));

    // "...\user\mods" -> "...\user\mods.disabled". Already-disabled paths are returned unchanged.
    public static string Disabled(string containerPath) =>
        IsDisabled(containerPath) ? containerPath : containerPath + DisabledSuffix;

    // "...\user\mods.disabled" -> "...\user\mods". Already-enabled paths are returned unchanged.
    public static string Enabled(string containerPath) =>
        IsDisabled(containerPath)
            ? containerPath[..^DisabledSuffix.Length]
            : containerPath;

    // True for a path whose own last segment carries the suffix - a container, not a mod inside one.
    public static bool IsDisabled(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);

    //
    // True when a mod's own folder (or loose DLL) sits inside a disabled container. Read from the
    // parent folder, so it holds for both "user\mods.disabled\SomeMod" and a loose
    // "BepInEx\plugins.disabled\SomeMod.dll".
    //
    public static bool IsModDisabled(string modPath)
    {
        var parent = Path.GetDirectoryName(modPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return parent is not null && IsDisabled(parent);
    }

    //
    // Where a mod would live in the opposite state - the same name under the container's
    // ".disabled" sibling, or back under the live container. Null when the path has no parent to
    // move it out of.
    //
    public static string? Counterpart(string modPath)
    {
        var trimmed = modPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed);
        var name = Path.GetFileName(trimmed);

        if (parent is null || string.IsNullOrEmpty(name)) return null;

        var target = IsDisabled(parent) ? Enabled(parent) : Disabled(parent);
        return Path.Combine(target, name);
    }
}
