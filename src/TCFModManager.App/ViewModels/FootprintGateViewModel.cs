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
    private bool _isPageEnabled;

    // Re-reads the setting. Called at startup and whenever the Options page changes it.
    public void Refresh() => IsPageEnabled = new SettingsService().Load().ShowModFootprintPage;
}
