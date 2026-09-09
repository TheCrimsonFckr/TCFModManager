using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.App.Views;
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

    // Same job for the two install-role switches, which are written back after the setup prompt
    // answers them - without this, filling them in would count as the user flipping them.
    private bool _settingInstallRole;

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
    // What this machine does with the install - see AppSettings.PlaysHere / RunsHeadlessClient.
    //
    // Two switches rather than one dropdown, because "dedicated headless" is just the second without
    // the first, and a machine that does both needs no value of its own.
    //
    [ObservableProperty]
    private bool _playsHere = true;

    [ObservableProperty]
    private bool _runsHeadlessClient;

    // Whether a Fika headless launcher is actually there. Drives the hint under the switches - a
    // machine with no launcher that claims to run a headless is worth mentioning, not preventing.
    [ObservableProperty]
    private bool _hasHeadlessLauncher;

    //
    // The headless launcher, named by hand, for a setup that keeps it somewhere the search will
    // never look.
    //
    // Detection looks at the top of the install folder and nowhere else, on purpose - see
    // SptLaunchService, where widening it has now gone wrong twice in opposite directions. A path
    // somebody typed is not a guess, so it is the way out for any layout, including a manager
    // sitting beside the SPT folder rather than in it.
    //
    [ObservableProperty]
    private string _headlessLauncherPath = string.Empty;

    [ObservableProperty]
    private string _installRoleDescription = string.Empty;

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
        RefreshInstallRole(settings);

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

    //
    // Neither switch is confirmed. Nothing on disk moves either way - the roles only decide which
    // entries of a list a SERVER hands you are this machine's to install, and the next preview shows
    // exactly what that came to before anything is applied.
    //
    partial void OnPlaysHereChanged(bool value) => SaveInstallRole();

    //
    // Saved as it is typed, like the switches beside it. Blank clears it back to detection rather
    // than storing an empty string, so a cleared box and a machine that never had one look the same
    // in settings.json.
    //
    partial void OnHeadlessLauncherPathChanged(string value)
    {
        if (!_loaded || _settingInstallRole) return;

        var settings = _settings.Load();
        settings.HeadlessLauncherPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _settings.Save(settings);

        RefreshInstallRole(settings);

        AppLog.Info("InstallRole", $"headless launcher path set to \"{settings.HeadlessLauncherPath}\"");
    }

    //
    // Points at the exe itself rather than a folder: the whole reason this box exists is that the
    // folder is not one the app can work the name out from.
    //
    [RelayCommand]
    private void BrowseHeadlessLauncher()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the Fika headless launcher",
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(HeadlessLauncherPath))
        {
            try
            {
                dialog.InitialDirectory = Path.GetDirectoryName(HeadlessLauncherPath);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
            {
                // A stored path that is not a path any more. The dialog opens wherever it likes.
            }
        }

        if (dialog.ShowDialog() == true) HeadlessLauncherPath = dialog.FileName;
    }

    [RelayCommand]
    private void ClearHeadlessLauncher() => HeadlessLauncherPath = string.Empty;

    partial void OnRunsHeadlessClientChanged(bool value) => SaveInstallRole();

    private void SaveInstallRole()
    {
        if (!_loaded || _settingInstallRole) return;

        var settings = _settings.Load();
        settings.PlaysHere = PlaysHere;
        settings.RunsHeadlessClient = RunsHeadlessClient;
        _settings.Save(settings);

        RefreshInstallRoleDescription();

        AppLog.Info("InstallRole", $"playsHere={PlaysHere} headless={RunsHeadlessClient}");
    }

    //
    // Reads the two switches back out of settings, and re-checks whether the folder actually has a
    // headless launcher in it.
    //
    // Written through the guard rather than to the backing fields, so the page updates without the
    // assignment being mistaken for the user answering.
    //
    private void RefreshInstallRole(AppSettings settings)
    {
        _settingInstallRole = true;

        var roles = settings.Roles;
        PlaysHere = roles.HasFlag(InstallRoles.Player);
        RunsHeadlessClient = roles.HasFlag(InstallRoles.Headless);
        HeadlessLauncherPath = settings.HeadlessLauncherPath ?? string.Empty;

        //
        // The named path counts as having one, and it stands on its own: a machine can name a
        // launcher outside the install folder, which is exactly the case the box was added for.
        //
        HasHeadlessLauncher = !string.IsNullOrWhiteSpace(SptEnvironment.InstallPath)
            && SptLaunchService.TryFindHeadlessLauncherExe(
                SptEnvironment.InstallPath!, out _, settings.HeadlessLauncherPath);

        _settingInstallRole = false;

        RefreshInstallRoleDescription();
    }

    private void RefreshInstallRoleDescription()
    {
        InstallRoleDescription = (PlaysHere, RunsHeadlessClient) switch
        {
            (false, true) =>
                "Dedicated headless. A mod list a server hands this machine arrives without the mods"
                + " only a player would need - it still gets everything that decides how a raid goes.",

            (true, true) =>
                "Plays and hosts. A served list arrives whole, because both kinds of mod have"
                + " somewhere to be useful here.",

            (false, false) =>
                "Neither switch is on, so this reads as an ordinary player install - the same as"
                + " leaving both alone. Turn one on rather than relying on that.",

            _ => "An ordinary player install. A served list arrives as it always has.",
        };
    }

    //
    // Puts the question once, when an install folder turns out to hold a headless launcher.
    //
    // Only when it has one and only when nobody has answered yet, so an ordinary install never meets
    // this and answering it once is the end of it. "Ask me later" stores nothing, which leaves the
    // machine reading as a player - the answer that installs everything - and brings the question
    // back next time the folder is set.
    //
    private void PromptForInstallRoleIfNeeded()
    {
        var installPath = SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var settings = _settings.Load();
        if (settings.InstallRolesAnswered) return;

        if (!SptLaunchService.TryFindHeadlessLauncherExe(
                installPath!, out var launcher, settings.HeadlessLauncherPath))
        {
            return;
        }

        var choice = InstallRoleWindow.Ask(launcher);
        if (choice == InstallRoleChoice.AskLater) return;

        settings.PlaysHere = choice == InstallRoleChoice.PlaysHereToo;

        // Yes either way: the launcher on disk is what asked the question, and it is the half of it
        // that does not need a person.
        settings.RunsHeadlessClient = true;

        _settings.Save(settings);

        RefreshInstallRole(settings);

        AppLog.Info("InstallRole", $"answered at setup: {choice}");
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

        //
        // Setting the folder is this app's setup step - there is no first-run wizard - so it is
        // where the machine gets asked what it is. After SetInstallPath, because the question is
        // about the folder that was just chosen.
        //
        PromptForInstallRoleIfNeeded();

        RefreshInstallRole(_settings.Load());
    }
}
