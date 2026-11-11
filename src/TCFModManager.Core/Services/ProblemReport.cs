using System.Runtime.InteropServices;
using System.Text;

namespace TCFModManager.Core.Services;

public enum ProblemSource
{
    Help,
    Crash,
    PreviousCrash,
    Download,
    AppUpdate,
    ModList,
}

public sealed record ProblemSetup
{
    public required string AppVersion { get; init; }

    public string? AppBuild { get; init; }

    public bool SingleFile { get; init; }

    public string? UiLanguage { get; init; }

    public string? SptVersion { get; init; }

    public string? SptLayout { get; init; }

    public bool? Headless { get; init; }

    public string? SptInstallPath { get; init; }

    public Version WindowsVersion { get; init; } = Environment.OSVersion.Version;

    public bool Is64Bit { get; init; } = Environment.Is64BitOperatingSystem;

    public string Runtime { get; init; } = RuntimeInformation.FrameworkDescription;

    public bool MonitorMode { get; init; }

    public bool ModPageGate { get; init; }

    public bool ServerMapConnected { get; init; }
}

public sealed record ProblemMod(string Name, string? Version, string? ModId, bool Disabled, bool IsAddon = false);

public sealed record ProblemContext
{
    public required ProblemSource Source { get; init; }

    public required ProblemSetup Setup { get; init; }

    public Exception? Exception { get; init; }

    public string? ExceptionText { get; init; }

    public ModListApplyResult? ListResult { get; init; }

    public string? Shown { get; init; }

    public IReadOnlyList<ProblemMod>? InstalledMods { get; init; }

    public string? Log { get; init; }

    public RedactionValues Redaction { get; init; } = new();
}

public sealed record ProblemReport
{
    public required string Title { get; init; }

    public required string VersionsLine { get; init; }

    public required string LogsText { get; init; }

    public string? Shown { get; init; }

    public string? Details { get; init; }

    public string Body(string? pasteLink)
    {
        var body = new StringBuilder()
            .Append("**Steps to reproduce**\n1. \n\n")
            .Append("**What you expected**\n\n\n")
            .Append("**What happened**\n");

        if (Title.Length > 0) body.Append(Title).Append('\n');

        if (Shown is { Length: > 0 })
        {
            foreach (var line in Shown.Split('\n')) body.Append("> ").Append(line).Append('\n');
        }

        if (Details is { Length: > 0 }) body.Append('\n').Append(Fenced(Details));

        body.Append("\n**Logs**\n").Append(VersionsLine).Append('\n');
        if (pasteLink is { Length: > 0 }) body.Append(pasteLink).Append('\n');
        else body.Append(Fenced(LogsText));

        return body.ToString();
    }

    public static ProblemReport Build(ProblemContext context)
    {
        string Redact(string text) => LogRedactor.Redact(Normalise(text), context.Redaction);

        var details = DetailsText(context);

        return new ProblemReport
        {
            Title = Redact(TitleText(context)),
            VersionsLine = Redact(VersionsText(context.Setup)),
            Shown = context.Shown is { Length: > 0 } shown ? Redact(shown.Trim()) : null,
            Details = details.Length > 0 ? Redact(details) : null,
            LogsText = Redact(LogsTextFor(context)),
        };
    }

    internal static string Fenced(string content)
    {
        var longest = 0;
        var run = 0;
        foreach (var c in content)
        {
            run = c == '`' ? run + 1 : 0;
            if (run > longest) longest = run;
        }

        var fence = new string('`', Math.Max(3, longest + 1));
        return $"{fence}text\n{content.TrimEnd('\n')}\n{fence}\n";
    }

    private static string TitleText(ProblemContext context) => context.Source switch
    {
        ProblemSource.Help => "",
        ProblemSource.Crash => $"Unhandled exception: {TypeName(context)}",
        ProblemSource.PreviousCrash => $"Closed unexpectedly: {TypeName(context)}",
        ProblemSource.Download => $"Install failed: {ReasonName(context.Exception) ?? TypeName(context)}",
        ProblemSource.AppUpdate => $"App update failed: {ReasonName(context.Exception) ?? TypeName(context)}",
        ProblemSource.ModList => context.ListResult?.Stopped is { } stop
            ? $"Mod list apply stopped: {stop}"
            : $"Mod list apply failed: {TypeName(context)}",
        _ => context.Source.ToString(),
    };

    private static string? ReasonName(Exception? exception) => exception switch
    {
        ModInstallException install => install.Reason.ToString(),
        AppUpdateException update => update.Reason.ToString(),
        _ => null,
    };

    private static string TypeName(ProblemContext context)
    {
        if (context.Exception is { } exception) return exception.GetType().Name;

        var first = context.ExceptionText?.TrimStart().Split('\n', 2)[0] ?? "";
        var colon = first.IndexOf(':');
        var type = (colon >= 0 ? first[..colon] : first).Trim();
        var dot = type.LastIndexOf('.');
        type = dot >= 0 ? type[(dot + 1)..] : type;
        return type.Length > 0 && !type.Contains(' ') ? type : "unknown";
    }

