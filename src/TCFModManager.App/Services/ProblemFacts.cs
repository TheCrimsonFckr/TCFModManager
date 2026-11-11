using System.IO;
using System.Reflection;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

public static class ProblemFacts
{
    private const int BuildLength = 7;

    public static ProblemContext Context(
        ProblemSource source,
        Exception? exception = null,
        string? shown = null,
        ModListApplyResult? listResult = null,
        string? exceptionText = null)
    {
        var settings = Try("settings", () => new SettingsService().Load(), new AppSettings());

        return new ProblemContext
        {
            Source = source,
            Setup = Setup(settings),
            Exception = exception,
            ExceptionText = exceptionText,
            ListResult = listResult,
            Shown = shown,
            Log = Try<string?>("log", ProblemLog.ReadCurrentSession, null),
            Redaction = Redaction(settings),
        };
    }

    public static IReadOnlyList<ProblemMod> InstalledMods() =>
        Try<IReadOnlyList<ProblemMod>>("installedMods", ReadInstalledMods, []);

    private static ProblemSetup Setup(AppSettings settings)
    {
        var install = Try("installPath", () => AppServices.SptEnvironment.InstallPath, settings.SptInstallPath);
        var hasInstall = !string.IsNullOrWhiteSpace(install);

        return new ProblemSetup
        {
            AppVersion = AppVersion.Current,
            AppBuild = Build(),
            SingleFile = string.IsNullOrEmpty(typeof(ProblemFacts).Assembly.Location),
            UiLanguage = Try<string?>("language", () => AppLanguage.Current.Name, null),
            SptVersion = Try("sptVersion", () => AppServices.SptEnvironment.InstalledVersion, null),
            SptLayout = hasInstall ? Try<string?>("layout", () => Layout(install!), null) : null,
            Headless = hasInstall
                ? Try<bool?>("headless", () => SptLaunchService.TryFindHeadlessLauncherExe(install!, out _, settings.HeadlessLauncherPath), null)
                : null,
            SptInstallPath = install,
            MonitorMode = settings.Monitor.InstallMode == InstallMode.DownloadOnly,
            ModPageGate = !settings.SkipModPageConfirmation,
            ServerMapConnected = Try("serverMap", () => AppServices.ServerMap.IsConnected, false),
        };
    }

    private static RedactionValues Redaction(AppSettings settings) => new()
    {
        UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        UserName = Environment.UserName,
        MachineName = Environment.MachineName,
        ServerHost = settings.ServerMap.Host,
        SharedKey = settings.ServerMap.SharedKey,
        Thumbprint = settings.ServerMap.PinnedThumbprint,
    };

    private static string? Layout(string install) =>
        SptInstallationService.TryGetServerRoot(install, out var root)
            ? root.Length == 0 ? "root" : root
            : null;

    private static string? Build()
    {
        var informational = typeof(ProblemFacts).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        var plus = informational?.IndexOf('+') ?? -1;
        if (plus < 0) return null;

        var build = informational![(plus + 1)..];
        return build.Length > BuildLength ? build[..BuildLength] : build;
    }

    private static IReadOnlyList<ProblemMod> ReadInstalledMods()
    {
        var install = AppServices.SptEnvironment.InstallPath;
        var stamp = string.IsNullOrWhiteSpace(install) ? null : InstallStamp.Of(install);

        return AppServices.InstallManifest.Load().Mods
            .Where(r => r.InstallPath is not { } stamped || stamp is null
                || string.Equals(stamped, stamp, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => new ProblemMod(
                r.Name,
                r.Version,
                r.ModId.ToString(),
                IsDisabled(r, install),
                r.IsAddon))
            .ToList();
    }

    private static bool IsDisabled(InstalledModRecord record, string? install)
    {
        if (record.DisabledWithParent || record.DisabledForFit) return true;
        if (string.IsNullOrWhiteSpace(install)) return false;

        var probe = record.Folders.FirstOrDefault() ?? record.Files.FirstOrDefault();
        if (probe is null) return false;

        if (Exists(install, probe)) return DisabledModPaths.IsRelativePathDisabled(probe);

        return DisabledModPaths.TryGetRelativeCounterpart(probe, out var other)
            && Exists(install, other)
            && DisabledModPaths.IsRelativePathDisabled(other);
    }

    private static bool Exists(string install, string relative)
    {
        var path = Path.Combine(install, relative.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(path) || File.Exists(path);
    }

    private static T Try<T>(string what, Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Report", $"couldn't read {what} for the problem report: {ex.Message}");
            return fallback;
        }
    }
}
