using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
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
public sealed partial class FootprintGateViewModel : LocalizedViewModel
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
        ? Strings.Options_FootprintToolTipOn
        : Strings.Options_FootprintToolTipOff;

    // Re-reads the setting. Called at startup and whenever the Options page changes it.
    public void Refresh() => IsPageEnabled = new SettingsService().Load().ShowModFootprintPage;
}
