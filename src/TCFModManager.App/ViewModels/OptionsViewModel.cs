using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class OptionsViewModel : ObservableObject
{
    // Suppresses the save while the dropdown is being set to what is already stored, so opening the
    // page doesn't count as choosing a theme.
    private readonly bool _loaded;

    // Every write does its own Load first, so this never fights the other things that save settings.
    private readonly SettingsService _settings = new();

    // Guards the toggle being put back after the user declines the warning, so restoring it doesn't
    // run the warning a second time.
    private bool _revertingSkip;

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

    // Turning this on is confirmed first - see the warning in OnSkipModPageConfirmationChanged.
    [ObservableProperty]
    private bool _skipModPageConfirmation;

    // The switch's own tooltip, and the install buttons' - one description of what the setting
    // currently means, shared rather than restated here.
    public ModPageGateViewModel ModPageGate => AppServices.ModPageGate;

    // Whether the Mod footprint page is in the sidebar. Off by default - see AppSettings.
    [ObservableProperty]
    private bool _showModFootprintPage;

    // Same arrangement as ModPageGate: one description of the setting, shared with the nav item.
    public FootprintGateViewModel FootprintGate => AppServices.FootprintGate;

    public OptionsViewModel()
    {
        InstallPathInput = SptEnvironment.InstallPath;

        var stored = AppTheme.Stored;
        _selectedTheme = ThemeOptions.FirstOrDefault(t => t.Value == stored) ?? ThemeOptions[^1];

        var settings = _settings.Load();
        _skipModPageConfirmation = settings.SkipModPageConfirmation;
        _showModFootprintPage = settings.ShowModFootprintPage;

        _loaded = true;
    }

    partial void OnSelectedThemeChanged(ThemeOptionItem value)
    {
        if (!_loaded) return;

        AppTheme.Set(value.Value);
    }

    //
    // Switching the gate off is warned about, switching it back on isn't - there is nothing to warn
    // about in choosing to read more.
    //
    partial void OnSkipModPageConfirmationChanged(bool value)
    {
        if (!_loaded || _revertingSkip) return;

        if (value && !ConfirmSkip())
        {
            _revertingSkip = true;
            SkipModPageConfirmation = false;
            _revertingSkip = false;
            return;
        }

        var settings = _settings.Load();
        settings.SkipModPageConfirmation = value;
        _settings.Save(settings);

        // Every install button's tooltip reads from this, so they all change with the switch.
        AppServices.ModPageGate.Refresh();

        AppLog.Info("ModPages", value ? "gate turned off" : "gate turned back on");
    }

    //
    // No confirmation either way. Nothing is at stake in showing or hiding a read-only page, and
    // the switch's own tooltip carries the caveat that matters.
    //
    partial void OnShowModFootprintPageChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.ShowModFootprintPage = value;
        _settings.Save(settings);

        // Moves the nav item now rather than at the next launch.
        AppServices.FootprintGate.Refresh();

        AppLog.Info("Footprint", value ? "page shown" : "page hidden");
    }

    //
    // Deliberately blunt, and defaulting to No. The gate is the one thing standing between someone
    // and installing a mod whose page says it needs a specific load order, a dependency this app
    // can't see, or a version of SPT they aren't running - and the app genuinely cannot tell them
    // which mods those are.
    //
    private static bool ConfirmSkip() =>
        MessageBox.Show(
            "A mod's page is where its author puts install steps, requirements, known conflicts and "
            + "warnings. Some mods won't work if you skip that, and this app has no way of telling "
            + "you which ones.\n\n"
            + "Turn this off and mods are downloaded and installed straight away, without showing "
            + "you any of it. Knowing what a mod needs becomes yours to keep track of.\n\n"
            + "You can turn it back on at any time.",
            "Stop asking me to read mod pages?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

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
