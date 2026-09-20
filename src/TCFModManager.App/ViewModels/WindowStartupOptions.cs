using TCFModManager.Core.Models;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

// One entry in Options' "When the app opens" dropdown. Overrides ToString() so the label shows
// instead of the enum name - same pattern as ThemeOptionItem beside it.
public sealed class WindowStartupItem(string key, WindowStartupMode value) : LocalizedViewModel
{
    public WindowStartupMode Value { get; } = value;

    // Read on every get, so a language change relabels the dropdown rather than rebuilding it.
    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}
