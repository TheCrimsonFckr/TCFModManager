using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Why a name can't be used for a preset. The App words it.
public enum PresetNameProblem
{
    None,

    // Nothing but spaces.
    Empty,

    // Another preset on the same page already has it (case ignored). Carries nothing - the caller
    // has the name, and asks whether to replace that preset.
    Taken,
}

//
// The rules every preset list on a page follows (OPEN-25 §2.1): names trimmed, inner runs of spaces
// collapsed, unique per page ignoring case, never empty. No cap on how many.
//
public static class FilterPresetRules
{
    /// <summary>The name as it is stored: trimmed, inner spaces collapsed. Empty for a blank name.</summary>
    public static string CleanName(string? name) =>
        string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The preset with this name on the page, ignoring case and spacing, or null.</summary>
    public static T? Find<T>(IEnumerable<T> presets, string? name) where T : IFilterPreset
    {
        var clean = CleanName(name);
        return clean.Length == 0
            ? default
            : presets.FirstOrDefault(p => string.Equals(CleanName(p.Name), clean, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether <paramref name="name"/> can name a preset - a new one, or <paramref name="renaming"/>.</summary>
    public static PresetNameProblem Check<T>(IEnumerable<T> presets, string? name, Guid? renaming = null) where T : IFilterPreset
    {
        if (CleanName(name).Length == 0) return PresetNameProblem.Empty;

        return Find(presets, name) is { } existing && existing.Id != renaming
            ? PresetNameProblem.Taken
            : PresetNameProblem.None;
    }

    //
    // Saves a preset into the list: one with the same name (ignoring case) is replaced in place, so
    // its position and its Id stay - a pinned dropdown showing it keeps showing it - otherwise it
    // goes on the end. The stored name is the cleaned one. Returns the preset as stored.
    //
    public static T Save<T>(List<T> presets, T preset) where T : IFilterPreset
    {
        preset.Name = CleanName(preset.Name);
        if (preset.Name.Length == 0) throw new ArgumentException("A preset needs a name.", nameof(preset));

        var index = presets.FindIndex(p => string.Equals(CleanName(p.Name), preset.Name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            presets.Add(preset);
            return preset;
        }

        var keptId = presets[index].Id;
        presets[index] = preset;
        return WithId(preset, keptId);
    }

    /// <summary>Renames a preset. False when the name is empty or taken by another preset.</summary>
    public static bool Rename<T>(List<T> presets, Guid id, string? name) where T : IFilterPreset
    {
        if (Check(presets, name, id) != PresetNameProblem.None) return false;

        var preset = presets.FirstOrDefault(p => p.Id == id);
        if (preset is null) return false;

        preset.Name = CleanName(name);
        return true;
    }

    private static T WithId<T>(T preset, Guid id) where T : IFilterPreset
    {
        switch (preset)
        {
            case BrowseFilterPreset browse: browse.Id = id; break;
            case InstalledFilterPreset installed: installed.Id = id; break;
        }

        return preset;
    }
}
