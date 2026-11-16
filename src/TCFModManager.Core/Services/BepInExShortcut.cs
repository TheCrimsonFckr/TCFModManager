namespace TCFModManager.Core.Services;

public sealed class BepInExShortcut : IEquatable<BepInExShortcut>
{
    private static readonly char[] Separators = [' ', '+', ',', ';', '|'];

    public static readonly BepInExShortcut Empty = new(UnityKeyCodes.None, []);

    private BepInExShortcut(string mainKey, IReadOnlyList<string> modifiers)
    {
        MainKey = mainKey;
        Modifiers = modifiers;
    }

    public string MainKey { get; }

    public IReadOnlyList<string> Modifiers { get; }

    public bool IsEmpty => MainKey == UnityKeyCodes.None;

    public IEnumerable<string> AllKeys => IsEmpty ? [] : Modifiers.Prepend(MainKey);

    public static BepInExShortcut Create(string mainKey, IEnumerable<string> modifiers)
    {
        if (!UnityKeyCodes.TryGetValue(mainKey, out var main))
            throw new ArgumentException($"Not a Unity KeyCode: {mainKey}", nameof(mainKey));

        var values = new List<int>();
        foreach (var modifier in modifiers)
        {
            if (!UnityKeyCodes.TryGetValue(modifier, out var value))
                throw new ArgumentException($"Not a Unity KeyCode: {modifier}", nameof(modifiers));
            values.Add(value);
        }

        return FromValues(main, values);
    }

    public static bool TryParse(string? text, out BepInExShortcut shortcut)
    {
        shortcut = Empty;
        var parts = (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return true;

        var values = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!UnityKeyCodes.TryGetValue(parts[i], out values[i])) return false;
        }

        shortcut = FromValues(values[0], values.Skip(1));
        return true;
    }

    public static BepInExShortcut Read(string? text) => TryParse(text, out var shortcut) ? shortcut : Empty;

    public string Format() => IsEmpty ? string.Empty : string.Join(" + ", AllKeys);

    public bool Equals(BepInExShortcut? other) =>
        other is not null
        && Value(MainKey) == Value(other.MainKey)
        && Modifiers.Select(Value).SequenceEqual(other.Modifiers.Select(Value));

    public bool UsesSameKeys(BepInExShortcut other) => KeySet().SequenceEqual(other.KeySet());

    public override bool Equals(object? obj) => Equals(obj as BepInExShortcut);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Value(MainKey));
        foreach (var modifier in Modifiers) hash.Add(Value(modifier));
        return hash.ToHashCode();
    }

    public override string ToString() => Format();

    internal IEnumerable<int> KeySet() => AllKeys.Select(Value).Order();

    private static BepInExShortcut FromValues(int main, IEnumerable<int> modifiers)
    {
        if (main == 0) return Empty;

        var names = modifiers
            .Distinct()
            .Where(value => value != main)
            .Order()
            .Select(UnityKeyCodes.NameOf)
            .ToList();

        return new BepInExShortcut(UnityKeyCodes.NameOf(main), names);
    }

    private static int Value(string name) => UnityKeyCodes.TryGetValue(name, out var value) ? value : 0;
}
