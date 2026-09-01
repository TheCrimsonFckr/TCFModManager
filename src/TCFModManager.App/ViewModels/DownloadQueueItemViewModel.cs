using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// The stage a DownloadQueueItemViewModel is currently in. Drives its status badge and progress bar rendering.
public enum DownloadQueueItemStatus
{
    Pending,
    Downloading,
    Installing,
    Completed,
    Failed,
    Cancelled,
}

// 
// One entry in AppServices.DownloadQueue's queue - one card in DownloadsPage's list, one mod
// version being installed. Created and owned by DownloadQueueViewModel.Enqueue; holds the data
// and notification surface the queue's worker loop writes into as it processes the item.
// 
public sealed partial class DownloadQueueItemViewModel : ObservableObject
{
    // What is being installed - a catalog mod or an addon attached to one.
    public InstallTarget Target { get; }

    // The version being installed, shown as-is (e.g. "1.4.2"). Display label only; the full ModVersion is resolved lazily via ResolveVersionAsync.
    public string VersionLabel { get; }

    public string ModName => Target.Name;
    public string? Thumbnail => Target.Thumbnail;

    // Distinguishes an addon card from a mod card in the queue, since an addon's name rarely says
    // on its own that it needs a parent mod.
    public bool IsAddon => Target.IsAddon;

    // The install folder, captured at enqueue time rather than re-read later.
    public string InstallPath { get; }

