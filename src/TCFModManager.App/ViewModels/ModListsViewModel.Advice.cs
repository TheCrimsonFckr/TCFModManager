using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class ModListsViewModel
{
    private CancellationTokenSource? _adviceCancel;

    private Guid? _announceAdviceFor;

    private string? _announceAfter;

    private void LoadAdvice()
    {
        _adviceCancel?.Cancel();
        _adviceCancel = null;

        if (Selected is not { } list) return;

        var subjects = new List<HeadlessSubject>();
        foreach (var entry in Entries.Select(e => e.Entry))
        {
            if (entry.ModId is not { } id) continue;

            if (!entry.IsAddon)
            {
                subjects.Add(HeadlessSubject.ForMod(id, _mods.TryGetValue(id, out var mod) ? mod.UpdatedAt : null));
                continue;
            }

            var addon = AppServices.Addons.ById(id);
            subjects.Add(HeadlessSubject.ForAddon(id, addon?.UpdatedAt));
            if (addon?.ModId is { } parent)
                subjects.Add(HeadlessSubject.ForMod(parent, _mods.TryGetValue(parent, out var parentMod) ? parentMod.UpdatedAt : null));
        }

        if (subjects.Count == 0) return;

        var cancel = _adviceCancel = new CancellationTokenSource();
        _ = LoadAdviceAsync(list.Id, subjects, cancel);
    }

    private async Task LoadAdviceAsync(Guid listId, List<HeadlessSubject> subjects, CancellationTokenSource cancel)
    {
        await Task.Yield();

        var progress = new Progress<HeadlessAdviceResolved>(resolved =>
        {
            if (!cancel.IsCancellationRequested && !resolved.FromCache) ShowAdvice(resolved.Subject);
        });

        IReadOnlyDictionary<HeadlessSubject, HeadlessAdvice> resolved;
        try
        {
            resolved = await AppServices.HeadlessAdvice.ResolveAsync(subjects, progress, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLog.Warn("ModLists", $"couldn't load headless advice: {ex.Message}");
            return;
        }

        if (cancel.IsCancellationRequested || Selected?.Id != listId || _announceAdviceFor != listId) return;
        _announceAdviceFor = null;

        var byKey = resolved.ToDictionary(r => r.Key.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
        HeadlessAdvice? Known(HeadlessSubject subject) =>
            byKey.TryGetValue(subject.Key, out var advice) ? advice : AppServices.HeadlessAdvice.Known(subject);

        var count = Entries.Count(e => WithAdvice(e, Known).NeedsAttention);

        if (count > 0 && StatusMessage == _announceAfter)
            StatusMessage = Text(Strings.ModLists_StatusThenAdviceFormat, StatusMessage, Strings.ModLists_AdviceFound(count));
    }

    private void ShowAdvice(HeadlessSubject subject)
    {
        var changed = false;

        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i].Entry;
            if (entry.ModId is not { } id) continue;

            var affected = subject.Kind == HeadlessSubjectKind.Addon
                ? entry.IsAddon && id == subject.Id
                : entry.IsAddon ? ParentOf(id) == subject.Id : id == subject.Id;
            if (!affected) continue;

            var rebuilt = Row(entry);
            if (SameAdvice(rebuilt.Advice, Entries[i].Advice) && rebuilt.AdviceFromParent == Entries[i].AdviceFromParent) continue;

            Entries[i] = rebuilt;
            changed = true;
        }

        if (changed) Notify();
    }

    [RelayCommand(CanExecute = nameof(SelectionIsEditable))]
    private void KeepAdvice(ModListEntryRowViewModel? entry)
    {
        if (Selected is not { } list || entry is not { AdviceKeepsOff: true, Advice: { } advice }) return;
        if (SubjectOf(entry.Entry) is not { } subject) return;

        var key = HeadlessAdviceDismissals.Key(list.Id, subject, advice.Verdict);
        _kept.Keep(key);
        _keptKeys.Add(key);

        var index = Entries.IndexOf(entry);
        if (index >= 0) Entries[index] = Row(entry.Entry);
        Notify();

        StatusMessage = Text(Strings.ModLists_AdviceKeptFormat, entry.Name);
    }

    private static bool SameAdvice(HeadlessAdvice? a, HeadlessAdvice? b) =>
        a?.Verdict == b?.Verdict && (a?.Quotes ?? []).SequenceEqual(b?.Quotes ?? []);
}
