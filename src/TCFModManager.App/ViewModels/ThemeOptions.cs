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
public sealed class ThemeOptionItem(string key, ThemePreference value) : LocalizedViewModel
{
    public ThemePreference Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}
