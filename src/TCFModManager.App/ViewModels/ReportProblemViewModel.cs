using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Help;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public sealed partial class ReportProblemViewModel : LocalizedViewModel
{
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(30);

    private readonly ProblemContext _context;
    private IReadOnlyList<ProblemMod>? _mods;
    private ProblemReport _report;
    private string? _link;
    private string? _linkFor;

    public ReportProblemViewModel(ProblemContext context)
    {
        _context = context;
        _report = ProblemReport.Build(context);
    }

    [ObservableProperty]
    private bool _includeMods;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenIssuesCommand))]
    private bool _isUploading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasError;

    public string WindowTitle => _context.Source == ProblemSource.Crash ? Strings.App_CrashTitle : Strings.ReportProblem_Title;

    public string? Warning => _context.Source == ProblemSource.Crash ? Strings.ReportProblem_CrashWarning : null;

    public string? WarningDetail => _context.Source == ProblemSource.Crash ? _context.Exception?.Message : null;

    public string ReportTitle => _report.Title;

    public bool HasTitle => _report.Title.Length > 0;

    public string Text => _report.Body(null);

    public string StepOne => LocalizationService.Text(Strings.ReportProblem_Step1Format, AppVersion.Current);

    public string StepTwo => HasTitle ? Strings.ReportProblem_Step2 : Strings.ReportProblem_Step2Own;

    partial void OnIncludeModsChanged(bool value)
    {
        if (value) _mods ??= ProblemFacts.InstalledMods();

        _report = ProblemReport.Build(_context with { InstalledMods = value ? _mods : null });
        OnPropertyChanged(nameof(Text));
    }

    [RelayCommand]
    private void CopyTitle() => Copy(_report.Title, Strings.ReportProblem_TitleCopied);

    [RelayCommand]
    private void CopyReport() => Copy(_report.Body(null), Strings.ReportProblem_Copied);

    private bool CanOpenIssues() => !IsUploading;

    [RelayCommand(CanExecute = nameof(CanOpenIssues))]
    private async Task OpenIssuesAsync()
    {
        IsUploading = true;
        HasError = false;
        StatusMessage = Strings.ReportProblem_Uploading;

        try
        {
            var logs = _report.LogsText;
            string? failure = null;

            if (_linkFor != logs)
            {
                using var http = new HttpClient { Timeout = UploadTimeout };
                using var client = new CodePasteClient(http);
                var result = await client.UploadAsync(logs);

                if (result.Succeeded)
                {
                    _link = result.Link;
                    _linkFor = logs;
                    AppLog.Info("Report", "log uploaded to codepaste");
                }
                else
                {
                    failure = ProblemReportProblems.Describe(result);
                    AppLog.Warn("Report", $"codepaste upload failed: {result.Failure} {result.ServerMessage}");
                }
            }

            var body = _linkFor == logs ? _report.Body(_link) : _report.Body(null);
            if (!Copy(body, failure ?? Strings.ReportProblem_Opened)) return;

            HasError = failure is not null;
            MarkupActions.OpenInBrowser(HelpCatalog.IssuesUrl);
        }
        finally
        {
            IsUploading = false;
        }
    }

    private bool Copy(string text, string done)
    {
        try
        {
            Clipboard.SetText(text.Replace("\n", Environment.NewLine));
            HasError = false;
            StatusMessage = done;
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            HasError = true;
            StatusMessage = ex.Message;
            return false;
        }
    }
}
