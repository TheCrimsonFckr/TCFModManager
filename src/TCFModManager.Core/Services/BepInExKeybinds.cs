namespace TCFModManager.Core.Services;

public sealed record BepInExKeybind<TOwner>(TOwner Owner, BepInExConfigSetting Setting, BepInExShortcut Shortcut);

public sealed record BepInExKeybindGroup<TOwner>(BepInExShortcut Shortcut, IReadOnlyList<BepInExKeybind<TOwner>> Keybinds)
{
    public bool IsClash => Keybinds.Count > 1;
}

public static class BepInExKeybinds
{
    public static IReadOnlyList<BepInExKeybind<TOwner>> Collect<TOwner>(TOwner owner, BepInExConfigFile file) =>
        file.Settings
            .Where(s => BepInExConfigValues.KindOf(s) == BepInExValueKind.Shortcut)
            .Select(s => new BepInExKeybind<TOwner>(owner, s, BepInExShortcut.Read(s.Value)))
            .ToList();

    public static IReadOnlyList<BepInExKeybindGroup<TOwner>> Group<TOwner>(IEnumerable<BepInExKeybind<TOwner>> keybinds)
    {
        var groups = new List<(BepInExShortcut Shortcut, List<BepInExKeybind<TOwner>> Members)>();

        foreach (var keybind in keybinds)
        {
            if (keybind.Shortcut.IsEmpty) continue;

            var index = groups.FindIndex(g => g.Shortcut.UsesSameKeys(keybind.Shortcut));
            if (index >= 0) groups[index].Members.Add(keybind);
            else groups.Add((keybind.Shortcut, [keybind]));
        }

        return groups
            .OrderByDescending(g => g.Members.Count > 1)
            .Select(g => new BepInExKeybindGroup<TOwner>(g.Shortcut, g.Members))
            .ToList();
    }
}
