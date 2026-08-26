using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

//
// One entry in the Options page's Theme dropdown. Overrides ToString() so the label shows instead of
// the enum name, the same "enum + labeled record" pattern the Installed page's filters use.
//
public sealed record ThemeOptionItem(string Label, ThemePreference Value)
{
    public override string ToString() => Label;
}
