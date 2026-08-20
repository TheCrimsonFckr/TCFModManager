using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace TCFModManager.App.ViewModels;

public partial class OptionsViewModel : ObservableObject
{
    public SptEnvironmentViewModel SptEnvironment => AppServices.SptEnvironment;

    [ObservableProperty]
    private string? _installPathInput;

    public OptionsViewModel()
    {
        InstallPathInput = SptEnvironment.InstallPath;
    }

    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "Select your SPT server install folder" };
        if (!string.IsNullOrWhiteSpace(InstallPathInput)) dialog.InitialDirectory = InstallPathInput;

        if (dialog.ShowDialog() == true)
        {
            InstallPathInput = dialog.FolderName;
            Save();
        }
    }

    [RelayCommand]
    private void Save()
    {
        SptEnvironment.SetInstallPath(string.IsNullOrWhiteSpace(InstallPathInput) ? null : InstallPathInput.Trim());
    }
}
