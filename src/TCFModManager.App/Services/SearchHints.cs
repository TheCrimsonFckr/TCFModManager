using System.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.App.Tours;

namespace TCFModManager.App.Services;

//
// OPEN-75: with Guide me on, a hint at each point of a search the user reaches - once per search.
//
public static class SearchHints
{
    private static readonly HashSet<string> Shown = new(StringComparer.Ordinal);
    private static bool _searched;

    public static void Start()
    {
        var finder = AppServices.BrokenModFinder;
        finder.PropertyChanged += OnChanged;
        finder.AnsweredNotSure += (_, _) => Hint("notsure", "diagnose.searchprogress",
            () => Strings.Hint_Search_NotSureTitle, () => Strings.Hint_Search_NotSureBody);
    }

    private static void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        var finder = AppServices.BrokenModFinder;

        switch (e.PropertyName)
        {
            case nameof(BrokenModFinder.IsRunning) when finder.IsRunning:
                Shown.Clear();
                _searched = true;
                Hint("start", "diagnose.searchprogress", () => Strings.Hint_Search_StartTitle,
                    () => finder.IsClientSearch ? Strings.Hint_Search_StartClientBody : Strings.Hint_Search_StartServerBody);
                break;

            case nameof(BrokenModFinder.AskText) when finder.AskText == Strings.Search_GameClosedAsk:
                Hint("gameclosed", "diagnose.searchanswer", () => Strings.Hint_Search_GameClosedTitle, () => Strings.Hint_Search_GameClosedBody);
                break;

            case nameof(BrokenModFinder.AskText) when finder.IsAsking:
                Hint("answer", "diagnose.searchanswer", () => Strings.Hint_Search_AnswerTitle, () => Strings.Hint_Search_AnswerBody);
                break;

            case nameof(BrokenModFinder.HasResult) when finder.HasResult && !finder.IsRunning:
                Hint("result", "diagnose.searchresult", () => Strings.Hint_Search_ResultTitle, () => Strings.Hint_Search_ResultBody);
                break;

            case nameof(BrokenModFinder.HasResultDone) when finder.HasResultDone:
                Hint("done", "diagnose.searchdone", () => Strings.Hint_Search_DoneTitle,
                    () => finder.IsClientSearch ? Strings.Hint_Search_DoneClientBody : Strings.Hint_Search_DoneServerBody);
                break;
        }
    }

    private static async void Hint(string moment, string target, Func<string> title, Func<string> body)
    {
        if (!_searched || !AppServices.BrokenModFinder.GuideMe || !Shown.Add(moment)) return;

        if (!await TourRunner.ShowHintAsync(target, title, body)) Shown.Remove(moment);
    }
}
