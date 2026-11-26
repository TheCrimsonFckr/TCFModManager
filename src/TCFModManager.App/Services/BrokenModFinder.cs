using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
using TCFModManager.App.Behaviors;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

// OPEN-54 N27: how the last search ended, kept in Data\brokenModResult.json until Done clears it.
public sealed class SavedSearchResult
{
    public string Title { get; set; } = "";

    public string? Body { get; set; }

    public string? Next { get; set; }

    public string? Lines { get; set; }

    public string? Note { get; set; }

    public List<string[]> Found { get; set; } = [];
}

public sealed partial class FoundMod(string name, string title, string? path) : ObservableObject
{
    public string Name { get; } = name;

    public string Title { get; } = title;

    public string? Path { get; } = path;

    public string ShowText => LocalizationService.Text(Strings.Search_ShowModFormat, Title);

    public string TurnOffText => LocalizationService.Text(Strings.Search_TurnOffFormat, Title);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTurnOff))]
    private bool _isOff = IsTurnedOff(path);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTurnOff))]
    private bool _isTurningOff;

    private static bool IsTurnedOff(string? path) =>
        path is not null && !Directory.Exists(path) && !File.Exists(path)
        && DisabledModPaths.Counterpart(path) is { } off && (Directory.Exists(off) || File.Exists(off));

    public bool CanTurnOff => Path is not null && !IsOff && !IsTurningOff;
}

//
// OPEN-54 N27: Find the broken mod on Diagnose logs - BrokenModSearch run against this install. One
// for the whole app, so a search goes on while other pages are open. Refused while SPT runs or
// downloads are queued; the queue is paused meanwhile; the profiles are copied first and put back
// if the starts changed them; every mod goes back on however it ends, at the next start if need be.
//
public sealed partial class BrokenModFinder : LocalizedViewModel
{
    private readonly BrokenModSessionStore _store = new();
    private CancellationTokenSource? _cancel;

    private readonly System.Diagnostics.Stopwatch _clock = new();
    private readonly List<(IReadOnlyList<int> On, TimeSpan Took)> _starts = [];
    private TimeSpan[]? _shares;
    private readonly System.Windows.Threading.DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private BrokenModProgress? _now;
    private TimeSpan _nowBegan;

    private static string ResultFile => Path.Combine(AppPaths.DataDirectory, "brokenModResult.json");

    public BrokenModFinder()
    {
        _tick.Tick += (_, _) =>
        {
            ElapsedText = T(Strings.Search_ElapsedFormat, Duration(_clock.Elapsed));
            SayProgress();
        };
        LoadResult();
    }

