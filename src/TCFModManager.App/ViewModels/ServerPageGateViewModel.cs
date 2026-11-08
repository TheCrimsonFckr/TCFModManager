using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Whether the Server page's cards are on the Profile page (OPEN-42): switched on in Options, and
// only while the server runs in its own window. Shared so the sidebar, the Profile page and the
// Options switch read one answer.
//
public sealed partial class ServerPageGateViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServerPageShown))]
    private bool _isServerOnProfile;

    public bool IsServerPageShown => !IsServerOnProfile;

    // Re-reads the settings. Called at startup and whenever the Options page changes either one.
    public void Refresh()
    {
        var settings = new SettingsService().Load();
        IsServerOnProfile = settings.ShowServerOnProfile && !settings.HideServerWindow;
    }
}
