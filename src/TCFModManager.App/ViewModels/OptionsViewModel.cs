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

    //
    // How the window opens. Applied and saved the moment it changes, the same as the theme
    // dropdown - and applied to the window that is already open, so the setting does something now
    // rather than at the next launch.
    //
    public IReadOnlyList<WindowStartupItem> WindowStartupOptions { get; } =
    [
        new("Remember the last size and position", WindowStartupMode.Remember),
        new("Always start maximised", WindowStartupMode.Maximized),
        new("Always start at a size I choose", WindowStartupMode.Custom),
        new("Always start full screen", WindowStartupMode.FullScreen),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCustomSize))]
    private WindowStartupItem _selectedWindowStartup;

    /// <summary>Whether the two size boxes are relevant - they only do anything in the one mode
    /// that reads them.</summary>
    public bool ShowCustomSize => SelectedWindowStartup.Value == WindowStartupMode.Custom;

    //
    // Text rather than numbers, and applied by a button rather than as they are typed: a size box
    // that resizes the window on every keystroke resizes it to 1, then 17, then 172 on the way to
    // 1720.
    //
    [ObservableProperty]
    private string _customWidthInput = string.Empty;

    [ObservableProperty]
    private string _customHeightInput = string.Empty;

    [ObservableProperty]
    private string? _windowSizeMessage;

    //
    // Whether either page has a saved default to put back. The reset buttons show regardless and
    // simply say so - a button that appears only once you have used a feature elsewhere is one
    // nobody finds when they want it.
    //
    [ObservableProperty]
    private string _installedDefaultsDescription = string.Empty;

    [ObservableProperty]
    private string _browseDefaultsDescription = string.Empty;

    //
    // The Server Map section binds straight to the shared connection rather than mirroring it into
    // properties here. It is not a stored setting the way the two switches above are: connecting is
    // an action with a result, and that result is the same object the sidebar and the page read.
    //
    public ServerMapGateViewModel ServerMap => AppServices.ServerMap;

    public OptionsViewModel()
    {
        InstallPathInput = SptEnvironment.InstallPath;

        var stored = AppTheme.Stored;
        _selectedTheme = ThemeOptions.FirstOrDefault(t => t.Value == stored) ?? ThemeOptions[^1];

        var settings = _settings.Load();
        _skipModPageConfirmation = settings.SkipModPageConfirmation;
        _showModFootprintPage = settings.ShowModFootprintPage;

        _selectedWindowStartup = WindowStartupOptions.FirstOrDefault(o => o.Value == settings.Window.StartupMode)
            ?? WindowStartupOptions[0];
        _customWidthInput = FormatSize(settings.Window.CustomWidth);
        _customHeightInput = FormatSize(settings.Window.CustomHeight);

        RefreshPageDefaultDescriptions(settings);

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

    partial void OnSelectedWindowStartupChanged(WindowStartupItem value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.Window.StartupMode = value.Value;
        _settings.Save(settings);

        // Remember deliberately does nothing to the open window - see WindowLayout.ApplyModeNow.
        WindowLayout.ApplyModeNow(value.Value, settings.Window);

        WindowSizeMessage = value.Value switch
        {
            WindowStartupMode.Remember => "The window will reopen wherever you leave it.",
            WindowStartupMode.Maximized => "The window will open maximised.",
            WindowStartupMode.Custom => "The window will open at the size below.",
            _ => "The window will open full screen. F11 leaves full screen at any time.",
        };

        AppLog.Info("Window", $"startup mode set to {value.Value}");
    }

    //
    // Saves and applies the two size boxes together. Anything that isn't a number, or is smaller
    // than the window's own minimum, is clamped rather than refused - the window would ignore it
    // anyway, and a size box that silently rejects what you typed is worse than one that corrects
    // it in front of you.
    //
    [RelayCommand]
    private void ApplyWindowSize()
    {
        var settings = _settings.Load();

        settings.Window.CustomWidth = ParseSize(CustomWidthInput, settings.Window.CustomWidth, WindowSettings.MinimumWidth);
        settings.Window.CustomHeight = ParseSize(CustomHeightInput, settings.Window.CustomHeight, WindowSettings.MinimumHeight);
        _settings.Save(settings);

        // Written back, so a clamped or unparseable entry is visibly corrected rather than left
        // sitting in the box disagreeing with what was saved.
        CustomWidthInput = FormatSize(settings.Window.CustomWidth);
        CustomHeightInput = FormatSize(settings.Window.CustomHeight);

        WindowLayout.ApplyCustomSizeNow(settings.Window);

        WindowSizeMessage = $"Saved - the window will open at {CustomWidthInput} x {CustomHeightInput}.";
    }

    /// <summary>Fills the two boxes from the window as it is right now, so a size can be chosen by
    /// dragging the window rather than by guessing two numbers.</summary>
    [RelayCommand]
    private void UseCurrentWindowSize()
    {
        if (WindowLayout.CurrentSize() is not { } size)
        {
            WindowSizeMessage = "Restore the window down from maximised or full screen first - there is no chosen size to read while it fills the screen.";
            return;
        }

        CustomWidthInput = FormatSize(size.Width);
        CustomHeightInput = FormatSize(size.Height);
        ApplyWindowSize();
    }

    private static double ParseSize(string? input, double current, double minimum) =>
        double.TryParse(input?.Trim(), out var value) && double.IsFinite(value)
            ? Math.Max(value, minimum)
            : Math.Max(current, minimum);

    private static string FormatSize(double value) => ((int)Math.Round(value)).ToString();

    //
    // The two page defaults are cleared here rather than on the pages themselves: the page has a
    // button that saves one, and the place to undo a setting is where the rest of the settings are.
    //
    [RelayCommand]
    private void ResetInstalledDefaults()
    {
        var settings = _settings.Load();
        settings.InstalledDefaults = null;
        _settings.Save(settings);

        RefreshPageDefaultDescriptions(settings);
        AppLog.Info("Installed", "cleared the saved page default");
    }

    [RelayCommand]
    private void ResetBrowseDefaults()
    {
        var settings = _settings.Load();
        settings.BrowseDefaults = null;
        _settings.Save(settings);

        RefreshPageDefaultDescriptions(settings);
        AppLog.Info("Browse", "cleared the saved page default");
    }

    //
    // Both pages read their default once, when they are first built, and both are kept alive for
    // the rest of the session - so this says plainly that clearing one lands at the next launch
    // rather than pretending it takes effect now.
    //
    private void RefreshPageDefaultDescriptions(AppSettings settings)
    {
        InstalledDefaultsDescription = settings.InstalledDefaults is null
            ? "Installed: no default saved - it opens on Cards, unfiltered, sorted by name."
            : "Installed: a default is saved. Clearing it takes effect the next time the app starts.";

        BrowseDefaultsDescription = settings.BrowseDefaults is null
            ? "Browse: no default saved - it opens sorted by newest, filtered to your installed SPT version."
            : "Browse: a default is saved. Clearing it takes effect the next time the app starts.";
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
