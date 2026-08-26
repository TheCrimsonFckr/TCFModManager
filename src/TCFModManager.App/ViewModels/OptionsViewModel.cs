using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

public partial class OptionsViewModel : ObservableObject
{
    // Suppresses the save while the dropdown is being set to what is already stored, so opening the
    // page doesn't count as choosing a theme.
    private readonly bool _loaded;

    public SptEnvironmentViewModel SptEnvironment => AppServices.SptEnvironment;

    [ObservableProperty]
    private string? _installPathInput;

    public IReadOnlyList<ThemeOptionItem> ThemeOptions { get; } =
    [
        new("Follow system", ThemePreference.FollowSystem),
        new("Light", ThemePreference.Light),
        new("Dark", ThemePreference.Dark),
    ];

    // Applied and saved the moment it changes - there is nothing here to confirm, and watching the
    // theme change as you pick it is the point.
    [ObservableProperty]
    private ThemeOptionItem _selectedTheme;

    public OptionsViewModel()
    {
        InstallPathInput = SptEnvironment.InstallPath;

        var stored = AppTheme.Stored;
        _selectedTheme = ThemeOptions.FirstOrDefault(t => t.Value == stored) ?? ThemeOptions[^1];

        _loaded = true;
    }

    partial void OnSelectedThemeChanged(ThemeOptionItem value)
    {
        if (!_loaded) return;

        AppTheme.Set(value.Value);
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
