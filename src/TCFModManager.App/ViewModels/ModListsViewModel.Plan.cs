using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class ModListsViewModel
{
    //
    // The plan at a glance, as coloured counts in the shared status colours: red for what is missing
    // here, amber for what is here but not as the list has it, green for what is already right, grey
    // for what needs a person or is left alone. Each row below carries the same colour, so the chip
    // and the rows it counts are one thing.
    //
    public ObservableCollection<PlanChip> PlanChips { get; } = [];

    //
    // What Apply will do, on the button itself: "Install 1 mod", "Install 2, update 1", or in
    // Install mode "Download 3 mods". Plain Apply when there is nothing to fetch - an apply that only
    // enables or disables, or nothing previewed yet.
    //
    public string ApplyLabel
    {
        get
        {
            if (_preview is not { } preview) return Strings.ModLists_Apply;

            var plan = preview.Plan;
            var installs = plan.Install.Count();
            var updates = plan.Update.Count() + plan.Enable.Count(a => a.NeedsUpdateAfterEnable);

            if (installs + updates == 0) return Strings.ModLists_Apply;

            if (ModListDownloadMode.For(preview.List).DownloadOnly)
                return Strings.ModLists_ApplyDownload(installs + updates);

            if (installs > 0 && updates > 0) return Text(Strings.ModLists_ApplyInstallUpdateFormat, installs, updates);

            return installs > 0 ? Strings.ModLists_ApplyInstall(installs) : Strings.ModLists_ApplyUpdate(updates);
        }
    }

    private void NotifyPlanWording()
    {
        OnPropertyChanged(nameof(ApplyLabel));
        OnPropertyChanged(nameof(StepHint));
        OnPropertyChanged(nameof(StepIsApply));
    }

    //
    // Preview and Apply share one spot. Before a plan is showing it holds Preview; once one is,
    // Apply takes its place, worded as what it will do. Two buttons side by side, one greyed out until
    // the other had been pressed, left people wondering why Apply did nothing - this way the only
    // thing to press is the next step, and the line under it says what that step does.
    //
    public bool StepIsApply => _preview is not null;

    public string? StepHint => StepIsApply ? Strings.ModLists_StepHintApply : null;

    // ---- OPEN-22: dependencies the list doesn't name ---------------------------------------------

    public ObservableCollection<ModListDependencyRowViewModel> DependencyRows { get; } = [];

    public bool HasDependencyRows => DependencyRows.Count > 0;

    // Set when sp-mod couldn't be asked - the list can still be applied (Q5).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDependencyProblem))]
    private string? _dependencyProblem;

    public bool HasDependencyProblem => DependencyProblem is not null;

    // Needed, but nothing is published for this SPT - named, never offered.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDependencyNoVersionNote))]
    private string? _dependencyNoVersionNote;

    public bool HasDependencyNoVersionNote => DependencyNoVersionNote is not null;

    // Q1: your own lists can have the ticked ones written onto them as the apply finishes.
    public bool CanAddDependenciesToList => HasDependencyRows && _preview?.List.IsEditable == true;

    [ObservableProperty]
    private bool _addDependenciesToList;

    partial void OnAddDependenciesToListChanged(bool value) => ReplanDependencies();

    // The offers the rows were built from, so a replan keeps the rows (and their ticks) in place.
    private IReadOnlyList<ModListDependencyOffer>? _dependencyOffers;

    private bool _replanning;

    private void ShowDependencies(ModListPreview preview)
    {
        var check = preview.Dependencies;
        DependencyProblem = check?.Problem;
        DependencyNoVersionNote = check is { NoVersion.Count: > 0 }
            ? Text(Strings.ModLists_DepsNoVersionFormat, string.Join(Strings.Common_ListSeparator, check.NoVersion.Select(d => d.Name)))
            : null;

        if (ReferenceEquals(check?.Offers, _dependencyOffers)) return;

        _dependencyOffers = check?.Offers;
        DependencyRows.Clear();
        foreach (var offer in check?.Offers ?? [])
            DependencyRows.Add(new ModListDependencyRowViewModel(offer, !check!.Unticked.Contains(offer.Dependency.ModId), ReplanDependencies));

        if (check is not null && !check.AddToList && AddDependenciesToList)
        {
            _replanning = true;
            AddDependenciesToList = false;
            _replanning = false;
        }

        OnPropertyChanged(nameof(HasDependencyRows));
        OnPropertyChanged(nameof(CanAddDependenciesToList));
    }

    // A tick or the add-to-list box changed: plan the same preview again, without rescanning.
    private void ReplanDependencies()
    {
        if (_replanning || _preview is not { Dependencies: { } check } preview) return;

        var unticked = DependencyRows.Where(r => !r.IsTicked).Select(r => r.ModId).ToHashSet();
        ShowPlan(ModListService.Replan(preview, check with { Unticked = unticked, AddToList = AddDependenciesToList && preview.List.IsEditable }));
    }

    //
    // Selects a list and previews it - where Review and install on the Play page and the Server map
    // page lands. The plan is on screen and Apply is one press away; nothing is applied from here.
    //
    public async Task ReviewAsync(Guid listId)
    {
        Refresh(listId);

        if (Selected?.Id != listId || !PreviewCommand.CanExecute(null)) return;

        await PreviewCommand.ExecuteAsync(null);
    }

    // Works out what applying the selected list would do. Nothing moves and nothing downloads.
    [RelayCommand(CanExecute = nameof(CanUseStoredList))]
    private async Task PreviewAsync()
    {
        if (Selected is not { } row) return;

        await RunAsync(async () =>
        {
            var preview = await _service.PreviewAsync(row.List, withDependencies: true);

            if (preview is null)
            {
                StatusMessage = AppMessages.NoSptInstallFolder;
                return;
            }

            ShowPlan(preview);
        });
    }

    private void ShowPlan(ModListPreview preview)
    {
        _preview = preview;
        PlanRows.Clear();

        foreach (var row in Rows(preview.Plan)) PlanRows.Add(row);

        ShowDependencies(preview);

        var plan = preview.Plan;

        PlanChips.Clear();
        foreach (var chip in Chips(plan)) PlanChips.Add(chip);

        var counts = new List<string>();
        void Count(int n, string format) { if (n > 0) counts.Add(Text(format, n)); }

        Count(plan.Install.Count(), Strings.ModLists_CountToInstallFormat);
        Count(plan.Update.Count(), Strings.ModLists_CountToUpdateFormat);
        Count(plan.Enable.Count(), Strings.ModLists_CountToEnableFormat);
        Count(plan.Disable.Count(), Strings.ModLists_CountToDisableFormat);
        Count(plan.Pinned.Count(), Strings.ModLists_CountPinnedFormat);
        Count(plan.Keep.Count(), Strings.ModLists_CountAlreadyRightFormat);
        Count(plan.Manual.Count(), Strings.ModLists_CountToFetchFormat);

        PlanSummary = plan.IsNoOp && !plan.Manual.Any()
            ? Strings.ModLists_PlanNoOp
            : string.Join(Strings.Common_ListSeparator, counts) + ".";

        VersionWarning = preview.List.SptVersion is { } captured
            && preview.Install.SptVersion is { } current
            && !string.Equals(captured, current, StringComparison.OrdinalIgnoreCase)
                ? Text(Strings.ModLists_VersionWarningFormat, captured, current)
                : null;

        var downloads = plan.Install.Count() + plan.Update.Count()
            + plan.Enable.Count(a => a.NeedsUpdateAfterEnable);
        DownloadOnlyNotice = downloads > 0 && ModListDownloadMode.For(preview.List).DownloadOnly
            ? Strings.ModLists_DownloadOnlyNotice(downloads)
            : null;

        SetApplyBlockers(ApplyBlockers(preview));
        NotifyPlanWording();
        StatusMessage = PlanStatus(preview);
        UpdateBlockerWatch();
    }

    //
    // What the apply would be refused over right now: a plan that enables, disables, installs or
    // updates a mod in the install, while SPT is running from it. A download-only list never touches
    // the install, so only its moves count.
    //
    private static IReadOnlyList<string> ApplyBlockers(ModListPreview preview)
    {
        var plan = preview.Plan;
        var touchesInstall = plan.Enable.Any() || plan.Disable.Any()
            || (!ModListDownloadMode.For(preview.List).DownloadOnly && (plan.Install.Any() || plan.Update.Any()));

        return touchesInstall ? ModInstallService.RunningBlockers(preview.Install.InstallPath) : [];
    }

    private static string PlanStatus(ModListPreview preview) => Text(Strings.ModLists_PreviewedFormat, preview.List.Name);

    //
    // Said in a warning bar beside the greyed-out Apply, naming the game or the server in words
    // rather than by executable - "close SPT.Server.exe" read as "close the game" to people who had
    // already closed it. One whole sentence per case so each translates as a sentence.
    //
    public string? ApplyBlockedWarning
    {
        get
        {
            if (_applyBlockers.Count == 0) return null;

            var game = _applyBlockers.Any(b => b.StartsWith("EscapeFromTarkov", StringComparison.OrdinalIgnoreCase));
            var server = _applyBlockers.Any(b => !b.StartsWith("EscapeFromTarkov", StringComparison.OrdinalIgnoreCase));

            return (game, server) switch
            {
                (true, true) => Strings.ModLists_ApplyBlockedBoth,
                (true, false) => Strings.ModLists_ApplyBlockedGame,
                _ => Strings.ModLists_ApplyBlockedServer,
            };
        }
    }

    public bool IsApplyBlocked => _applyBlockers.Count > 0;

    private void SetApplyBlockers(IReadOnlyList<string> blockers)
    {
        _applyBlockers = blockers;
        ApplyCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ApplyBlockedWarning));
        OnPropertyChanged(nameof(IsApplyBlocked));
    }

    //
    // Apply stays greyed out while SPT runs from the install, and comes back by itself once it is
    // closed - the server and game are started and stopped outside this page, so a poll is the only
    // way to notice. Runs only while a plan is showing and the page is on screen.
    //
    private IReadOnlyList<string> _applyBlockers = [];

    private DispatcherTimer? _blockerWatch;

    private bool _pageShown;

    private bool _checkingBlockers;

    public void PageShown()
    {
        _pageShown = true;
        if (_preview is not null) _ = CheckBlockersAsync();
        UpdateBlockerWatch();
    }

    public void PageHidden()
    {
        _pageShown = false;
        UpdateBlockerWatch();
    }

    private void UpdateBlockerWatch()
    {
        if (_pageShown && _preview is not null)
        {
            if (_blockerWatch is null)
            {
                _blockerWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _blockerWatch.Tick += (_, _) => _ = CheckBlockersAsync();
            }

            _blockerWatch.Start();
        }
        else
        {
            _blockerWatch?.Stop();
        }
    }

    private async Task CheckBlockersAsync()
    {
        if (_checkingBlockers || IsBusy || _preview is not { } preview) return;

        IReadOnlyList<string> now;
        _checkingBlockers = true;
        try
        {
            now = await Task.Run(() => ApplyBlockers(preview));
        }
        finally
        {
            _checkingBlockers = false;
        }

        // Replaced or cleared while the look-up ran, or nothing changed: leave it alone.
        if (!ReferenceEquals(preview, _preview) || IsBusy || now.SequenceEqual(_applyBlockers)) return;

        SetApplyBlockers(now);
    }

    private IEnumerable<ModListActionRowViewModel> Rows(ModListPlan plan)
    {
        _titles = CatalogTitles();

        // A dependency the list doesn't name has its own row above, with its tick.
        return plan.Actions
            .Where(a => !a.IsDependency)
            .Select(PlanRow)
            .OrderBy(r => r.Order)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
    }

    //
    // A row with an entry reads exactly as that entry does in the contents view. A row without one -
    // a mod installed here that the list does not name, about to be disabled or held by a pin - is
    // described from what is installed, in the same shape.
    //
    private ModListActionRowViewModel PlanRow(ModListAction action)
    {
        var entry = action.Entry ?? (action.Installed is { } installed
            ? new ModListEntry
            {
                Name = installed.Name,
                ModId = installed.ModId,
                IsAddon = installed.IsAddon,
                Version = installed.Version,
                Folders = [.. installed.Folders],
            }
            : null);

        var name = entry?.ModId is { } id && _titles.TryGetValue((id, entry.IsAddon), out var title)
            ? title
            : action.Name;

        var (status, tone, icon) = StatusOf(action);

        return new ModListActionRowViewModel(
            Label(action),
            name,
            Detail(action),
            Order(action.Kind),
            action,
            entry is null ? string.Empty : EntryDetail(entry, name),
            action.Entry is { } listed
                ? _preview?.List.SpModSource?.IsScopeUnchecked(listed) == true
                    ? Strings.ModLists_ScopeUnchecked
                    : ModListScopes.Label(listed.EffectiveScope)
                : null,
            status,
            tone,
            icon);
    }

    private const string DisabledGlyph = "PlugDisconnected24";

    private static (string Status, string Tone, string Icon) StatusOf(ModListAction action) => action.Kind switch
    {
        ModListActionKind.Install =>
            (Strings.ModLists_StatusMissing, "Bad", ModStatusDisplay.Glyph(ModStatus.NotInstalled)),
        ModListActionKind.Update when action.IsRepair =>
            (Strings.ModLists_StatusHalfInstalled, "Warn", "ErrorCircle24"),
        ModListActionKind.Update =>
            (VersionMove(action), "Warn", ModStatusDisplay.Glyph(ModStatus.UpdateAvailable)),
        ModListActionKind.Enable when action.NeedsUpdateAfterEnable && !action.IsRepair =>
            (Text(Strings.ModLists_StatusDisabledMovingFormat, VersionMove(action)), "Disabled", DisabledGlyph),
        ModListActionKind.Enable => (Strings.ModLists_StatusDisabled, "Disabled", DisabledGlyph),
        ModListActionKind.Disable => (Strings.ModLists_StatusWillDisable, "Neutral", DisabledGlyph),
        ModListActionKind.Pinned => (Strings.ModLists_StatusPinned, "Neutral", "Pin24"),
        ModListActionKind.Manual =>
            (Strings.ModLists_StatusGetByHand, "Neutral", ModStatusDisplay.Glyph(ModStatus.NoCompatibleVersion)),
        _ => (Strings.ModLists_StatusAlreadyRight, "Good", ModStatusDisplay.Glyph(ModStatus.Installed)),
    };

    // "3.0.0 → 3.1.2": the version installed here, then the one the list asks for.
    private static string VersionMove(ModListAction action) =>
        Text(Strings.ModLists_StatusVersionFormat,
            action.InstalledVersion ?? Strings.Common_Unknown,
            action.TargetVersion ?? Strings.ModLists_ActionNewestPublished);

    private static IEnumerable<PlanChip> Chips(ModListPlan plan)
    {
        var repairs = plan.Update.Count(a => a.IsRepair);

        (int Count, Func<int, string> Text, string Tone, string Icon)[] chips =
        [
            (plan.Install.Count(), n => Strings.ModLists_ChipMissing(n), "Bad", ModStatusDisplay.Glyph(ModStatus.NotInstalled)),
            (plan.Update.Count() - repairs, n => Strings.ModLists_ChipDifferentVersion(n), "Warn",
                ModStatusDisplay.Glyph(ModStatus.UpdateAvailable)),
            (repairs, n => Strings.ModLists_ChipHalfInstalled(n), "Warn", "ErrorCircle24"),
            (plan.Enable.Count(), n => Strings.ModLists_ChipDisabled(n), "Disabled", DisabledGlyph),
            (plan.Manual.Count(), n => Strings.ModLists_ChipGetByHand(n), "Neutral",
                ModStatusDisplay.Glyph(ModStatus.NoCompatibleVersion)),
            (plan.Disable.Count(), n => Strings.ModLists_ChipWillDisable(n), "Neutral", DisabledGlyph),
            (plan.Pinned.Count(), n => Strings.ModLists_ChipPinned(n), "Neutral", "Pin24"),
            (plan.Keep.Count(), n => Strings.ModLists_ChipAlreadyRight(n), "Good", ModStatusDisplay.Glyph(ModStatus.Installed)),
        ];

        return chips.Where(c => c.Count > 0).Select(c => new PlanChip(c.Text(c.Count), c.Tone, c.Icon));
    }

    private static string Label(ModListAction action) => action.Kind switch
    {
        ModListActionKind.Install => Strings.ModLists_ActionInstall,
        ModListActionKind.Update => action.IsRepair
            ? Strings.ModLists_ActionReinstall
            : action.IsDowngrade ? Strings.ModLists_ActionDowngrade : Strings.ModLists_ActionUpdate,
        ModListActionKind.Enable => action.NeedsUpdateAfterEnable
            ? action.IsRepair ? Strings.ModLists_ActionEnableReinstall : Strings.ModLists_ActionEnableUpdate
            : Strings.ModLists_ActionEnable,
        ModListActionKind.Disable => Strings.ModLists_ActionDisable,
        ModListActionKind.Pinned => Strings.ModLists_ActionPinned,
        ModListActionKind.Manual => Strings.ModLists_ActionManual,
        _ => Strings.ModLists_ActionUnchanged,
    };

    //
    // Prefixed with "Addon - " where it applies: two rows can otherwise read identically while
    // meaning different things, since an addon and a mod are numbered separately and an addon's
    // name rarely says what it attaches to.
    //
    private static string Detail(ModListAction action)
    {
        var detail = DetailFor(action);
        if (!action.IsAddon) return detail;

        return detail.Length == 0
            ? Strings.ModLists_ActionAddon
            : Text(Strings.ModLists_ActionAddonDetailFormat, detail);
    }

    private static string DetailFor(ModListAction action) => action.Kind switch
    {
        ModListActionKind.Install => action.TargetVersion is null
            ? Strings.ModLists_ActionNewestPublished
            : Text(Strings.ModLists_ActionVersionFormat, action.TargetVersion),

        ModListActionKind.Update or ModListActionKind.Enable when action.IsRepair =>
            Text(Strings.ModLists_ActionRepairFormat, action.TargetVersion ?? action.InstalledVersion),

        ModListActionKind.Update or ModListActionKind.Enable when action.TargetVersion is not null
            && action.InstalledVersion is not null && action.TargetVersion != action.InstalledVersion =>
            Text(Strings.ModLists_ActionVersionChangeFormat, action.InstalledVersion, action.TargetVersion),

        ModListActionKind.Disable => Strings.ModLists_ActionNotOnList,
        ModListActionKind.Pinned => Strings.ModLists_ActionPinnedDetail,
        ModListActionKind.Manual => Strings.ModLists_ActionManualDetail,
        _ => action.InstalledVersion is null
            ? string.Empty
            : Text(Strings.ModLists_ActionVersionFormat, action.InstalledVersion),
    };

    private static int Order(ModListActionKind kind) => kind switch
    {
        ModListActionKind.Install => 0,
        ModListActionKind.Update => 1,
        ModListActionKind.Enable => 2,
        ModListActionKind.Disable => 3,
        ModListActionKind.Pinned => 4,
        ModListActionKind.Manual => 5,
        _ => 6,
    };

    // Keeps the panel in the order a stored list holds - by name, the order capture writes.
    // Puts the list's contents back in view after a preview. Changes nothing either way.
    [RelayCommand]
    private void ClosePlan()
    {
        ClearPlan();
        StatusMessage = Selected is { } row ? Text(Strings.ModLists_ShowingFormat, row.Name) : StatusMessage;
    }
}
