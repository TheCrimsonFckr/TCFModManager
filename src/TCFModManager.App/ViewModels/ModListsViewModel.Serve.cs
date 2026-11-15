using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One difference between the list and the copy the server serves, on the Serve preview.
public sealed record ServeChangeRow(string Name, string Change, string Tone);

// OPEN-44 Q18: the list served from this machine is previewed and served, never applied whole.
public partial class ModListsViewModel
{
    public bool SelectionIsServedHere => Selected?.IsServedHere == true;

    public string? ServedHereHint => SelectionIsServedHere ? Strings.ModLists_ServedHereHint : null;

    public bool CanPreviewAsPlayerHere =>
        SelectionIsServedHere && new SettingsService().Load().Roles.HasFlag(InstallRoles.Player);

    public ObservableCollection<ServeChangeRow> ServeRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServePreview))]
    [NotifyPropertyChangedFor(nameof(ShowPreviewStep))]
    [NotifyPropertyChangedFor(nameof(ShowServeStep))]
    [NotifyCanExecuteChangedFor(nameof(ServeCommand))]
    private string? _serveSummary;

    private bool _serveHasChanges;

    public bool HasServePreview => ServeSummary is not null;

    public bool ShowPreviewStep => !StepIsApply && !HasServePreview;

    public bool ShowServeStep => !StepIsApply && HasServePreview;

    public bool ShowPreviewAsPlayer => CanPreviewAsPlayerHere && !StepIsApply;

    partial void OnUnsavedCountChanged(int value)
    {
        if (value > 0) ClearServe();
    }

    private void NotifyServedHere()
    {
        OnPropertyChanged(nameof(SelectionIsServedHere));
        OnPropertyChanged(nameof(ServedHereHint));
        OnPropertyChanged(nameof(CanPreviewAsPlayerHere));
        OnPropertyChanged(nameof(ShowPreviewAsPlayer));
        OnPropertyChanged(nameof(HasUnpublishedChanges));
    }

    private void ClearServe()
    {
        ServeRows.Clear();
        _serveHasChanges = false;
        ServeSummary = null;
    }

    // (b): this install against the list first, then what the Map's machines would get.
    private async Task ServePreviewAsync(ModListRowViewModel row)
    {
        var preview = await _service.PreviewAsync(row.List, asPlayer: true);
        if (Selected?.Id != row.Id) return;

        if (preview is null)
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        ApplyStanding(preview);
        SetBehindCount(ModListService.NewerHere(row.List.Entries, preview.Install).Changed.Count);

        ServeRows.Clear();

        if (ServedCopy() is not { } served)
        {
            _serveHasChanges = true;
            ServeSummary = Strings.ModLists_ServeNotServedYet;
            return;
        }

        var changes = ModListPublication.Changes(row.List, served.List);
        foreach (var change in changes) ServeRows.Add(ServeRow(change));

        _serveHasChanges = !ModListPublication.ContentsMatch(row.List, served.List);
        ServeSummary = _serveHasChanges
            ? Text(Strings.ModLists_ServeChangesFormat, served.At.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : Strings.ModLists_ServeUpToDate;
    }

    private ServeChangeRow ServeRow(ModListServeChange change)
    {
        var name = NameOf(change.Entry);

        return change.Kind switch
        {
            ModListServeChangeKind.Added => new(name, Strings.ModLists_ServeAdded, "Good"),
            ModListServeChangeKind.Removed => new(name, Strings.ModLists_ServeRemoved, "Bad"),
            ModListServeChangeKind.Version => new(name, Text(Strings.ModLists_StatusVersionFormat,
                change.FromVersion ?? Strings.ModLists_ActionNewestPublished,
                change.ToVersion ?? Strings.ModLists_ActionNewestPublished), "Warn"),
            _ => new(name, Text(Strings.ModLists_ServeScopeFormat,
                ModListScopes.Label(change.FromScope), ModListScopes.Label(change.ToScope)), "Neutral"),
        };
    }

    private bool CanServe() => SelectionIsServedHere && _serveHasChanges && HasServePreview && !HasUnsavedChanges && CanPublish();

    [RelayCommand(CanExecute = nameof(CanServe))]
    private void Serve() => Publish();

    // (e): the served list planned here as a player receives it - never the whole-list sweep.
    [RelayCommand(CanExecute = nameof(CanUseStoredList))]
    private async Task PreviewAsPlayerAsync()
    {
        if (Selected is not { IsServedHere: true } row) return;

        await RunAsync(async () =>
        {
            var preview = await _service.PreviewAsync(row.List, withDependencies: true, asPlayer: true);
            if (Selected?.Id != row.Id) return;

            if (preview is null)
            {
                StatusMessage = AppMessages.NoSptInstallFolder;
                return;
            }

            ClearServe();
            ShowPlan(preview);
        });
    }
}
