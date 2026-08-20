using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManagement.Core.Models;
using TCFModManagement.Core.Services;

namespace TCFModManagement.App.ViewModels;

// Shared, app-lifetime state for where SPT is installed and what version was detected there.
public partial class SptEnvironmentViewModel : ObservableObject
{
    private readonly SettingsService _settings = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallPathMissing))]
    private string? _installPath;

    [ObservableProperty]
    private string? _installedVersion;

    [ObservableProperty]
    private string? _statusMessage;

    // True until an SPT install folder has been set.
    public bool IsInstallPathMissing => string.IsNullOrWhiteSpace(InstallPath);

    public SptEnvironmentViewModel()
    {
        InstallPath = _settings.Load().SptInstallPath;
        Redetect();
    }

    public void SetInstallPath(string? path)
    {
        InstallPath = path;
        _settings.Save(new AppSettings { SptInstallPath = path });
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
