using System.Windows;
using System.Windows.Threading;
using TCFModManager.App.Behaviors;
using TCFModManager.Core.Services;

namespace TCFModManager.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.Start($"{AppVersion.Current}, SPT install: {AppServices.SptEnvironment.InstallPath ?? "(not set)"}");

        // Before the main window exists, so it is painted in the right theme rather than repainted a
        // moment after it opens. Following the OS needs a real window and is set up in MainWindow.
        AppTheme.ApplyStored();

        // TEMPORARY, ADDED IN v1.5.0 - DELETE WHEN THE APP LEAVES BETA, along with the method
        // itself. Carries a pre-v1.5.0 LegacyConfigs folder from beside the exe into Data\. A no-op
        // on every launch after the first, and on any install that never had one.
        AppPaths.MigrateLegacyConfigsFolder();

        // Reports how a self-update went (the script doing the swap runs after the previous process
        // is gone, so its own log is the only record of it) and clears out the staged files.
        AppUpdateInstaller.SweepAfterStartup();

        // Shows unhandled dispatcher exceptions instead of crashing/hanging silently.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Catches anything thrown off the UI thread, which the dispatcher handler never sees.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("App", "Unhandled exception", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("App", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DependencyBadgeLoader.Flush();
        AppLog.Info("App", "Shutting down");
        AppLog.Flush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("App", "Unhandled UI exception", e.Exception);

        MessageBox.Show(
            $"Something went wrong and wasn't handled:\n\n{e.Exception}\n\nThis was written to:\n{AppLog.CurrentFile}",
            "TCF Mod Manager - Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