    public event EventHandler? Ended;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanStart))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isStopping;

    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private string? _elapsedText;

    [ObservableProperty]
    private string? _leftText;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultTitle;

    [ObservableProperty]
    private string? _resultBody;

    [ObservableProperty]
    private string? _resultNext;

    [ObservableProperty]
    private string? _resultLines;

    [ObservableProperty]
    private string? _resultNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultDone))]
    private string? _resultDone;

    public bool HasResultDone => ResultDone is not null;

    public ObservableCollection<FoundMod> Found { get; } = [];

    public bool HasResult => ResultTitle is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInterrupted), nameof(CanStart))]
    private string? _interruptedText;

    public bool IsInterrupted => InterruptedText is not null;

    public bool IsIdle => !IsRunning;

    public bool CanStart => !IsRunning && !IsInterrupted;

    private static string T(string format, params object?[] values) => LocalizationService.Text(format, values);

    private static string TitleOf(InstalledMod mod) => mod.DeclaredName is { Length: > 0 } declared ? declared : mod.Name;

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!CanStart) return;

        if (AppServices.SptEnvironment.InstallPath is not { Length: > 0 } install)
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        if (AppServices.DownloadQueue.Items.Any(i => i.Status is DownloadQueueItemStatus.Pending
                or DownloadQueueItemStatus.Downloading or DownloadQueueItemStatus.Installing))
        {
            StatusMessage = Strings.Search_WaitForDownloads;
            return;
        }

        if (ModInstallService.RunningBlockers(install) is { Count: > 0 } running)
        {
            StatusMessage = ModInstallProblems.InstallInUse(running, ModInstallAction.Disable);
            return;
        }

        var (scanned, mods, needs) = await Task.Run(() =>
        {
            var scan = InstalledScan.Run(install);
            var (suspects, needed) = BrokenModSearch.Suspects(scan);
            return (scan, suspects, needed);
        });
        if (mods.Count == 0)
        {
            StatusMessage = Strings.Search_NoServerMods;
            return;
        }

        var about = 2 + new ModBisect(mods.Count, needs).TriesLeft;
        if (AskWindow.Show(new AskRequest
            {
                Title = Strings.Search_Title,
                Lead = T(Strings.Search_ConfirmFormat, mods.Count, about),
                First = Strings.Search_Start,
            }) != AskChoice.First)
        {
            return;
        }

        ClearResult();
        StatusMessage = null;
        IsRunning = true;
        _clock.Restart();
        _starts.Clear();
        _shares = null;
        _now = null;
        ElapsedText = T(Strings.Search_ElapsedFormat, Duration(TimeSpan.Zero));
        _tick.Start();
        Progress = 0;
        ProgressText = Strings.Search_Preparing;

        var cancel = _cancel = new CancellationTokenSource();
        var queueWasPaused = AppServices.DownloadQueue.IsPaused;
        AppServices.DownloadQueue.IsPaused = true;

        var session = new BrokenModSession
        {
            InstallPath = install,
            StartedAt = DateTimeOffset.Now,
            Mods = [.. mods.Select(m => m.FolderPath)],
        };

        BrokenModResult? result = null;
        string? problem = null;
        var stopped = false;
        ProfileBackup? profiles = null;

        try
        {
            profiles = await Task.Run(() =>
                AppServices.ProfileBackups.EnsureBackupBefore(install, ProfileBackups.BeforeSearch)
                    ? AppServices.ProfileBackups.List(install).FirstOrDefault() ?? NoProfiles
                    : null);

            if (profiles is null)
            {
                problem = Strings.Search_BackupFailed;
            }
            else
            {
                session.Logs = ServerTrial.LogLengths(install);
                _store.Save(session);
                AppLog.Info("Search", $"searching {mods.Count} server mod(s) of {install}");

                var progress = new Progress<BrokenModProgress>(p =>
                {
                    if (!_tick.IsEnabled && _now is not null) return;

                    if (_now is { } was) _starts.Add((was.On, _clock.Elapsed - _nowBegan));
                    _now = p;
                    _nowBegan = _clock.Elapsed;
                    _tick.Start();

                    Progress = 100.0 * (p.Try - 1) / Math.Max(p.About, p.Try);
                    SayProgress();
                });

                var recent = await Task.Run(() => new ServerUpLog().Last(install) is { } up
                    ? BrokenModSearch.ChangedSince(session.Mods, up)
                    : null);
                if (recent is not null) AppLog.Info("Search", $"{recent.Count} mod(s) changed since the server was last up");

                var locator = await Task.Run(() => LogModLocator.Build(scanned, install));
                var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < session.Mods.Count; i++) indexOf.TryAdd(session.Mods[i], i);
                int? ModOf(string source) => locator.ByCode(source) is { } mod && indexOf.TryGetValue(mod.FolderPath, out var i) ? i : null;

                result = await Task.Run(() => BrokenModSearch.RunAsync(mods.Count, needs, async (on, errors, ct) =>
                {
                    var wanted = on.Select(i => session.Mods[i]).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var moved = BrokenModSessionStore.Set(session, wanted);
                    if (moved.Failed.Count > 0) throw new SearchFailed(Failures(moved.Failed));

                    var outcome = await ServerTrial.RunAsync(install, ct, settle: errors).ConfigureAwait(false);
                    if (_shares is null && on.Count == mods.Count) _shares = Shares(outcome, ModOf, mods);
                    AppLog.Info("Search", $"{on.Count} on: {(outcome.Started ? "started" : outcome.TimedOut ? "timed out" : "did not start")}, {outcome.Errors.Count} error(s)");
                    return outcome;
                }, progress, cancel.Token, recent));
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        catch (ServerTrialException ex)
        {
            problem = Describe(ex);
        }
        catch (ModInstallException ex)
        {
            problem = ModInstallProblems.Describe(ex);
        }
        catch (SearchFailed ex)
        {
            problem = ex.Message;
        }
        catch (Exception ex)
        {
            AppLog.Error("Search", $"the search failed: {ex}");
            problem = ex.Message;
        }

        _tick.Stop();
        ProgressText = Strings.Search_PuttingBack;
        LeftText = null;
        Progress = 100;

        var back = await PutBackAsync(session, profiles == NoProfiles ? null : profiles, trimLogs: true);

        AppServices.DownloadQueue.IsPaused = queueWasPaused;
        _cancel = null;
        cancel.Dispose();
        IsStopping = false;
        IsRunning = false;
        ProgressText = null;
        ElapsedText = null;
        LeftText = null;

        if (problem is not null)
        {
            AppLog.Warn("Search", $"couldn't finish: {problem}");
            StatusMessage = Sentences(T(Strings.Search_FailedFormat, problem), back.AllBack ? Strings.Search_AllBack : null);
        }
        else if (stopped)
        {
            StatusMessage = back.AllBack ? Sentences(Strings.Search_Stopped, Narrowed(mods)) : null;
        }
        else if (result is not null)
        {
            Show(result, mods, back, _clock.Elapsed);
            SaveResult();

            if (Application.Current?.MainWindow is not { IsActive: true })
                UpdateToasts.ShowMessage("search", Strings.Search_Title, Sentences(ResultTitle, ResultBody));
        }

        Ended?.Invoke(this, EventArgs.Empty);

        if (_closeWhenStopped)
        {
            _closeWhenStopped = false;
            AppTray.Quit();
        }
    }

    private string? Narrowed(IReadOnlyList<InstalledMod> mods)
    {
        if (_now?.Suspects is not { Count: > 0 } suspects || suspects.Count >= mods.Count) return null;

        var names = suspects.Where(i => i >= 0 && i < mods.Count).Select(i => TitleOf(mods[i])).ToList();
        AppLog.Info("Search", $"stopped with {names.Count} suspect(s): {string.Join(", ", names)}");

        return names.Count <= 8
            ? T(Strings.Search_StoppedNarrowedFormat, names.Count, mods.Count, TextLists.Join(names))
            : T(Strings.Search_StoppedNarrowedCountFormat, names.Count, mods.Count);
    }

    private static TimeSpan[] Shares(ServerTrialOutcome outcome, Func<string, int?> modOf, IReadOnlyList<InstalledMod> mods)
    {
        var shares = ModLoadShares.From(outcome.Lines, modOf, mods.Count);
        AppLog.Info("Search", "load shares: " + string.Join(", ", shares
            .Select((took, i) => (took, i)).Where(s => s.took > TimeSpan.Zero).OrderByDescending(s => s.took).Take(8)
            .Select(s => $"{TitleOf(mods[s.i])} {s.took.TotalSeconds:0.0}s")));
        return shares;
    }

    private void SayProgress()
    {
        if (_now is not { } p || IsStopping || !_tick.IsEnabled) return;

        var left = BrokenModSearch.TimeLeft(_starts, _shares, p.On, p.About - p.Try + 1, _clock.Elapsed - _nowBegan);
        ProgressText = T(Strings.Search_ProgressFormat, p.Try, p.About, p.ModsOn, p.Mods);
        LeftText = left is not { } time ? null
            : time.TotalSeconds < 60 ? Strings.Search_LeftUnderMinute
            : T(Strings.Search_LeftMinutesFormat, (int)Math.Ceiling(time.TotalMinutes));
    }

    private static string Duration(TimeSpan took) => took.TotalMinutes >= 1
        ? T(Strings.Search_DurationMinutesFormat, (int)took.TotalMinutes, took.Seconds)
        : T(Strings.Search_DurationSecondsFormat, Math.Max(1, (int)Math.Round(took.TotalSeconds)));

    private static readonly ProfileBackup NoProfiles = new("", default, "", 0, 0);

    private sealed class SearchFailed(string message) : Exception(message);

    [RelayCommand]
    private void Stop()
    {
        if (!IsRunning || IsStopping) return;

        IsStopping = true;
        ProgressText = Strings.Search_Stopping;
        LeftText = null;
        _cancel?.Cancel();
    }

    [RelayCommand]
    private void Dismiss() => ClearResult();

    [RelayCommand]
    private void ShowMod(FoundMod? mod)
    {
        if (mod is not null) AppNavigation.ShowInInstalled(mod.Name);
    }

    [RelayCommand]
    private async Task TurnOffModAsync(FoundMod? mod)
    {
        if (IsRunning || mod is not { Path: { } path, IsOff: false, IsTurningOff: false }) return;

        var install = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(install))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        if (!await SptInUsePrompt.ClearAsync(install, ModInstallAction.Disable)) return;

        mod.IsTurningOff = true;
        try
        {
            await TurnOffAsync(mod, path, install);
        }
        finally
        {
            mod.IsTurningOff = false;
        }
    }

    private async Task TurnOffAsync(FoundMod mod, string path, string install)
    {
        var scanned = await Task.Run(() => InstalledScan.Run(install));
        var target = scanned.FirstOrDefault(m => !m.IsDisabled && string.Equals(m.FolderPath, path, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            mod.IsOff = true;
            SayTurnedOff();
            return;
        }

        var targets = new List<InstalledMod> { target };
        var impact = new List<ModDisableImpactRow>();
        foreach (var link in ModDependencyGraph.Build(scanned).DisableImpact([target]))
        {
            if (link.Dependent.IsDisabled || targets.Contains(link.Dependent) || impact.Any(i => i.Name == TitleOf(link.Dependent))) continue;
            var detail = T(link.IsSoft ? Strings.Installed_ImpactOptionallyUsesFormat : Strings.Installed_ImpactNeedsFormat, TitleOf(link.Dependency));
            impact.Add(new ModDisableImpactRow(TitleOf(link.Dependent), detail, link.IsSoft));
        }

        var cascade = new List<InstalledMod>();
        if (impact.Count > 0)
        {
            switch (ModDisableConfirmationWindow.Confirm(true, [mod.Title], impact))
            {
                case ModDisableChoice.Cancel:
                    return;
                case ModDisableChoice.ProceedWithCascade:
                    cascade.AddRange(ModDependencyGraph.Build(scanned).DisableImpact([target])
                        .Where(l => !l.IsSoft && !l.Dependent.IsDisabled).Select(l => l.Dependent).Distinct());
                    break;
            }
        }

        ModDisableOutcome outcome;
        try
        {
            outcome = await Task.Run(() =>
            {
                AppServices.ProfileBackups.BackupIfChanged(install, ProfileBackups.BeforeDisable);
                return ModDisableService.Apply([target, .. cascade], true, install);
            });
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
            return;
        }

        if (outcome.Failed.Count > 0)
        {
            StatusMessage = Failures(outcome.Failed);
            return;
        }

        AppLog.Info("Search", $"turned off {mod.Name} from the result");
        StatusMessage = null;
        mod.IsOff = true;
        SayTurnedOff();
    }

    private void SayTurnedOff()
    {
        var off = Found.Where(f => f.IsOff).Select(f => f.Title).ToList();
        ResultDone = off.Count > 0 ? T(Strings.Search_TurnedOffFormat, TextLists.Join(off)) : null;
    }

    [RelayCommand]
    private static void StartServer()
    {
        AppNavigation.ShowServer();
        if (AppServices.Play.StartServerCommand.CanExecute(null)) AppServices.Play.StartServerCommand.Execute(null);
    }

    public void OpenAndStart()
    {
        AppNavigation.Navigate(typeof(DiagnosePage));
        Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            () => { if (CanStart) StartCommand.Execute(null); });
    }

    private void ClearResult()
    {
        ResultTitle = null;
        ResultBody = null;
        ResultNext = null;
        ResultLines = null;
        ResultNote = null;
        ResultDone = null;
        Found.Clear();

        try
        {
            File.Delete(ResultFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Search", $"couldn't clear the kept result: {ex.Message}");
        }
    }

    private void SaveResult()
    {
        if (ResultTitle is null) return;

        try
        {
            SafeFile.WriteText(ResultFile, JsonSerializer.Serialize(new SavedSearchResult
            {
                Title = ResultTitle,
                Body = ResultBody,
                Next = ResultNext,
                Lines = ResultLines,
                Note = ResultNote,
                Found = [.. Found.Select(f => new[] { f.Name, f.Title, f.Path ?? string.Empty })],
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Search", $"couldn't keep the result: {ex.Message}");
        }
    }

    private void LoadResult()
    {
        try
        {
            if (!File.Exists(ResultFile)) return;
            if (JsonSerializer.Deserialize<SavedSearchResult>(File.ReadAllText(ResultFile)) is not { Title.Length: > 0 } saved) return;

            ResultTitle = saved.Title;
            ResultBody = saved.Body;
            ResultNext = saved.Next;
            ResultLines = saved.Lines;
            ResultNote = saved.Note;
            foreach (var mod in saved.Found.Where(f => f is { Length: >= 2 }))
                Found.Add(new FoundMod(mod[0], mod[1], mod.Length > 2 && mod[2].Length > 0 ? mod[2] : null));

            SayTurnedOff();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Debug("Search", $"couldn't read the kept result: {ex.Message}");
        }
    }

    private sealed record PutBack(bool AllBack, bool ProfilesRestored);

    // Every mod back on, and the profiles as they were if the starts changed them. trimLogs: whether
    // what the server logged since the search began is the search's own.
    private async Task<PutBack> PutBackAsync(BrokenModSession session, ProfileBackup? profiles, bool trimLogs)
    {
        var (problem, restored) = await Task.Run(async () =>
        {
            try
            {
                await ServerTrial.StopAsync(session.InstallPath).ConfigureAwait(false);

                var outcome = BrokenModSessionStore.PutBack(session);
                if (outcome.Failed.Count > 0) return (Failures(outcome.Failed), false);

                if (trimLogs && session.Logs is { } logs) ServerTrial.TrimLogs(session.InstallPath, logs);

                var changed = profiles is not null
                    && AppServices.ProfileBackups.BackupIfChanged(session.InstallPath, ProfileBackups.BeforeSearch) is not null;
                if (changed)
                {
                    AppServices.ProfileBackups.Restore(profiles!, session.InstallPath);
                    AppLog.Info("Search", "the starts changed the profiles; put back as they were");
                }

                return ((string?)null, changed);
            }
            catch (ServerTrialException ex)
            {
                return (Describe(ex), false);
            }
            catch (ModInstallException ex)
            {
                return (ModInstallProblems.Describe(ex), false);
            }
            catch (Exception ex)
            {
                AppLog.Error("Search", $"putting back failed: {ex}");
                return (ex.Message, false);
            }
        });

        var left = BrokenModSessionStore.TurnedOff(session);
        if (left == 0)
        {
            _store.Clear();
            InterruptedText = null;
            if (problem is not null) StatusMessage = problem;
            return new PutBack(true, restored);
        }

        AppLog.Warn("Search", $"{left} mod(s) still off: {problem}");
        InterruptedText = Sentences(T(Strings.Search_InterruptedFormat, left), problem);
        return new PutBack(false, restored);
    }

    private void Show(BrokenModResult result, IReadOnlyList<InstalledMod> mods, PutBack back, TimeSpan took)
    {
        var down = result.Symptom == BrokenModSymptom.ServerDoesNotStart;

        switch (result.Verdict)
        {
            case BrokenModVerdict.NothingWrong:
                ResultTitle = Strings.Search_NothingWrongTitle;
                ResultBody = Strings.Search_NothingWrongBody;
                break;

            case BrokenModVerdict.NotTheMods:
                ResultTitle = Strings.Search_NotTheModsTitle;
                ResultBody = down ? Strings.Search_NotTheModsDownBody : Strings.Search_NotTheModsErrorsBody;
                break;

            default:
                var culprit = mods[result.Culprit!.Value];
                Found.Add(new FoundMod(culprit.Name, TitleOf(culprit), culprit.FolderPath));

                if (result.Partner is { } other)
                {
                    var partner = mods[other];
                    Found.Add(new FoundMod(partner.Name, TitleOf(partner), partner.FolderPath));
                    ResultTitle = T(Strings.Search_FoundPairTitleFormat, TitleOf(culprit), TitleOf(partner));
                    ResultBody = T(down ? Strings.Search_FoundPairDownFormat : Strings.Search_FoundPairErrorsFormat, TitleOf(culprit), TitleOf(partner));
                    ResultNext = Strings.Search_WhatToDoPair;
                }
                else
                {
                    ResultTitle = T(Strings.Search_FoundTitleFormat, TitleOf(culprit));
                    ResultBody = T(down ? Strings.Search_FoundDownFormat : Strings.Search_FoundErrorsFormat, TitleOf(culprit));
                    ResultNext = Strings.Search_WhatToDo;
                }

                break;
        }

        var lines = result.Errors.Take(4)
            .Select(e => string.Join("\n", new[] { "[" + e.Source + "] " + e.Message }.Concat(e.More.Take(2))))
            .ToList();
        ResultLines = lines.Count > 0 ? string.Join("\n", lines) : null;

        ResultNote = Sentences(
            T(Strings.Search_SummaryFormat, mods.Count, result.Tries, Duration(took)),
            result.RecentTried > 0 ? T(Strings.Search_RecentFirstFormat, result.RecentTried) : null,
            back.AllBack ? Strings.Search_AllBack : null,
            back.ProfilesRestored ? Strings.Search_ProfilesPutBack : null);

        AppLog.Info("Search", $"{result.Verdict} after {result.Tries} start(s) in {took.TotalSeconds:0} s: {ResultTitle}");
    }

    // At startup: a search the last run didn't finish is put back now, unless SPT from that install runs.
    public async Task RecoverAsync()
    {
        if (IsRunning || _store.Load() is not { } session) return;

        var left = BrokenModSessionStore.TurnedOff(session);
        if (left == 0)
        {
            _store.Clear();
            return;
        }

        AppLog.Info("Search", $"a search of {session.InstallPath} was cut short; {left} mod(s) off");

        if (ModInstallService.RunningBlockers(session.InstallPath).Count > 0)
        {
            InterruptedText = T(Strings.Search_InterruptedFormat, left);
            Tell(Sentences(InterruptedText, Strings.Search_InterruptedWhere));
            return;
        }

        var back = await PutBackAsync(session, null, trimLogs: true);
        Tell(back.AllBack ? T(Strings.Search_PutBackDoneFormat, left) : Sentences(InterruptedText, Strings.Search_InterruptedWhere));
    }

    [RelayCommand]
    private async Task TurnBackOnAsync()
    {
        if (IsRunning || _store.Load() is not { } session)
        {
            InterruptedText = null;
            return;
        }

        var running = ModInstallService.RunningBlockers(session.InstallPath);
        var serverOnly = running.Count > 0 && running.All(r => r.Contains("Server", StringComparison.OrdinalIgnoreCase));
        if (running.Count > 0 && !serverOnly)
        {
            StatusMessage = ModInstallProblems.InstallInUse(running, ModInstallAction.Enable);
            return;
        }

        if (serverOnly && AskWindow.Show(new AskRequest
            {
                Title = Strings.Search_Title,
                Lead = Strings.Search_StopServerFirst,
                First = Strings.Search_StopServerAndTurnOn,
            }) != AskChoice.First)
        {
            return;
        }

        var left = BrokenModSessionStore.TurnedOff(session);
        var back = await PutBackAsync(session, null, trimLogs: !serverOnly);
        if (back.AllBack) StatusMessage = T(Strings.Search_PutBackDoneFormat, left);

        Ended?.Invoke(this, EventArgs.Empty);
    }

    private bool _closeWhenStopped;

    // Closing mid-search asks; stopping ends after the current try, puts every mod back, then quits.
    public bool MayClose()
    {
        if (!IsRunning) return true;

        if (AskWindow.Show(new AskRequest
            {
                Title = Strings.Search_Title,
                Lead = Strings.Search_CloseAskLead,
                First = Strings.Search_CloseAskStop,
            }) == AskChoice.First)
        {
            _closeWhenStopped = true;
            Stop();
        }

        return false;
    }

    private static void Tell(string text) => AskWindow.Show(new AskRequest
    {
        Title = Strings.Search_Title,
        Lead = text,
        First = Strings.Common_Close,
        Cancel = null,
    });

    private static string Describe(ServerTrialException ex) => ex.Problem switch
    {
        ServerTrialProblem.AlreadyRunning => Strings.Search_ProblemAlreadyRunning,
        ServerTrialProblem.PortTaken => Strings.Search_ProblemPortTaken,
        ServerTrialProblem.CouldNotStop => Strings.Search_ProblemCouldNotStop,
        _ => T(Strings.Search_ProblemCouldNotStartFormat, ex.Message),
    };

    private static string Failures(IReadOnlyList<ModDisableFailure> failed) =>
        string.Join(" ", failed.Select(f => T(Strings.Search_MoveFailedFormat, f.ModName, f.Reason)));

    private static string Sentences(params string?[] parts) => string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
}