    private static string VersionsText(ProblemSetup setup) =>
        $"{SelfMod.Name} {setup.AppVersion} · SPT {setup.SptVersion ?? "not found"} · Windows {WindowsText(setup.WindowsVersion)}";

    private static string DetailsText(ProblemContext context)
    {
        var text = new StringBuilder();

        if (context.ListResult?.Stopped is { } stop)
        {
            text.Append("Reason: ModListStop.").Append(stop).Append('\n');
            Value(text, "Running", Join(context.ListResult.Running));
            if (stop == ModListStop.FetchFailed) Value(text, "FailedFetches", context.ListResult.FailedFetches);
            if (context.ListResult.Refusal is { } refusal) Reason(text, refusal, "  ");
        }

        if (context.Exception is { } exception) Reason(text, exception, "");

        var trace = context.Exception?.ToString()
            ?? context.ListResult?.Refusal?.ToString()
            ?? context.ExceptionText;

        if (trace is { Length: > 0 })
        {
            if (text.Length > 0) text.Append('\n');
            text.Append("Exception:\n").Append(Normalise(trace).TrimEnd('\n')).Append('\n');
        }

        return text.ToString();
    }

    private static void Reason(StringBuilder text, Exception exception, string indent)
    {
        switch (exception)
        {
            case ModInstallException install:
                text.Append(indent).Append("Reason: ModInstallFailure.").Append(install.Reason).Append('\n');
                if (install.Reason == ModInstallFailure.InstallInUse) Value(text, "Action", install.Action, indent);
                Value(text, "Running", Join(install.Running), indent);
                Value(text, "ModName", install.ModName, indent);
                Value(text, "Version", install.Version, indent);
                Value(text, "PlacedFiles", install.PlacedFiles, indent);
                Value(text, "TotalFiles", install.TotalFiles, indent);
                Value(text, "ArchiveEntry", install.ArchiveEntry, indent);
                Value(text, "ExpectedBytes", install.ExpectedBytes, indent);
                Value(text, "ReceivedBytes", install.ReceivedBytes, indent);
                Value(text, "Folder", install.Folder, indent);
                Value(text, "Refusal", install.Refusal, indent);
                break;

            case AppUpdateException update:
                text.Append(indent).Append("Reason: AppUpdateFailure.").Append(update.Reason).Append('\n');
                Value(text, "Version", update.Version, indent);
                Value(text, "ExeName", update.ExeName, indent);
                Value(text, "Folder", update.Folder, indent);
                Value(text, "DriveName", update.DriveName, indent);
                Value(text, "StagedExeBytes", update.StagedExeBytes, indent);
                Value(text, "RequiredBytes", update.RequiredBytes, indent);
                Value(text, "AvailableBytes", update.AvailableBytes, indent);
                break;
        }
    }

    private static void Value(StringBuilder text, string name, object? value, string indent = "")
    {
        if (value is null || value is string { Length: 0 }) return;
        text.Append(indent).Append("  ").Append(name).Append(": ").Append(value).Append('\n');
    }

    private static string? Join(IReadOnlyList<string> values) => values.Count > 0 ? string.Join(", ", values) : null;

    private static string LogsTextFor(ProblemContext context)
    {
        var setup = context.Setup;
        var text = new StringBuilder();

        text.Append("App\n");
        Value(text, "Version", setup.AppBuild is { Length: > 0 } build ? $"{setup.AppVersion} ({build})" : setup.AppVersion);
        Value(text, "Single file", YesNo(setup.SingleFile));
        Value(text, "Language", setup.UiLanguage);

        text.Append("SPT\n");
        Value(text, "Version", setup.SptVersion ?? "not found");
        Value(text, "Layout", setup.SptLayout);
        Value(text, "Headless", setup.Headless is { } headless ? YesNo(headless) : null);
        Value(text, "Install", setup.SptInstallPath);

        text.Append("Windows\n");
        Value(text, "Version", WindowsText(setup.WindowsVersion));
        Value(text, "64-bit", YesNo(setup.Is64Bit));
        Value(text, ".NET", setup.Runtime);

        text.Append("Settings\n");
        Value(text, "Monitor mode", OnOff(setup.MonitorMode));
        Value(text, "Mod page gate", OnOff(setup.ModPageGate));
        Value(text, "Server Map", setup.ServerMapConnected ? "connected" : "not connected");

        if (context.InstalledMods is { } mods)
        {
            text.Append("Installed mods (").Append(mods.Count).Append(")\n");
            foreach (var mod in mods)
            {
                text.Append("  ").Append(mod.Name);
                if (mod.Version is { Length: > 0 }) text.Append(' ').Append(mod.Version);
                if (mod.ModId is { Length: > 0 }) text.Append(mod.IsAddon ? " (sp-mod addon " : " (sp-mod ").Append(mod.ModId).Append(')');
                if (mod.Disabled) text.Append(" disabled");
                text.Append('\n');
            }
        }

        if (context.Log is { Length: > 0 } log)
            text.Append("Log\n").Append(Normalise(log).TrimEnd('\n')).Append('\n');

        return text.ToString();
    }

    private static string WindowsText(Version version) => version.ToString(version.Build >= 0 ? 3 : 2);

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string Normalise(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
