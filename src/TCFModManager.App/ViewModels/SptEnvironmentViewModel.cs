using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// Shared, app-lifetime state for where SPT is installed and what version was detected there.
public partial class SptEnvironmentViewModel : ObservableObject
{
    private readonly SettingsService _settings = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallPathMissing))]
    private string? _installPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _installedVersion;

    [ObservableProperty]
    private string? _statusMessage;

    // True until an SPT install folder has been set.
    public bool IsInstallPathMissing => string.IsNullOrWhiteSpace(InstallPath);

    //
    // What the window and its title bar show - the app and its version, plus which SPT release
    // this window is pointed at.
    //
    // Running two installs side by side is normal, and every page, dialog and Data\ file belongs
    // to one of them. Until this was here the only way to tell two windows apart was to open the
    // Options page in each, which made a perfectly correct reading - the same mod at different
    // versions in each install - look like the app contradicting its own manifest.
    //
    // Deliberately the SPT version and nothing else. The install folder's name was tried first and
    // read as a client/server label, which is not what an install folder means. The cost is that
    // two installs on the same SPT line look alike; the title saying one unambiguous thing is
    // worth more than covering that case.
    //
    // Falls back to the bare app title when no version has been detected - no install set, or a
    // folder with no server exe in it - rather than showing an empty bracket.
    //
    public string WindowTitle => string.IsNullOrWhiteSpace(InstalledVersion)
        ? AppVersion.DisplayTitle
        : $"{AppVersion.DisplayTitle} [SPT {InstalledVersion}]";

    public SptEnvironmentViewModel()
    {
        InstallPath = _settings.Load().SptInstallPath;
        Redetect();
    }

    public void SetInstallPath(string? path)
    {
        InstallPath = path;

        // Load-mutate-save rather than saving a fresh AppSettings: settings.json holds more than
        // the install path now (the skipped app-update version), and constructing a new object here
        // would silently drop everything this view model doesn't know about.
        var settings = _settings.Load();
        settings.SptInstallPath = path;
        _settings.Save(settings);

        Redetect();
    }

    private void Redetect()
    {
        if (SptInstallationService.TryGetInstalledVersion(InstallPath, out var version, out var error))
        {
            InstalledVersion = version;
            StatusMessage = $"Detected SPT {version}.";
        }
        else
        {
            InstalledVersion = null;
            StatusMessage = error;
        }
    }
}
