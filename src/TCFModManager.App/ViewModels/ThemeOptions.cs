using TCFModManager.Core.Models;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

//
// One entry in the Options page's Theme dropdown. Overrides ToString() so the label shows instead of
// the enum name, the same "enum + labeled record" pattern the Installed page's filters use.
//
// Holds a KEY, not a label: the text is read on every get, so choosing a language relabels the
// dropdown in place. Rebuilding the list instead would replace the item instances and drop the
// SelectedItem binding, which resets the dropdown to its first entry every time - see D9.
//
public sealed class ThemeOptionItem : LocalizedViewModel
{
    private readonly string? _key;
    private readonly string? _name;

    public ThemeOptionItem(string key, ThemePreference value)
    {
        _key = key;
        Value = value;
    }

    private ThemeOptionItem(string? key, string? name, string themeId)
    {
        _key = key;
        _name = name;
        ThemeId = themeId;
    }

    public static ThemeOptionItem Preset(string key, string themeId) => new(key, null, themeId);

    public static ThemeOptionItem Saved(string name, string themeId) => new(null, name, themeId);

    public ThemePreference Value { get; }

    public string? ThemeId { get; }

    public string Label => _key is not null ? LocalizationService.Get(_key) : _name ?? "";

    public override string ToString() => Label;
}
