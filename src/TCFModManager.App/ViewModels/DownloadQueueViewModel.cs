using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

// App-lifetime download queue: up to three downloads at once, installs one at a time in the order queued (OPEN-12 F7), each item's dependencies resolved before it downloads.
public sealed partial class DownloadQueueViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly Channel<DownloadQueueItemViewModel> _channel = Channel.CreateUnbounded<DownloadQueueItemViewModel>();

    public ObservableCollection<DownloadQueueItemViewModel> Items { get; } = [];

    //
    // The whole queue's state, one value per box: how far through it is, how much is left to fetch
    // and how long that should take. Worth having at all because a mod list can queue forty items
    // at once - per-card progress answers "how is this one doing", not "how long until I can play".
    //
    // Split into separate values rather than one line so DownloadsPage can give each its own fixed
    // container; run together they shuffle sideways every time a figure changes width.
    //
    [ObservableProperty]
    private bool _hasSummary;

    // "1 of 2 done"
    [ObservableProperty]
    private string _summaryProgress = NoValue;

    // "85.3 MB"
    [ObservableProperty]
    private string _summaryRemaining = NoValue;

    // How many queued items The Forge gave no size for, so the remaining figure never silently
    // under-reports. Its own box rather than a note on the remaining one, which would make that box
    // change width as items resolve.
    [ObservableProperty]
    private bool _hasUnsized;

    [ObservableProperty]
    private string _summaryUnsized = NoValue;

    // "11s"
    [ObservableProperty]
    private string _summaryEta = NoValue;

    private const string NoValue = "\u2014";

    // ---- OPEN-12 A7: the downloads bar along the bottom of the window (Views/DownloadsBar) ----------
    //
    // Shown only while the queue has work, and for a few seconds after it finishes (Chris,
    // 2026-10-06), so it takes no room when nothing is happening.

    [ObservableProperty]
    private bool _barVisible;

    // "Downloading" / "Installing" while the queue has work, "Downloads finished" after.
    [ObservableProperty]
    private string _barTitle = string.Empty;

    // What is happening now - "SAIN 4.4.3 - 2 of 5 done".
    [ObservableProperty]
    private string _barDetail = string.Empty;

    // How far the whole queue is, 0 to 1: finished items count whole, the rest by how far along they are.
    [ObservableProperty]
    private double _barFraction;

    private static readonly TimeSpan BarLinger = TimeSpan.FromSeconds(5);
    private System.Windows.Threading.DispatcherTimer? _barTimer;
    private bool _barBusy;

    // Raised after an item finishes installing successfully. BrowseViewModel subscribes to refresh its cards' install/update status dots.
    public event EventHandler? ItemInstalled;

    public DownloadQueueViewModel()
    {
        _ = PrepareLoopAsync();
        _ = InstallLoopAsync();
    }

    // Adds a request to the end of the queue and returns immediately; the download/install
    // happens later when the worker reaches it. When <paramref name="dependencyOf"/> is set, the
    // new item is registered against it so cancelling that item cancels this one too.
    //
    // <paramref name="downloadOnly"/> saves the archive for the user to install instead (Monitor
    // mode). A dependency takes its parent's value, whatever is passed here.
    public DownloadQueueItemViewModel Enqueue(
        InstallTarget target,
        string versionLabel,
        string installPath,
        Func<Task<ModVersion?>> resolveVersion,
        bool checkDependencies = true,
        DownloadQueueItemViewModel? dependencyOf = null,
        long? totalBytes = null,
        bool downloadOnly = false,
        string? downloadSubfolder = null)
    {
        if (dependencyOf is not null)
        {
            downloadOnly = dependencyOf.DownloadOnly;
            downloadSubfolder = dependencyOf.DownloadSubfolder;
        }

        var item = new DownloadQueueItemViewModel(
            target, versionLabel, installPath, resolveVersion, checkDependencies, totalBytes,
            downloadOnly, downloadSubfolder);
        dependencyOf?.AddDependency(item);
        item.PropertyChanged += OnItemChanged;

        // How a failed card gets to go round again. The channel is the queue's, so the card asks
        // rather than writing to it - and an item that was never enqueued has no retry, which is
        // what CanRetry checks.
        item.Requeue = queued => _channel.Writer.TryWrite(queued);

        Items.Add(item);
        _channel.Writer.TryWrite(item);
        UpdateSummary();
        return item;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadQueueItemViewModel.Status)
            or nameof(DownloadQueueItemViewModel.Progress)
            or nameof(DownloadQueueItemViewModel.TotalBytes))
        {
            UpdateSummary();
        }
    }

    //
    // Sizes come from the catalog's content_length, so they are known for a mod list apply (which
    // resolves every version before queueing) and fill in one at a time for anything else. The
    // estimate is deliberately built from what is known rather than extrapolated over what isn't -
    // it says how much is left to fetch, and only adds a time once a real rate has been observed.
    //
    private void UpdateSummary()
    {
        var unfinished = Items.Where(i => !i.IsFinished).ToList();

        UpdateBar(unfinished);

        if (unfinished.Count == 0)
        {
            HasSummary = false;
            HasUnsized = false;
            SummaryProgress = SummaryRemaining = SummaryEta = SummaryUnsized = NoValue;

            // Still notified on the way out: a queue that has just finished with failures in it is
            // exactly when the retry button has to appear.
            OnPropertyChanged(nameof(HasRetryable));
            return;
        }

        var done = Items.Count - unfinished.Count;
        var remaining = unfinished.Sum(i => i.RemainingBytes ?? 0);
        var unknown = unfinished.Count(i => i.RemainingBytes is null);

        HasSummary = true;
        SummaryProgress = Text(Strings.Downloads_SummaryProgressFormat, done, Items.Count);

        SummaryRemaining = remaining > 0 ? DownloadQueueItemViewModel.SizeLabel(remaining) : NoValue;

        HasUnsized = unknown > 0;
        // Just the count: the caption above this box already says what they are, so there is no
        // sentence for a plural to agree with.
        SummaryUnsized = unknown.ToString(CultureInfo.CurrentCulture);

        // Every download running now together - up to three at once (OPEN-12 F7).
        var rate = unfinished.Sum(i => i.BytesPerSecond ?? 0);
        SummaryEta = remaining > 0 && rate > 0
            ? DownloadQueueItemViewModel.RemainingLabel(TimeSpan.FromSeconds(remaining / rate))
            : NoValue;

        OnPropertyChanged(nameof(HasRetryable));
    }

    // The bar's text is set as the queue moves, so a language change has it written again.
    protected internal override void RefreshText()
    {
        base.RefreshText();
        UpdateSummary();
    }

    private void UpdateBar(List<DownloadQueueItemViewModel> unfinished)
    {
        var total = Items.Count;
        var done = total - unfinished.Count;

        if (unfinished.Count > 0)
        {
            _barTimer?.Stop();
            _barBusy = true;
            BarVisible = true;

            var installing = unfinished.FirstOrDefault(i => i.Status == DownloadQueueItemStatus.Installing);
            var current = installing
                ?? unfinished.FirstOrDefault(i => i.Status == DownloadQueueItemStatus.Downloading && i.Progress < 1)
                ?? unfinished[0];

            BarTitle = installing is not null ? Strings.Downloads_BarInstalling : Strings.Downloads_BarDownloading;
            BarDetail = Text(Strings.Downloads_BarDetailFormat, current.ModName, current.VersionLabel, done, total);

            // A download is most of an item's time; installing it is the rest.
            var partly = unfinished.Sum(i => i.Progress * 0.8 + (i.Status == DownloadQueueItemStatus.Installing ? 0.1 : 0));
            BarFraction = total == 0 ? 0 : Math.Clamp((done + partly) / total, 0, 1);
            return;
        }

        if (!_barBusy) return;

        // The queue has just emptied: say so for a moment, then step out of the way.
        _barBusy = false;
        BarTitle = Strings.Downloads_BarFinished;
        BarDetail = Items.Any(i => i.Status is DownloadQueueItemStatus.Failed)
            ? Strings.Downloads_BarSomeFailed
            : Text(Strings.Downloads_BarDoneFormat, Items.Count(i => i.Status == DownloadQueueItemStatus.Completed), total);
        BarFraction = 1;

        _barTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = BarLinger };
        _barTimer.Tick -= OnBarTimer;
        _barTimer.Tick += OnBarTimer;
        _barTimer.Start();
    }

    private void OnBarTimer(object? sender, EventArgs e)
    {
        _barTimer?.Stop();
        if (!_barBusy) BarVisible = false;
    }

    //
    // Every failed or cancelled item back on the queue, oldest first.
    //
    // Worth having as well as the per-card button because of how these actually fail: a mod list
    // apply queues dozens at once, and one flaky spell on The Forge takes out a run of them
    // together. Clicking Retry fourteen times is the same work with more chances to miss one.
    //
    public void RetryFailed()
    {
        foreach (var item in Items.Where(i => i.CanRetry).ToList()) item.RetryCommand.Execute(null);

        UpdateSummary();
    }

    // Whether anything is sitting there retryable, so the button can stay out of the way otherwise.
    public bool HasRetryable => Items.Any(i => i.CanRetry);

    // Removes every Completed/Failed/Cancelled card from the list; queued/in-progress items are left alone.
    public void ClearFinished()
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!Items[i].IsFinished) continue;

            Items[i].PropertyChanged -= OnItemChanged;
            Items.RemoveAt(i);
        }

        UpdateSummary();
    }

    //
    // OPEN-12 F7: the queue runs in three stages rather than one item start to finish.
    //
    //   1. Prepare - one item at a time, in the order queued: the version is looked up and the
    //      Fika and dependency questions asked. Questions stay one at a time and in order.
    //   2. Download - up to MaxDownloads at once, each retried after a dropped connection and
    //      resumed from where it broke off when the server allows it.
    //   3. Install - one at a time, in the order queued: installs write into the same folders,
    //      so they never overlap, and a dependency queued behind its mod still lands behind it.
    //
    // So a mod list of forty downloads three at a time while the first ones install, where it used
    // to download and install one after another. Ported from SSPTMM's queue, without its archive
    // cache (F6 not taken): each archive is a one-off file, deleted once it is installed.
    //
    private const int MaxDownloads = 3;
    private readonly SemaphoreSlim _downloadSlots = new(MaxDownloads);

    // A download that fails in a way that can pass (the connection dropped, the host busy) is tried
    // again after these waits - resuming, not starting over, when the server allows it.
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)];

    private readonly Channel<InstallTurn> _installs = Channel.CreateUnbounded<InstallTurn>();

    //
    // One item's turn to install: the attempt it belongs to (its token) and that attempt's archive.
    // Carried together because a retry starts a NEW attempt on the same item - a turn left in the
    // channel by the attempt it replaced must not install the old archive.
    //
    private sealed record InstallTurn(DownloadQueueItemViewModel Item, CancellationToken Token, ModVersion Version, Task<string> Archive);

    // Stage 1, for the whole session. Nothing that goes wrong with one item stops the queue.
    private async Task PrepareLoopAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            try
            {
                await PrepareAsync(item);
            }
            catch (Exception ex)
            {
                AppLog.Error("Downloads", $"preparing {item.ModName} failed", ex);
                Settle(item, item.Token, ex);
            }
        }
    }

    // Stage 3, likewise.
    private async Task InstallLoopAsync()
    {
        await foreach (var turn in _installs.Reader.ReadAllAsync())
        {
            try
            {
                await InstallTurnAsync(turn);
            }
            catch (Exception ex)
            {
                AppLog.Error("Downloads", $"installing {turn.Item.ModName} failed", ex);
                Settle(turn.Item, turn.Token, ex);
            }
        }
    }

    private async Task PrepareAsync(DownloadQueueItemViewModel item)
    {
        // Cancelled while it sat in the queue behind something else.
        if (item.Status == DownloadQueueItemStatus.Cancelled || item.Token.IsCancellationRequested)
        {
            item.Status = DownloadQueueItemStatus.Cancelled;
            item.StatusMessage = Strings.Downloads_CancelledBeforeStart;
            return;
        }

        // Already under way: an item cancelled while waiting and then retried is in the queue twice,
        // and the second time round is not a second attempt.
        var token = item.Token;
        if (item.Status != DownloadQueueItemStatus.Pending || item.PreparedFor == token) return;
        item.PreparedFor = token;

        //
        // Cancel says so at once - while the item waits for a download slot or its turn to install -
        // rather than when the installs reach it. An install under way finishes placing files first,
        // so that one settles when it is done.
        //
        token.Register(() =>
        {
            if (item.Status != DownloadQueueItemStatus.Installing)
                Settle(item, token, new OperationCanceledException(token));
        });

        try
        {
            item.Status = DownloadQueueItemStatus.Downloading;
            item.StatusMessage = Strings.Downloads_ResolvingLink;

            var version = await item.ResolveVersionAsync();

            // Retried since: the new attempt is the one that goes on.
            if (token != item.Token) return;

            if (version?.Link is null)
            {
                Fail(item, Text(Strings.Downloads_NoLinkFormat, item.ModName, item.VersionLabel));
                return;
            }

            item.TotalBytes ??= version.ContentLength;

            token.ThrowIfCancellationRequested();

            //
            // A version sp-mod marks as not working with Fika, on an install that runs Fika (OPEN-12
            // F17): asked before anything is downloaded. Asked, never refused - the mark is the
            // author's word - and No leaves this one out, nothing else.
            //
            if (FikaInstall.IsIncompatible(version.FikaCompatibility)
                && await RunsFikaAsync(item.InstallPath)
                && !ConfirmNotForFika(item))
            {
                item.Status = DownloadQueueItemStatus.Cancelled;
                item.Progress = 0;
                item.StatusMessage = Text(Strings.Downloads_FikaDeclinedFormat, item.ModName, item.VersionLabel);
                AppLog.Info("Downloads", $"{item.ModName} {item.VersionLabel}: not for Fika, left out");
                return;
            }

            // Checked before this item's own download starts, so an accepted missing dependency
            // lands right behind it in the queue.
            if (item.CheckDependencies)
            {
                item.StatusMessage = Strings.Downloads_CheckingDependencies;
                await CheckDependenciesAsync(item, version);
            }

            token.ThrowIfCancellationRequested();
            if (token != item.Token) return;

            //
            // Monitor mode keeps the archive instead of installing it, so it never takes an install
            // turn. It takes a download slot like any other download, and runs beside this loop so
            // the items queued after it are not held up.
            //
            if (item.DownloadOnly)
            {
                item.StatusMessage = Strings.Downloads_WaitingToDownload;
                _ = SaveArchiveAsync(item, version, token);
                return;
            }

            _installs.Writer.TryWrite(new InstallTurn(item, token, version, FetchArchiveAsync(item, version, token)));
        }
        catch (Exception ex)
        {
            // An attempt a retry has replaced says nothing: the card is the new attempt's now.
            Settle(item, token, ex);
        }
    }

    // Stage 2: this item's archive, downloaded to a one-off file. Settles the card itself on failure.
    private async Task<string> FetchArchiveAsync(DownloadQueueItemViewModel item, ModVersion version, CancellationToken token)
    {
        var path = QueueArchives.NewPath(item.InstallPath);
        var part = path + ".part";
        var slot = false;

        try
        {
            item.StatusMessage = Strings.Downloads_WaitingToDownload;
            await _downloadSlots.WaitAsync(token);
            slot = true;

            var progress = new Progress<double>(p => item.Progress = p);

            for (var attempt = 0; ; attempt++)
            {
                item.StatusMessage = ModInstallWording.Describe(
                    new ModInstallProgress(ModInstallStage.Downloading, item.ModName, version.Version));
                try
                {
                    // A second attempt carries on from where the first broke off, when it can.
                    await AppServices.Downloads.DownloadAsync(version.Link!, part, progress, token,
                        resume: attempt > 0, resumable: true);
                    File.Move(part, path, overwrite: true);
                    break;
                }
                catch (Exception ex) when (attempt < RetryDelays.Length && IsPassing(ex, token))
                {
                    var wait = RetryDelays[attempt];
                    AppLog.Info("Downloads", $"{item.ModName} {item.VersionLabel}: {ex.Message} - trying again in {wait.TotalSeconds:F0}s");
                    item.StatusMessage = Text(Strings.Downloads_RetryingFormat, wait.TotalSeconds);
                    await Task.Delay(wait, token);
                }
            }
        }
        catch (Exception ex)
        {
            QueueArchives.Delete(path);
            QueueArchives.Delete(part);
            QueueArchives.Delete(ModDownloadService.ValidatorPathFor(part));

            // Said now, not when the installs reach this item.
            Settle(item, token, ex);
            throw;
        }
        finally
        {
            if (slot) _downloadSlots.Release();
        }

        // Downloaded; the install is one at a time, in the order queued.
        if (token == item.Token && !item.IsFinished)
        {
            item.Progress = 1.0;
            item.StatusMessage = Strings.Downloads_WaitingToInstall;
        }

        return path;
    }

    //
    // A failure that may not happen again: the connection dropped, reset or timed out, the host was
    // busy or failing (5xx, 408, 429), or the file arrived short. Not one that will happen again the
    // same way - a refusal (403, 404), a name that does not resolve, a certificate, a full disk.
    //
    private static bool IsPassing(Exception ex, CancellationToken token) => ex switch
    {
        _ when token.IsCancellationRequested => false,
        HttpRequestException { StatusCode: { } code } => (int)code >= 500 || (int)code is 408 or 429,
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded or HttpRequestError.Unknown } => true,
        HttpIOException => true,
        ModInstallException { Reason: ModInstallFailure.DownloadIncomplete } => true,
        TaskCanceledException => true,
        _ => false,
    };

    // Stage 3.
    private async Task InstallTurnAsync(InstallTurn turn)
    {
        var item = turn.Item;

        string archive;
        try
        {
            archive = await turn.Archive;
        }
        catch (Exception)
        {
            // Its download failed or was cancelled, and said so on the card already.
            return;
        }

        try
        {
            // Cancelled while it waited, or retried since: the archive is let go, nothing installed.
            if (turn.Token != item.Token || item.IsFinished || turn.Token.IsCancellationRequested)
            {
                if (turn.Token == item.Token && !item.IsFinished) Settle(item, turn.Token, new OperationCanceledException(turn.Token));
                return;
            }

            // Core reports which stage it is in; every phase past the download (removing the previous
            // version, extracting, copying files) is bucketed under Installing.
            var status = new Progress<ModInstallProgress>(p =>
            {
                item.StatusMessage = ModInstallWording.Describe(p);
                item.Status = DownloadQueueItemStatus.Installing;
            });

            item.Status = DownloadQueueItemStatus.Installing;

            var result = await AppServices.ModInstall.InstallAsync(
                item.Target, turn.Version, item.InstallPath, status, null, turn.Token, downloadedArchive: archive);

            Installed(item, result);
        }
        catch (Exception ex)
        {
            Settle(item, turn.Token, ex);
        }
        finally
        {
            QueueArchives.Delete(archive);
        }
    }

    private void Installed(DownloadQueueItemViewModel item, ModInstallResult result)
    {
        item.Status = DownloadQueueItemStatus.Completed;
        item.Progress = 1.0;

        // An update that changed one of the mod's own config files says so here rather than
        // leaving the user to find out in game - see ConfigUpdateWording.
        var configs = result.Configs is { } report ? ConfigUpdateWording.Summary(report) : null;

        var installed = Text(Strings.Downloads_InstalledFormat, item.ModName, item.VersionLabel);

        //
        // What the install deliberately didn't do is said on the card (D5): SPT's own files it left
        // alone and any that would have landed on this app's own files - named one per line in the
        // tooltip - and originals it kept to put back later (D22).
        //
        var skippedSpt = result.SkippedProtected ?? [];
        var skippedApp = result.SkippedAppFolder ?? [];
        var skipped = skippedSpt.Concat(skippedApp).ToList();
        var keptSpt = skippedSpt.Count > 0 ? Strings.Downloads_KeptSptFiles(skippedSpt.Count, skippedSpt.Count) : null;
        var keptApp = skippedApp.Count > 0 ? Strings.Downloads_KeptAppFiles(skippedApp.Count, skippedApp.Count) : null;
        var keptOriginals = result.OriginalsKept > 0
            ? Strings.Downloads_KeptOriginals(result.OriginalsKept, result.OriginalsKept)
            : null;

        var keptSettings = result.KeptSettings.Count > 0
            ? Strings.Downloads_KeptSettings(result.KeptSettings.Count, result.KeptSettings.Count)
            : null;

        item.StatusMessage = string.Join(
            Strings.Common_SentenceSeparator,
            new[] { installed, configs, keptSpt, keptApp, keptSettings, keptOriginals }.Where(s => !string.IsNullOrEmpty(s)));
        var named = skipped.Concat(result.KeptSettings).ToList();
        item.StatusDetail = named.Count > 0
            ? string.Join(Environment.NewLine, new[] { item.StatusMessage, "" }.Concat(named))
            : null;
        ItemInstalled?.Invoke(this, EventArgs.Empty);
    }

    //
    // How any stage ends an item that didn't succeed - only for the attempt it belongs to, and only
    // once: an attempt a retry has replaced, or an item already settled, says nothing.
    //
    private static void Settle(DownloadQueueItemViewModel item, CancellationToken token, Exception ex)
    {
        if (token != item.Token || item.IsFinished) return;

        switch (ex)
        {
            case OperationCanceledException when token.IsCancellationRequested:
                item.Status = DownloadQueueItemStatus.Cancelled;
                item.Progress = 0;
                item.StatusMessage = Text(Strings.Downloads_CancelledItemFormat, item.ModName, item.VersionLabel);
                break;
            case SpModApiException or HttpRequestException or HttpIOException:
                Fail(item, ApiProblems.Describe(ex), ex);
                break;
            case ModInstallException problem:
                // ModInstallService refused or gave up part way; ModInstallProblems words it.
                Fail(item, ModInstallProblems.Describe(problem), ex);
                break;
            case InvalidOperationException:
                // Anything else that reached here already carries a readable message of its own.
                Fail(item, ex.Message, ex);
                break;
            default:
                Fail(item, Text(Strings.Downloads_UnexpectedFormat, ex.Message), ex, unexpected: true);
                break;
        }
    }

    //
    // Marks the card failed and writes the same reason to the log - the card was the only place it
    // ever showed, so a failure on a user's PC left the log ending mid-install. The line carries the
    // card's sentence and the exception's type and message; an unexpected one gets its stack trace.
    //
    // Whether the install runs Fika, remembered for a minute: a mod list queues dozens of items, and
    // each would otherwise scan the install again.
    private (string Install, bool Runs, DateTime At)? _fika;

    private async Task<bool> RunsFikaAsync(string installPath)
    {
        if (_fika is { } known
            && string.Equals(known.Install, installPath, StringComparison.OrdinalIgnoreCase)
            && DateTime.UtcNow - known.At < TimeSpan.FromMinutes(1))
        {
            return known.Runs;
        }

        var runs = await Task.Run(() => FikaInstall.IsPresent(InstalledModScanner.Scan(installPath)));
        _fika = (installPath, runs, DateTime.UtcNow);
        return runs;
    }

    // Defaults to No, like the SPT question (SptCompatibility).
    private static bool ConfirmNotForFika(DownloadQueueItemViewModel item) =>
        System.Windows.MessageBox.Show(
            Text(item.DownloadOnly ? Strings.Downloads_FikaIncompatibleDownloadFormat : Strings.Downloads_FikaIncompatibleFormat,
                item.ModName, item.VersionLabel),
            Strings.Downloads_FikaIncompatibleTitle,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;

    private static void Fail(DownloadQueueItemViewModel item, string message, Exception? ex = null, bool unexpected = false)
    {
        item.Status = DownloadQueueItemStatus.Failed;
        item.StatusMessage = message;

        if (unexpected) AppLog.Error("Downloads", $"{item.ModName} {item.VersionLabel} failed: {message}", ex);
        else if (ex is null) AppLog.Warn("Downloads", $"{item.ModName} {item.VersionLabel} failed: {message}");
        else AppLog.Warn("Downloads", $"{item.ModName} {item.VersionLabel} failed: {message} ({ex.GetType().Name}: {ex.Message})");
    }

    //
    // Monitor mode's branch: the same download, saved to the user's folder instead of installed.
    // Nothing in the SPT install is touched, so there is no running-SPT check and no ItemInstalled -
    // what is on disk has not changed.
    //
    private async Task SaveArchiveAsync(DownloadQueueItemViewModel item, ModVersion version, CancellationToken token)
    {
        var slot = false;
        try
        {
            await _downloadSlots.WaitAsync(token);
            slot = true;

            await SaveArchiveCoreAsync(item, version, token);
        }
        catch (Exception ex)
        {
            Settle(item, token, ex);
        }
        finally
        {
            if (slot) _downloadSlots.Release();
        }
    }

    private static async Task SaveArchiveCoreAsync(DownloadQueueItemViewModel item, ModVersion version, CancellationToken token)
    {
        var folder = DownloadFolders.Resolve(new SettingsService().Load().Monitor.DownloadFolder);

        item.Status = DownloadQueueItemStatus.Downloading;
        item.StatusMessage = Text(Strings.Downloads_SavingToFormat, folder);

        var downloadProgress = new Progress<double>(p => item.Progress = p);

        var result = await AppServices.ModArchive.SaveArchiveAsync(
            item.Target, version, folder, item.InstallPath, item.DownloadSubfolder, downloadProgress, token);

        if (token != item.Token || item.IsFinished) return;

        item.Status = DownloadQueueItemStatus.Completed;
        item.Progress = 1.0;
        item.SavedPath = result.Record.ArchivePath;

        var saved = Text(
            result.Record.Unrecognised ? Strings.Downloads_SavedUnrecognisedFormat : Strings.Downloads_SavedFormat,
            item.ModName,
            item.VersionLabel,
            result.Record.ArchivePath);

        item.StatusMessage = ReplacesConfigs(result.Record, item.InstallPath)
            ? string.Join(Strings.Common_SentenceSeparator, saved, Strings.Downloads_SavedConfigsNote)
            : saved;
    }

    //
    // §8: config protection only runs on an install this app does. Said when the archive carries a
    // server config file that is already on disk - an update the user will be copying over their
    // own settings - and not for a first download, where there is nothing of theirs to lose.
    //
    private static bool ReplacesConfigs(DownloadedModRecord download, string installPath) =>
        download.ExpectedFiles.Any(f =>
            ModConfigFiles.IsServerModConfig(f.Path)
            && File.Exists(Path.Combine(installPath, f.Path.Replace('/', Path.DirectorySeparatorChar))));

    // Resolves item's full dependency tree for the version being installed, cross-references it against
    // a fresh disk scan + catalog match, and offers to queue anything missing via one
    // ReadModPageConfirmationWindow listing every missing mod. Anything queued is registered as a
    // dependency of <paramref name="item"/>, so cancelling it cancels them too. Best-effort: a
    // failed lookup silently skips the check rather than failing the queued item.
    private async Task CheckDependenciesAsync(DownloadQueueItemViewModel item, ModVersion version)
    {
        var target = item.Target;
        var installPath = item.InstallPath;

        var sptVersion = AppServices.SptEnvironment.InstalledVersion;
        if (string.IsNullOrWhiteSpace(sptVersion) || string.IsNullOrWhiteSpace(version.Version)) return;

        List<DependencyNode> nodes;
        try
        {
            // Both endpoints resolve to ordinary mods, so everything below this point is identical
            // for an addon - what an addon requires is mods, not other addons. An addon's parent is
            // deliberately not part of this: it isn't returned here, and it's handled where the
            // addon is offered instead.
            var identifier = string.IsNullOrWhiteSpace(target.Guid) ? target.Id.ToString() : target.Guid;
            var result = target.IsAddon
                ? await AppServices.SpModApi.GetAddonDependenciesAsync($"{identifier}:{version.Version}", sptVersion)
                : await AppServices.SpModApi.GetModDependenciesAsync($"{identifier}:{version.Version}", sptVersion);
            nodes = result.Values.FirstOrDefault() ?? [];
        }
        catch (Exception)
        {
            // Rate limited, network error, or an unrecognized SPT version - skip the check.
            return;
        }

        if (nodes.Count == 0) return;

        item.Token.ThrowIfCancellationRequested();

        // Fresh disk scan each time so it reflects whatever was just installed in this same batch.
        await AppServices.ModCache.EnsureLoadedAsync();
        var scanned = await Task.Run(() => InstalledModScanner.Scan(installPath));
        var installedMatches = InstalledModCardViewModel.BuildFrom(
            scanned, AppServices.ModCache.AllMods, sptVersion, AppServices.InstallManifest.Load().Mods,
            AppServices.Addons.AllAddons);

        // Every dependency node is a mod, so addon cards - whose ModId is an addon id - are left
        // out of both sets rather than being compared against mod ids.
        var installedIds = installedMatches.Where(m => m is { IsAddon: false, ModId: not null })
            .Select(m => m.ModId!.Value).ToHashSet();
        var installedGuids = installedMatches.Where(m => m.Guid is not null)
            .Select(m => m.Guid!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Guards against two mods sharing a dependency both queuing the same download twice. Only a
        // card still waiting or running counts: a finished one is either on disk (the scan above
        // finds it) or isn't - removed since, failed, cancelled, or only downloaded in Monitor mode -
        // and is offered again.
        var queuedIds = Items
            .Where(i => !i.IsFinished && !i.Target.IsAddon)
            .Select(i => i.Target.Id)
            .ToHashSet();

        var missing = Flatten(nodes)
            .Where(n => n.LatestCompatibleVersion is not null)
            .Where(n => !installedIds.Contains(n.Id) && !queuedIds.Contains(n.Id))
            .Where(n => n.Guid is null || !installedGuids.Contains(n.Guid))
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .ToList();

        if (missing.Count == 0) return;

        item.Token.ThrowIfCancellationRequested();

        // Prefer each cached catalog Mod when available; fall back to a minimal Mod built from
        // the dependency node's own fields.
        var depDetails = missing
            .Select(dep => (
                Dep: dep,
                Mod: AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == dep.Id)
                    ?? new Mod { Id = dep.Id, Guid = dep.Guid, Name = dep.Name, Slug = dep.Slug }))
            .ToList();

        // One gate covering every missing dependency at once: each mod's page must be opened
        // before Continue unlocks, replacing what was previously a separate Yes/No prompt plus a
        // per-dependency read-page confirmation.
        var links = depDetails
            .Select(d => new ModPageLink(
                d.Mod.Name ?? d.Dep.Name ?? Text(Strings.Downloads_UnnamedModFormat, d.Dep.Id),
                d.Mod.DetailUrl))
            .ToList();
        if (!ReadModPageConfirmationWindow.ConfirmAll(links)) return;

        item.Token.ThrowIfCancellationRequested();

        foreach (var (dep, depMod) in depDetails)
        {
            // LatestCompatibleVersion already has the Link/ContentLength needed; no further lookup required.
            var depVersion = new ModVersion
            {
                Id = dep.LatestCompatibleVersion!.Id,
                Version = dep.LatestCompatibleVersion.Version,
                Link = dep.LatestCompatibleVersion.Link,
                ContentLength = dep.LatestCompatibleVersion.ContentLength,
                FikaCompatibility = dep.LatestCompatibleVersion.FikaCompatibility,
            };

            Enqueue(
                InstallTarget.For(depMod),
                depVersion.Version ?? Strings.Common_Unknown,
                installPath,
                () => Task.FromResult<ModVersion?>(depVersion),
                checkDependencies: false,
                dependencyOf: item);
        }
    }

    // Walks a resolved dependency tree depth-first, flattening every nested level into one sequence.
    private static IEnumerable<DependencyNode> Flatten(IEnumerable<DependencyNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Dependencies)) yield return child;
        }
    }
}
