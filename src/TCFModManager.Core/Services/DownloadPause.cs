namespace TCFModManager.Core.Services;

// Shared by every download that should stop together: Pause cancels Token, and downloads given this
// wait in WaitAsync until Resume, then carry on from what is already on disk.
public sealed class DownloadPause
{
    private readonly object _gate = new();
    private CancellationTokenSource _running = new();
    private TaskCompletionSource _resumed = Resumed();

    public bool IsPaused
    {
        get { lock (_gate) return !_resumed.Task.IsCompleted; }
    }

    public CancellationToken Token
    {
        get { lock (_gate) return _running.Token; }
    }

    public void Pause()
    {
        CancellationTokenSource running;
        lock (_gate)
        {
            if (!_resumed.Task.IsCompleted) return;
            _resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            running = _running;
        }

        running.Cancel();
    }

    public void Resume()
    {
        TaskCompletionSource resumed;
        lock (_gate)
        {
            if (_resumed.Task.IsCompleted) return;
            _running = new();
            resumed = _resumed;
        }

        resumed.TrySetResult();
    }

    public Task WaitAsync(CancellationToken ct)
    {
        Task resumed;
        lock (_gate) resumed = _resumed.Task;
        return resumed.WaitAsync(ct);
    }

    private static TaskCompletionSource Resumed()
    {
        var resumed = new TaskCompletionSource();
        resumed.SetResult();
        return resumed;
    }
}
