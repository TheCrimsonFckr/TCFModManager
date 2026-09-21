using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using TCFModManager.App.Behaviors;
using TCFModManager.App.Localization;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.Start($"{AppVersion.Current}, SPT install: {AppServices.SptEnvironment.InstallPath ?? "(not set)"}");

        // Before the theme and before any string is read, so the first frame is drawn in the right
        // language rather than re-read a moment later.
        AppLanguage.ApplyStored();

        // Subscribes the source every {loc:Str} binding reads from, so a language chosen before any
        // page has been opened still reaches the bindings made afterwards.
        _ = LocalizationService.Instance;

        ApplyElementLanguage();

        // Before the main window exists, so it is painted in the right theme rather than repainted a
        // moment after it opens. Following the OS needs a real window and is set up in MainWindow.
        AppTheme.ApplyStored();

        // TEMPORARY, ADDED IN v1.5.0 - DELETE WHEN THE APP LEAVES BETA, along with the method
        // itself. Carries a pre-v1.5.0 LegacyConfigs folder from beside the exe into Data\. A no-op
        // on every launch after the first, and on any install that never had one.
        AppPaths.MigrateLegacyConfigsFolder();

        // TEMPORARY, ADDED IN v1.12.0 - DELETE WHEN THE APP LEAVES BETA, along with the method
        // itself. Carries a Server Map key and published list out of TCFModManager\ServerMap\config\
        // and into Data\ServerMap\, so a hand-deploy that replaces TCFModManager\ stops taking the
        // operator's key with it. A no-op unless this machine runs the server mod.
        ServerMapConfigFolder.MigrateLegacyFolder(new SettingsService().Load().SptInstallPath);

        // So the install buttons know from the first frame whether they are skipping mod pages.
        AppServices.ModPageGate.Refresh();

        // Before the window is built, so the sidebar is drawn with the right items rather than
        // gaining one a moment after it opens.
        AppServices.FootprintGate.Refresh();

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

    //
    // WPF gives every element a Language of en-US regardless of what Windows is set to, and that is
    // what a StringFormat binding reads for its dates and numbers. Without this, a machine set to
    // en-GB or de-DE still renders every bound date in the US order - and the app's own rule that
    // dates and numbers follow the regional setting would be silently untrue everywhere in XAML.
    //
    // Reads CurrentCulture, the regional setting, not CurrentUICulture: the chosen language moves
    // the text and nothing else.
    //
    private static void ApplyElementLanguage()
    {
        try
        {
            var tag = CultureInfo.CurrentCulture.IetfLanguageTag;
            if (string.IsNullOrWhiteSpace(tag)) return;

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(tag)));
        }
        catch (Exception ex)
        {
            // Dates in the wrong order are worth a line in the log. They are not worth refusing to
            // start over.
            AppLog.Warn("Language", $"couldn't set the element language: {ex.Message}");
        }
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

        //
        // Keyed like everything else, and safe to be: Get falls back to the key itself rather than
        // throwing, so even a failure inside the language stack leaves a readable dialog.
        //
        MessageBox.Show(
            LocalizationService.Text(Strings.App_CrashBodyFormat, e.Exception, AppLog.CurrentFile),
            Strings.App_CrashTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
