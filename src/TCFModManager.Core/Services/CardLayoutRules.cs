using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public static class CardLayoutRules
{
    public static IEnumerable<CardLayout> ForPage(IEnumerable<CardLayout> layouts, CardLayoutPage page) =>
        layouts.Where(l => l.Page == page);

    public static CardLayout? Find(IEnumerable<CardLayout> layouts, CardLayoutPage page, Guid? id) =>
        id is { } wanted ? layouts.FirstOrDefault(l => l.Page == page && l.Id == wanted) : null;

    public static CardLayout? Find(IEnumerable<CardLayout> layouts, CardLayoutPage page, string? id) =>
        Guid.TryParse(id, out var parsed) ? Find(layouts, page, parsed) : null;

    public static PresetNameProblem CheckName(IEnumerable<CardLayout> layouts, CardLayoutPage page, string? name, Guid? renaming = null)
    {
        var clean = FilterPresetRules.CleanName(name);
        if (clean.Length == 0) return PresetNameProblem.Empty;

        return ForPage(layouts, page).Any(l => l.Id != renaming
            && string.Equals(FilterPresetRules.CleanName(l.Name), clean, StringComparison.OrdinalIgnoreCase))
            ? PresetNameProblem.Taken
            : PresetNameProblem.None;
    }

    public static string UniqueName(IEnumerable<CardLayout> layouts, CardLayoutPage page, string baseName)
    {
        var clean = FilterPresetRules.CleanName(baseName);

        var taken = ForPage(layouts, page)
            .Select(l => FilterPresetRules.CleanName(l.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (clean.Length > 0 && !taken.Contains(clean)) return clean;

        for (var n = 2; ; n++)
        {
            var candidate = clean.Length == 0 ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{clean} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    public static List<string> CleanFields(IEnumerable<string>? fields, IReadOnlySet<string> known)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return (fields ?? []).Where(f => known.Contains(f) && seen.Add(f)).ToList();
    }

    public static CardLayout Normalise(CardLayout layout, IReadOnlySet<string> known, IEnumerable<CardLayout> others, string fallbackName)
    {
        layout.Name = FilterPresetRules.CleanName(layout.Name);
        if (layout.Name.Length == 0) layout.Name = UniqueName(others.Where(o => o.Id != layout.Id), layout.Page, fallbackName);

        layout.Fields = CleanFields(layout.Fields, known);
        if (layout.ListFields is not null) layout.ListFields = CleanFields(layout.ListFields, known);

        if (!Enum.IsDefined(layout.Size)) layout.Size = CardSize.Normal;
        if (!Enum.IsDefined(layout.Width)) layout.Width = CardWidth.Normal;
        if (!Enum.IsDefined(layout.Picture)) layout.Picture = CardPicture.Off;

        return layout;
    }

    public static CardLayout Copy(CardLayout layout, string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Page = layout.Page,
        Fields = [.. layout.Fields],
        ListFields = layout.ListFields is null ? null : [.. layout.ListFields],
        Size = layout.Size,
        Width = layout.Width,
        Picture = layout.Picture,
        DetailsInWindow = layout.DetailsInWindow,
        FieldsRevision = layout.FieldsRevision,
    };
}
