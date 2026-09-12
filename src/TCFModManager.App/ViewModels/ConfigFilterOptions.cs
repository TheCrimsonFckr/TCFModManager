using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public enum ConfigSourceFilter
{
    // No restriction - every config found shows.
    All,

    // Only BepInEx\config files belonging to an installed plugin.
    Client,

    // Only configs inside a server mod's own folder.
    Server,

    // Only BepInEx's own files and configs no installed plugin claims - the two the list keeps
    // apart from mods, grouped together here because "not a mod's" is the one thing they share.
    Other,
}

// One entry in the Configs page's source dropdown. Overrides ToString() so the label shows instead
// of the enum name, same as the Installed page's filter items.
public sealed record ConfigSourceFilterItem(string Label, ConfigSourceFilter Value)
{
    public override string ToString() => Label;

    public bool Matches(ModConfigSource source) => Value switch
    {
        ConfigSourceFilter.All => true,
        ConfigSourceFilter.Client => source == ModConfigSource.Client,
        ConfigSourceFilter.Server => source == ModConfigSource.Server,
        _ => source is ModConfigSource.Framework or ModConfigSource.Unmatched,
    };
}

//
// One choice in the update-policy dropdown on the Configs page. ToString is what the closed ComboBox
// shows, the same shape the source filter above uses.
//
public sealed record ConfigPolicyOption(string Label, ModConfigPolicy Value)
{
    public override string ToString() => Label;
}
