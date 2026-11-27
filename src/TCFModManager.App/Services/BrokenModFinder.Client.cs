using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

public enum ClientRoundAnswer
{
    Broke,
    Worked,
    NotSure,
}

//
// OPEN-54 N27c: Find the client mod causing it. The same search over client mods - only their
// client half is switched, and the server keeps running - where each try is the game started as
// Quick play starts it and answered by the user on the card.
//
public sealed partial class BrokenModFinder
{
    private TaskCompletionSource<ClientRoundAnswer>? _answer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description), nameof(ShowsStart), nameof(ShowsStartServer))]
    private bool _isClientSearch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    private string? _askText;

    public bool IsAsking => AskText is not null;

    public string Description => IsClientSearch ? Strings.Search_ClientDescription : Strings.Search_Description;

    public bool ShowsStart => CanStart && !IsClientSearch;

    public bool ShowsStartServer => !IsClientSearch;

    [RelayCommand]
    private Task StartClientAsync() => RunSearchAsync(client: true);

    public void OpenAndStartClient()
    {
        AppNavigation.Navigate(typeof(DiagnosePage));
        Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            () => { if (CanStart) StartClientCommand.Execute(null); });
    }

    [RelayCommand]
    private void AnswerBroke() => _answer?.TrySetResult(ClientRoundAnswer.Broke);

    [RelayCommand]
    private void AnswerWorked() => _answer?.TrySetResult(ClientRoundAnswer.Worked);

    [RelayCommand]
    private void AnswerNotSure() => _answer?.TrySetResult(ClientRoundAnswer.NotSure);

    // On the UI thread: starts the game, waits for the answer, then closes the game. Not sure starts
    // it again with the same mods. True when the problem showed.
    private async Task<bool> RoundAsync(string install, int on, CancellationToken ct)
    {
        while (true)
        {
            var answer = await AskRoundAsync(install, ct);
            AppLog.Info("Search", $"{on} client mod(s) on: answered {answer}");
            if (answer != ClientRoundAnswer.NotSure) return answer == ClientRoundAnswer.Broke;
        }
    }

    private async Task<ClientRoundAnswer> AskRoundAsync(string install, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var spares = GameCloser.SparesWindowless(new SettingsService().Load(), install);
        if (await AppServices.Play.StartGameForSearchAsync() is { } problem) throw new SearchFailed(problem);

        var answer = _answer = new TaskCompletionSource<ClientRoundAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled = ct.Register(() => answer.TrySetCanceled());
        AskText = Strings.Search_AskRound;

        try
        {
            var seen = false;
            var told = false;
            while (!answer.Task.IsCompleted)
            {
                await Task.WhenAny(answer.Task, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
                if (answer.Task.IsCompleted) break;

                var running = await Task.Run(() => SptLaunchService.IsGameRunning(install, spares), CancellationToken.None);
                if (running)
                {
                    seen = true;
                }
                else if (seen && !told)
                {
                    told = true;
                    AskText = Strings.Search_GameClosedAsk;
                    if (Application.Current?.MainWindow is { } window) WindowActivation.BringForward(window);
                    UpdateToasts.ShowMessage("search", Strings.Search_Title, Strings.Search_GameClosedAsk);
                }
            }

            return await answer.Task;
        }
        finally
        {
            AskText = null;
            _answer = null;
            UpdateToasts.RemoveMessage("search");
            await Task.Run(() => CloseGame(install), CancellationToken.None);
        }
    }

    private static void CloseGame(string install)
    {
        var spares = GameCloser.SparesWindowless(new SettingsService().Load(), install);
        if (!SptLaunchService.IsGameRunning(install, spares)) return;

        var result = SptLaunchService.CloseGame(install, spares);
        if (result.Problem is not (SptLaunchProblem.None or SptLaunchProblem.NotRunning))
            AppLog.Warn("Search", $"the game could not be closed: {result.Problem}{(result.Error is null ? "" : " - " + result.Error.Message)}");
    }

    private void ShowClient(BrokenModResult result, IReadOnlyList<InstalledMod> mods, PutBack back, TimeSpan took)
    {
        switch (result.Verdict)
        {
            case BrokenModVerdict.NothingWrong:
                ResultTitle = Strings.Search_ClientNothingWrongTitle;
                ResultBody = Strings.Search_ClientNothingWrongBody;
                break;

            case BrokenModVerdict.NotTheMods:
                ResultTitle = Strings.Search_ClientNotTheModsTitle;
                ResultBody = Strings.Search_ClientNotTheModsBody;
                break;

            default:
                var culprit = mods[result.Culprit!.Value];
                Found.Add(new FoundMod(culprit.Name, TitleOf(culprit), culprit.FolderPath));

                if (result.Partner is { } other)
                {
                    var partner = mods[other];
                    Found.Add(new FoundMod(partner.Name, TitleOf(partner), partner.FolderPath));
                    ResultTitle = T(Strings.Search_FoundPairTitleFormat, TitleOf(culprit), TitleOf(partner));
                    ResultBody = T(Strings.Search_ClientFoundPairFormat, TitleOf(culprit), TitleOf(partner));
                    ResultNext = Strings.Search_WhatToDoPair;
                }
                else
                {
                    ResultTitle = T(Strings.Search_FoundTitleFormat, TitleOf(culprit));
                    ResultBody = T(Strings.Search_ClientFoundFormat, TitleOf(culprit));
                    ResultNext = Strings.Search_WhatToDo;
                }

                _foundAt = DateTimeOffset.Now;
                break;
        }

        ResultNote = Sentences(
            T(Strings.Search_ClientSummaryFormat, mods.Count, result.Tries, Duration(took)),
            result.RecentTried > 0 ? T(Strings.Search_ClientRecentFirstFormat, result.RecentTried) : null,
            back.AllBack ? Strings.Search_AllBack : null,
            back.ProfilesRestored ? Strings.Search_ProfilesPutBack : null);

        AppLog.Info("Search", $"client search: {result.Verdict} after {result.Tries} start(s) in {took.TotalSeconds:0} s: {ResultTitle}");
    }
}
