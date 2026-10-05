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

// App-lifetime download queue that processes one download/install at a time and resolves each item's dependencies before installing it.
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

    // Raised after an item finishes installing successfully. BrowseViewModel subscribes to refresh its cards' install/update status dots.
    public event EventHandler? ItemInstalled;

    public DownloadQueueViewModel()
    {
        _ = ProcessQueueAsync();
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

        SummaryEta = remaining > 0
            && unfinished.FirstOrDefault(i => i.BytesPerSecond is > 0)?.BytesPerSecond is { } rate
                ? DownloadQueueItemViewModel.RemainingLabel(TimeSpan.FromSeconds(remaining / rate))
                : NoValue;

        OnPropertyChanged(nameof(HasRetryable));
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

    // FIFO single-reader loop that processes one item at a time for the entire app session. A failed item doesn't stop the loop.
    private async Task ProcessQueueAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            await ProcessItemAsync(item);
        }
    }

    private async Task ProcessItemAsync(DownloadQueueItemViewModel item)
    {
        // Cancelled while it sat in the queue behind something else.
        if (item.Status == DownloadQueueItemStatus.Cancelled || item.Token.IsCancellationRequested)
        {
            item.Status = DownloadQueueItemStatus.Cancelled;
            item.StatusMessage = Strings.Downloads_CancelledBeforeStart;
            return;
        }

        try
        {
            item.Status = DownloadQueueItemStatus.Downloading;
            item.StatusMessage = Strings.Downloads_ResolvingLink;

            var version = await item.ResolveVersionAsync();
            if (version?.Link is null)
            {
                item.Status = DownloadQueueItemStatus.Failed;
                item.StatusMessage = Text(
                    Strings.Downloads_NoLinkFormat, item.ModName, item.VersionLabel);
                return;
            }

            item.TotalBytes ??= version.ContentLength;

            item.Token.ThrowIfCancellationRequested();

            // Checked before this item's own download starts, so an accepted missing dependency
            // lands right behind it in the queue.
            if (item.CheckDependencies)
            {
                item.StatusMessage = Strings.Downloads_CheckingDependencies;
                await CheckDependenciesAsync(item, version);
            }

            item.Token.ThrowIfCancellationRequested();

            if (item.DownloadOnly)
            {
                await SaveArchiveAsync(item, version);
                return;
            }

            // Core reports which stage it is in; every phase but the download itself (removing the
            // previous version, extracting, copying files) is bucketed under Installing.
            var status = new Progress<ModInstallProgress>(p =>
            {
                item.StatusMessage = ModInstallWording.Describe(p);
                item.Status = p.Stage == ModInstallStage.Downloading
                    ? DownloadQueueItemStatus.Downloading
                    : DownloadQueueItemStatus.Installing;
            });
            var downloadProgress = new Progress<double>(p => item.Progress = p);

            var result = await AppServices.ModInstall.InstallAsync(
                item.Target, version, item.InstallPath, status, downloadProgress, item.Token);

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
        catch (OperationCanceledException) when (item.Token.IsCancellationRequested)
        {
            item.Status = DownloadQueueItemStatus.Cancelled;
            item.Progress = 0;
            item.StatusMessage = Text(
                Strings.Downloads_CancelledItemFormat, item.ModName, item.VersionLabel);
        }
        catch (SpModApiRateLimitedException ex)
        {
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = ApiProblems.Describe(ex);
        }
        catch (SpModApiException ex)
        {
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = ApiProblems.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = ApiProblems.Describe(ex);
        }
        catch (ModInstallException ex)
        {
            // ModInstallService refused or gave up part way; ModInstallProblems words it.
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = ModInstallProblems.Describe(ex);
        }
        catch (InvalidOperationException ex)
        {
            // Anything else that reached here already carries a readable message of its own.
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            item.Status = DownloadQueueItemStatus.Failed;
            item.StatusMessage = Text(Strings.Downloads_UnexpectedFormat, ex.Message);
        }
    }

    //
    // Monitor mode's branch: the same download, saved to the user's folder instead of installed.
    // Nothing in the SPT install is touched, so there is no running-SPT check and no ItemInstalled -
    // what is on disk has not changed.
    //
    private static async Task SaveArchiveAsync(DownloadQueueItemViewModel item, ModVersion version)
    {
        var folder = DownloadFolders.Resolve(new SettingsService().Load().Monitor.DownloadFolder);

        item.Status = DownloadQueueItemStatus.Downloading;
        item.StatusMessage = Text(Strings.Downloads_SavingToFormat, folder);

        var downloadProgress = new Progress<double>(p => item.Progress = p);

        var result = await AppServices.ModArchive.SaveArchiveAsync(
            item.Target, version, folder, item.InstallPath, item.DownloadSubfolder, downloadProgress, item.Token);

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

        // Guards against two mods sharing a dependency both queuing the same download twice. A
        // cancelled card doesn't count, so a dependency dropped earlier can be picked up again.
        var queuedIds = Items
            .Where(i => i.Status != DownloadQueueItemStatus.Cancelled && !i.Target.IsAddon)
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
