using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Whether the Mod footprint page is switched on, and the wording the Options switch uses to say
// what that means.
//
// A shared singleton for the same reason ModPageGateViewModel is one: the sidebar item and the
// Options switch are two views of a single stored choice, and flicking the switch has to move the
// nav item at that moment rather than at the next launch.
//
public sealed partial class FootprintGateViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingToolTip))]
    private bool _isPageEnabled;

    //
    // The switch's tooltip. Says what the page is *and* what it is not, because the second half is
    // the part that decides whether someone reads a level correctly, and the Options page is where
    // they are choosing whether to see it at all.
    //
    public string SettingToolTip => IsPageEnabled
        ? "The Mod footprint page is in the sidebar. Remember that it reads files rather than "
          + "timing anything - what a mod actually costs depends on your hardware, your settings "
          + "and the other mods you run, none of which it can see."
        : "Adds a page that reads what each installed mod ships and describes how much of the game "
          + "it is positioned to touch. Nothing on it is timed or measured, and a heavy footprint "
          + "is not the same as a mod that costs you frames.";

    // Re-reads the setting. Called at startup and whenever the Options page changes it.
    public void Refresh() => IsPageEnabled = new SettingsService().Load().ShowModFootprintPage;
}
