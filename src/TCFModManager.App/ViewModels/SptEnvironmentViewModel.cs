using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// Shared, app-lifetime state for where SPT is installed and what version was detected there.
public partial class SptEnvironmentViewModel : ObservableObject
{
    private readonly SettingsService _settings = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallPathMissing))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _installPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _installedVersion;

    [ObservableProperty]
    private string? _statusMessage;

    // True until an SPT install folder has been set.
    public bool IsInstallPathMissing => string.IsNullOrWhiteSpace(InstallPath);

    //
    // What the window and its title bar show - the app and its version, plus which install this
    // window is pointed at.
    //
    // Running two installs side by side (a 4.0 server and a 4.1 client, say) is normal, and every
    // page, dialog and Data\ file belongs to one of them. Until this was here the only way to tell
    // two windows apart was to open the Options page in each, which made a perfectly correct
    // reading - the same mod at different versions in each install - look like the app contradicting
    // its own manifest.
    //
    // Falls back to just the app title when no install is set, rather than showing an empty
    // bracket: at that point there is nothing to disambiguate.
    //
    public string WindowTitle =>
        DescribeInstall() is { } install ? $"{AppVersion.DisplayTitle} [{install}]" : AppVersion.DisplayTitle;

    // The folder tells two installs apart; the SPT version says which is which at a glance. Either
    // half can be missing - a path that no longer exists still has a name worth showing.
    private string? DescribeInstall()
    {
        var folder = FolderName(InstallPath);
        var version = string.IsNullOrWhiteSpace(InstalledVersion) ? null : $"SPT {InstalledVersion}";

        return (folder, version) switch
        {
            (null, null) => null,
            (null, _) => version,
            (_, null) => folder,
            _ => $"{folder}, {version}",
        };
    }

    //
    // The last segment of the install path. Splits on both separators by hand rather than going
    // through Path, so a Windows path still resolves when this runs anywhere else - the App only
    // ever runs on Windows, but its view models are exercised headless on Linux.
    //
    private static string? FolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var trimmed = path.Trim().TrimEnd('\\', '/');
        if (trimmed.Length == 0) return null;

        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        var name = cut >= 0 ? trimmed[(cut + 1)..] : trimmed;

        // An install sitting at a drive root ("D:\") has no folder name of its own, and the root
        // is then the only thing identifying it.
        return name.Length == 0 ? trimmed : name;
    }

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
