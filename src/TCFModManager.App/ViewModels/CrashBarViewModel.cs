using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// OPEN-54 N30: the red bar at the top of the window when the server, the game or the headless crashed.
public sealed partial class CrashBarViewModel : LocalizedViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Message), nameof(HasMod))]
    private ProcessCrash? _crash;

    [ObservableProperty]
    private bool _isOpen;

    public bool HasMod => Crash?.Mod is not null;

    public string Title
    {
        get
        {
            if (Crash is not { } crash) return string.Empty;
            if (crash.Cause == CrashCause.ServerGaveUp) return Strings.Diagnose_Title_ServerStopped;

            var mod = crash.Mod?.DisplayName;
            return crash.Kind switch
            {
                WatchedProcessKind.Server => mod is null ? Strings.CrashBar_ServerTitle : T(Strings.CrashBar_ServerInModFormat, mod),
                WatchedProcessKind.Headless => mod is null ? Strings.CrashBar_HeadlessTitle : T(Strings.CrashBar_HeadlessInModFormat, mod),
                _ => mod is null ? Strings.CrashBar_GameTitle : T(Strings.CrashBar_GameInModFormat, mod),
            };
        }
    }

    public string Message
    {
        get
        {
            if (Crash is not { } crash) return string.Empty;

            var what = crash.What is { Length: > 0 } line ? line
                : crash.Module is { Length: > 0 } module ? T(Strings.CrashBar_ModuleFormat, module, crash.ExceptionCode ?? string.Empty)
                : T(Strings.CrashBar_ExitCodeFormat, crash.ExceptionCode ?? CrashSigns.Hex(crash.ExitCode));

            return crash.Cause == CrashCause.ServerGaveUp && crash.Mod is { } named
                ? what + " " + T(Strings.Diagnose_Body_ServerStoppedModFormat, named.DisplayName)
                : what;
        }
    }

    public void Show(ProcessCrash crash)
    {
        Crash = crash;
        IsOpen = true;
    }

    [RelayCommand]
    private void Diagnose()
    {
        IsOpen = false;
        AppNavigation.Navigate(typeof(DiagnosePage));
    }

    [RelayCommand]
    private void ShowMod()
    {
        if (Crash?.Mod is not { } mod) return;

        IsOpen = false;
        AppNavigation.ShowInInstalled(mod.Name);
    }

    private static string T(string format, params object?[] values) => LocalizationService.Text(format, values);
}
