using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// One entry in Options' "When the app opens" dropdown. Overrides ToString() so the label shows
// instead of the enum name - same pattern as ThemeOptionItem beside it.
public sealed record WindowStartupItem(string Label, WindowStartupMode Value)
{
    public override string ToString() => Label;
}
