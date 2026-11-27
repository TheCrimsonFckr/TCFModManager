using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

/// <summary>A client mod the dry load check found something in, worded.</summary>
public sealed class ClientCheckRow(string title, string? modName, IReadOnlyList<string> lines)
{
    public string Title { get; } = title;

    public string? ModName { get; } = modName;

    public bool HasMod => ModName is not null;

    public string Lines { get; } = string.Join("\n", lines);
}

//
// OPEN-54 N27b: Check client mods - ClientLoadCheck over the install's client plugins, on demand.
//
public sealed partial class DiagnoseViewModel
{
    private const int LinesPerMod = 6;

    public ObservableCollection<ClientCheckRow> ClientChecks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotCheckingClients))]
    private bool _isCheckingClients;

    public bool IsNotCheckingClients => !IsCheckingClients;

    [ObservableProperty]
    private string? _clientCheckStatus;

    [ObservableProperty]
    private bool _hasClientChecks;

    [RelayCommand]
    private async Task CheckClientModsAsync()
    {
        if (IsCheckingClients) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            ClientCheckStatus = AppMessages.NoSptInstallFolder;
            return;
        }

        IsCheckingClients = true;
        ClientCheckStatus = Strings.ClientCheck_Checking;
        ClientChecks.Clear();
        HasClientChecks = false;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var checks = await Task.Run(() =>
            {
                var gameRoot = SptInstallationService.ToGameRoot(installPath) ?? installPath;
                return ClientLoadCheck.Run(gameRoot, installPath, InstalledScan.Run(installPath));
            });

            AppLog.Info("ClientCheck", $"{checks.Count} client mod(s) with findings in {clock.ElapsedMilliseconds}ms: "
                + string.Join("; ", checks.Select(c => $"{c.Mod?.Name ?? Path.GetFileName(c.File)} x{c.Findings.Count}")));

            foreach (var check in checks) ClientChecks.Add(Row(check));
            HasClientChecks = ClientChecks.Count > 0;

            ClientCheckStatus = checks.Count == 0
                ? Strings.ClientCheck_NothingFound
                : Strings.ClientCheck_Found(checks.Count, checks.Count);
        }
        catch (Exception ex)
        {
            AppLog.Warn("ClientCheck", $"couldn't check the client mods: {ex}");
            ClientCheckStatus = ex.Message;
        }
        finally
        {
            IsCheckingClients = false;
        }
    }

    [RelayCommand]
    private static void ShowClientMod(ClientCheckRow? row)
    {
        if (row?.ModName is { } name) AppNavigation.ShowInInstalled(name);
    }

    private static ClientCheckRow Row(ClientModCheck check)
    {
        var lines = check.Findings.Select(Line).Distinct().ToList();
        if (lines.Count > LinesPerMod)
            lines = [.. lines.Take(LinesPerMod), Strings.ClientCheck_More(lines.Count - LinesPerMod, lines.Count - LinesPerMod)];

        var title = check.Mod is { } mod
            ? mod.DeclaredName is { Length: > 0 } declared ? declared : mod.Name
            : Path.GetFileName(check.File);

        return new ClientCheckRow(title, check.Mod?.Name, lines);
    }

    private static string Line(ClientLoadFinding f)
    {
        string T(string format, params object?[] values) => LocalizationService.Text(format, values);

        return f.Problem switch
        {
            ClientLoadProblem.MissingAssembly => T(Strings.ClientCheck_MissingAssemblyFormat, f.Assembly),
            ClientLoadProblem.MissingType => T(Strings.ClientCheck_MissingTypeFormat, f.Type, f.Assembly),
            ClientLoadProblem.MissingMember => T(Strings.ClientCheck_MissingMemberFormat, f.Type + "." + f.Member, f.Assembly),
            ClientLoadProblem.PatchTargetMissing => T(Strings.ClientCheck_PatchTargetFormat, f.Member is { } m ? f.Type + "." + m : f.Type),
            ClientLoadProblem.DependencyOff => T(Strings.ClientCheck_DependencyOffFormat, f.Member, f.Type),
            _ => T(Strings.ClientCheck_DependencyMissingFormat, f.Type),
        };
    }
}