    // Whether the worker should resolve this item's dependency tree and offer to queue anything missing before downloading it. False for items added as a dependency of another queued item.
    public bool CheckDependencies { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private DownloadQueueItemStatus _status = DownloadQueueItemStatus.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransferredValue))]
    [NotifyPropertyChangedFor(nameof(RateValue))]
    [NotifyPropertyChangedFor(nameof(EtaValue))]
    [NotifyPropertyChangedFor(nameof(HasTransferDetails))]
    private double _progress;

    //
    // The archive's size, when it is known: the catalog carries content_length on a version, and a
    // mod list resolves every version before it queues anything, so a list apply knows the whole
    // size up front. A single Install resolves lazily, so this fills in once the worker gets there.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransferredValue))]
    [NotifyPropertyChangedFor(nameof(RateValue))]
    [NotifyPropertyChangedFor(nameof(EtaValue))]
    [NotifyPropertyChangedFor(nameof(HasTransferDetails))]
    private long? _totalBytes;

    // Runs for the download stage only, which is the only stage with a measurable rate.
    private readonly Stopwatch _downloading = new();

    //
    // The transfer detail, split into one value per box - how much has arrived, how fast, and how
    // long is left. DownloadsPage renders each in its own fixed container so a number changing
    // width can't shift the ones beside it.
    //
    // Rate and time stay blank until there is enough to be honest with: a rate computed off the
    // first fraction of a percent is nonsense, and an estimate that swings between 8 seconds and
    // four minutes is worse than no estimate.
    //

    // Placeholder that holds a box's shape before its number exists.
    private const string NoValue = "\u2014";

    // Only while downloading, and only once the size is known - there is nothing to put in the
    // boxes otherwise.
    public bool HasTransferDetails =>
        Status == DownloadQueueItemStatus.Downloading && TotalBytes is > 0;

    // "173 of 258.3 MB" - both halves in the same unit, so the pair doesn't switch units partway
    // through and jump.
    public string TransferredValue =>
        TotalBytes is > 0 and var total ? SizePair(Progress * total, total) : NoValue;

    public string RateValue => BytesPerSecond is { } rate ? $"{Size(rate)}/s" : NoValue;

    public string EtaValue => BytesPerSecond is > 0
        ? Remaining(TimeSpan.FromSeconds(_downloading.Elapsed.TotalSeconds * (1 - Progress) / Progress))
        : NoValue;


    // What is left to fetch for this item, or null when its size isn't known yet. A pending item
    // hasn't started, so its whole archive is still to come.
    public long? RemainingBytes =>
        IsFinished || TotalBytes is not > 0 ? null : (long)(TotalBytes.Value * (1 - Progress));

    // The rate actually observed on this item so far, once there is enough of it to mean anything.
    public double? BytesPerSecond =>
        Status == DownloadQueueItemStatus.Downloading
        && TotalBytes is > 0
        && Progress > 0.02
        && _downloading.Elapsed > TimeSpan.FromSeconds(1.5)
            ? Progress * TotalBytes.Value / _downloading.Elapsed.TotalSeconds
            : null;

    public static string SizeLabel(double bytes) => Size(bytes);

    public static string RemainingLabel(TimeSpan left) => Remaining(left);

    // Both halves of "x of y" scaled to the larger one's unit.
    private static string SizePair(double done, double total)
    {
        var (scale, unit) = total switch
        {
            >= 1024d * 1024 * 1024 => (1024d * 1024 * 1024, "GB"),
            >= 1024d * 1024 => (1024d * 1024, "MB"),
            >= 1024d => (1024d, "KB"),
            _ => (1d, "B"),
        };

        return $"{done / scale:0.#} of {total / scale:0.#} {unit}";
    }

    private static string Size(double bytes) => bytes switch
    {
        >= 1024d * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.#} GB",
        >= 1024d * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        >= 1024d => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes:0} B",
    };

    private static string Remaining(TimeSpan left) => left switch
    {
        { TotalSeconds: < 10 } => "a few seconds",
        { TotalMinutes: < 1 } => $"{left.TotalSeconds:0}s",
        { TotalMinutes: < 60 } => $"{left.TotalMinutes:0}m",
        _ => $"{left.TotalHours:0.#}h",
    };

    [ObservableProperty]
    private string _statusMessage = "Waiting in queue...";

    // True while installing, since that stage has no byte count to report fractional progress for.
    public bool IsIndeterminateProgress => Status == DownloadQueueItemStatus.Installing;

    // Short badge text for Status.
    public string StatusLabel => Status switch
    {
        DownloadQueueItemStatus.Pending => "Pending",
        DownloadQueueItemStatus.Downloading => "Downloading",
        DownloadQueueItemStatus.Installing => "Installing",
        DownloadQueueItemStatus.Completed => "Completed",
        DownloadQueueItemStatus.Failed => "Failed",
        DownloadQueueItemStatus.Cancelled => "Cancelled",
        _ => "Unknown",
    };

    // Whether this item is still in a stage the user can back out of.
    public bool CanCancel => Status is DownloadQueueItemStatus.Pending
        or DownloadQueueItemStatus.Downloading
        or DownloadQueueItemStatus.Installing;

    // True once this item has finished one way or another, whatever the outcome.
    public bool IsFinished => Status is DownloadQueueItemStatus.Completed
        or DownloadQueueItemStatus.Failed
        or DownloadQueueItemStatus.Cancelled;

    private readonly CancellationTokenSource _cancellation = new();

    // Cancels the download/install this item is running. The install's file-copy stage
    // runs to completion regardless, so a mod is never left half-placed.
    internal CancellationToken Token => _cancellation.Token;

    // Items queued because this one declared them as missing dependencies. Cancelling this
    // item cancels them too.
    private readonly List<DownloadQueueItemViewModel> _dependencies = [];

    // Resolves the full ModVersion (with its download Link) for this item.
    private readonly Func<Task<ModVersion?>> _resolveVersion;

    internal DownloadQueueItemViewModel(
        InstallTarget target,
        string versionLabel,
        string installPath,
        Func<Task<ModVersion?>> resolveVersion,
        bool checkDependencies,
        long? totalBytes = null)
    {
        Target = target;
        VersionLabel = versionLabel;
        InstallPath = installPath;
        _resolveVersion = resolveVersion;
        CheckDependencies = checkDependencies;
        _totalBytes = totalBytes;
    }

    internal Task<ModVersion?> ResolveVersionAsync() => _resolveVersion();

    internal void AddDependency(DownloadQueueItemViewModel item) => _dependencies.Add(item);

    // Cancels this item and every dependency queued on its behalf. A still-pending item is
    // marked Cancelled immediately; an in-flight one is signalled and settles when its worker
    // unwinds.
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        foreach (var dependency in _dependencies)
        {
            if (dependency.CanCancel) dependency.Cancel();
        }

        if (!CanCancel) return;

        _cancellation.Cancel();

        if (Status == DownloadQueueItemStatus.Pending)
        {
            Status = DownloadQueueItemStatus.Cancelled;
            StatusMessage = "Cancelled before it started.";
        }
        else
        {
            StatusMessage = "Cancelling...";
        }
    }

    partial void OnStatusChanged(DownloadQueueItemStatus value)
    {
        if (value == DownloadQueueItemStatus.Downloading) _downloading.Restart();
        else _downloading.Stop();

        OnPropertyChanged(nameof(TransferredValue));
        OnPropertyChanged(nameof(RateValue));
        OnPropertyChanged(nameof(EtaValue));
        OnPropertyChanged(nameof(HasTransferDetails));
        OnPropertyChanged(nameof(IsIndeterminateProgress));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(IsFinished));
    }
}
