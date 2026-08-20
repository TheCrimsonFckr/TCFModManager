using System.Windows;
using System.Windows.Threading;
using TCFModManagement.App.Behaviors;
using TCFModManagement.Core.Services;

namespace TCFModManagement.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.Start($"SPT install: {AppServices.SptEnvironment.InstallPath ?? "(not set)"}");

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
            "TCF Mod Management - Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
