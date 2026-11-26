using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Behaviors;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// OPEN-70: the web pages this PC's server mods declare (IModBlazorMetadata.HomePage), opened at
// this PC's own server address. Shared so the sidebar item, the page and the Options switch read
// one answer.
//
public sealed partial class WebPagesViewModel : LocalizedViewModel
{
    private bool _started;
    private int _scanVersion;
    private IReadOnlyList<InstalledMod> _mods = [];
    private string _address = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNavShown))]
    private bool _isPageEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNavShown))]
    [NotifyPropertyChangedFor(nameof(ServerStoppedLine))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private bool _isServerRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNavShown))]
    private bool _isOnPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNavShown))]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    private IReadOnlyList<WebPageRowViewModel> _rows = [];

    public bool HasRows => Rows.Count > 0;

    public bool IsNavShown => IsPageEnabled && HasRows && (IsServerRunning || IsOnPage);

    public string? ServerStoppedLine => IsServerRunning ? null : Strings.WebPages_ServerNotRunning;

    public void Start()
    {
        if (_started) return;
        _started = true;

        RefreshSetting();
        AppServices.Play.PropertyChanged += OnPlayChanged;
        IsServerRunning = AppServices.Play.IsServerRunning;
        if (IsServerRunning) _ = RescanAsync();
    }

    public void RefreshSetting() => IsPageEnabled = new SettingsService().Load().ShowWebPagesPage;

    private void OnPlayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayViewModel.IsServerRunning))
            IsServerRunning = AppServices.Play.IsServerRunning && !AppServices.BrokenModFinder.IsRunning;
    }

    partial void OnIsServerRunningChanged(bool value)
    {
        if (value) _ = RescanAsync();
    }

    [RelayCommand]
    public async Task RescanAsync()
    {
        var version = ++_scanVersion;
        var installPath = AppServices.SptEnvironment.InstallPath;
        var serverExe = AppServices.Play.Server?.ExePath;

        try
        {
            var (mods, address) = await Task.Run(() =>
            {
                var found = InstalledScan.Run(installPath)
                    .Where(m => m is { Target: InstalledModTarget.Server, IsDisabled: false, HomePage: not null })
                    .ToList();
                return (found, SptDirectLaunch.ServerAddress(serverExe));
            });

            if (version != _scanVersion) return;

            _mods = mods;
            _address = address;
            Rows = BuildRows();
            AppLog.Debug("WebPages", $"{mods.Count} mod web page(s) at {address}");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("WebPages", $"scan failed: {ex.Message}");
        }
    }

    private IReadOnlyList<WebPageRowViewModel> BuildRows()
    {
        if (_mods.Count == 0) return [];

        var rows = new List<WebPageRowViewModel>
        {
            new(Strings.WebPages_SptPanel, Strings.WebPages_SptPanelDescription, $"https://{_address}/"),
        };

        rows.AddRange(_mods
            .Select(m => new WebPageRowViewModel(m.DeclaredName ?? m.Name, m.HomePageDescription, $"https://{_address}{m.HomePage}"))
            .OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase));

        return rows;
    }

    protected internal override void RefreshText()
    {
        Rows = BuildRows();
        base.RefreshText();
    }

    private bool CanOpen(WebPageRowViewModel? row) => IsServerRunning;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open(WebPageRowViewModel? row)
    {
        if (row is null) return;

        AppLog.Info("WebPages", $"opening {row.Url}");
        MarkupActions.OpenInBrowser(row.Url);
    }

    [RelayCommand]
    private void CopyAddress(WebPageRowViewModel? row) => CopyText.Copy(row?.Url);
}

public sealed record WebPageRowViewModel(string Title, string? Description, string Url)
{
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}
