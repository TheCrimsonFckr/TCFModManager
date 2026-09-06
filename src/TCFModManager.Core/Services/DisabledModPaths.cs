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

    //
    // True for BepInEx\patchers, or its ".disabled" sibling - the preloader's container, as opposed
    // to BepInEx\plugins. Read from the path's own last segment so it holds for either state, and
    // so the scanner can tell which of the two client containers it is walking without the caller
    // having to thread that through from ClientContainers.
    //
    public static bool IsPatcherContainer(string containerPath)
    {
        var trimmed = containerPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(Enabled(trimmed)), PatchersFolder, StringComparison.OrdinalIgnoreCase);
    }

    private const string PatchersFolder = "patchers";

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
    // Everything above answers a question about a folder on disk. Everything below answers the
    // same questions about a forward-slash path relative to a root - the shape a mod list entry, a
    // served inventory or any cross-machine payload carries, where there is no drive letter to
    // anchor on and Path.GetDirectoryName means the wrong thing.
    //
    // This is what stops a disabled mod being read as missing: a scanner that only looks at the
    // live path sees nothing there and reinstalls it, silently re-enabling something the user
    // turned off. Hashing the disabled copy instead is no better - the diff then reads "present but
    // different" and updates it into the live folder, re-enabling it another way. Present-but-
    // disabled has to mean no action at all, and that needs the counterpart lookup below.
    //

    // Forward slashes, no leading separator. Accepts either separator and is safe to call twice.
    public static string ToRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    //
    // The container a relative path sits in, spelled as it appears in that path - so
    // "BepInEx/plugins.disabled" when the container is disabled.
    //
    // Matches on segment boundaries anywhere in the path, so server content still matches under a
    // "SPT_Runtime/" prefix. The LEFTMOST match wins: a client mod whose own folder is called
    // "user/mods" belongs to the BepInEx/plugins container it sits in, and picking the inner one
    // would name a counterpart that cannot exist.
    //
    public static bool TryFindRelativeContainer(string relativePath, out string container, out bool disabled)
    {
        container = "";
        disabled = false;

        var path = ToRelative(relativePath);

        var bestEnd = int.MaxValue;
        var bestDisabled = false;

        foreach (var name in Containers)
        {
            foreach (var disabledForm in new[] { false, true })
            {
                var end = MatchEnd(path, disabledForm ? name + DisabledSuffix : name);
                if (end >= 0 && end < bestEnd)
                {
                    bestEnd = end;
                    bestDisabled = disabledForm;
                }
            }
        }

        if (bestEnd == int.MaxValue) return false;

        container = path.Substring(0, bestEnd);
        disabled = bestDisabled;
        return true;
    }

    // The same file in the container's other state. False when the path is in no known container.
    public static bool TryGetRelativeCounterpart(string relativePath, out string counterpart)
    {
        counterpart = "";

        if (!TryFindRelativeContainer(relativePath, out var container, out var disabled)) return false;

        var swapped = disabled
            ? container.Substring(0, container.Length - DisabledSuffix.Length)
            : container + DisabledSuffix;

        counterpart = swapped + ToRelative(relativePath).Substring(container.Length);
        return true;
    }

    public static bool IsRelativePathDisabled(string relativePath) =>
        TryFindRelativeContainer(relativePath, out _, out var disabled) && disabled;

    //
    // The path as it would read with its container enabled. Unchanged when it already is, or when
    // it is in no known container, so it is safe to key a lookup on.
    //
    public static string ToEnabledRelativePath(string relativePath) =>
        IsRelativePathDisabled(relativePath) && TryGetRelativeCounterpart(relativePath, out var enabled)
            ? enabled
            : ToRelative(relativePath);

    //
    // End index of the first prefix of <paramref name="path"/> that finishes with
    // <paramref name="form"/> on segment boundaries, or -1.
    //
    private static int MatchEnd(string path, string form)
    {
        var from = 0;
        while (from <= path.Length - form.Length)
        {
            var at = path.IndexOf(form, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;

            var end = at + form.Length;
            var startsOnBoundary = at == 0 || path[at - 1] == '/';
            var endsOnBoundary = end == path.Length || path[end] == '/';

            if (startsOnBoundary && endsOnBoundary) return end;

            from = at + 1;
        }

        return -1;
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
