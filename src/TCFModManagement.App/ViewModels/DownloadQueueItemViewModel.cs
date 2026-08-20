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
    public Mod Mod { get; }

    // The version being installed, shown as-is (e.g. "1.4.2"). Display label only; the full ModVersion is resolved lazily via ResolveVersionAsync.
    public string VersionLabel { get; }

    public string ModName => Mod.Name ?? "Unknown mod";
    public string? Thumbnail => Mod.Thumbnail;

    // The install folder, captured at enqueue time rather than re-read later.
    public string InstallPath { get; }

    // Whether the worker should resolve this item's dependency tree and offer to queue anything missing before downloading it. False for items added as a dependency of another queued item.
    public bool CheckDependencies { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private DownloadQueueItemStatus _status = DownloadQueueItemStatus.Pending;

    [ObservableProperty]
    private double _progress;

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

    internal DownloadQueueItemViewModel(Mod mod, string versionLabel, string installPath, Func<Task<ModVersion?>> resolveVersion, bool checkDependencies)
    {
        Mod = mod;
        VersionLabel = versionLabel;
        InstallPath = installPath;
        _resolveVersion = resolveVersion;
        CheckDependencies = checkDependencies;
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
        OnPropertyChanged(nameof(IsIndeterminateProgress));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(IsFinished));
    }
}
