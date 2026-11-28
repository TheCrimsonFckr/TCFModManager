using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using TCFModManager.App.Behaviors;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Tours;

internal static class TourRunner
{
    private const int PollMilliseconds = 250;
    private const int ReadyTimeoutMilliseconds = 15000;

    private static readonly SettingsService Settings = new();

    private static Window? _window;
    private static TourCard? _card;
    private static DependencyObject? _root;
    private static ContentPresenter? _dialogs;

    private static Tour? _tour;
    private static bool _isHint;
    private static string? _requested;
    private static string? _whatsNewAfter;
    private static int _step;
    private static int _generation;
    private static TourRing? _ring;
    private static AdornerLayer? _ringLayer;

    public static bool IsOpen => _tour is not null;

    public static void Attach(Window window, TourCard card, DependencyObject root, ContentPresenter dialogs)
    {
        _window = window;
        _card = card;
        _root = root;
        _dialogs = dialogs;
        _whatsNewAfter = RecordRun();

        card.Acted += (_, action) => OnActed(action);

        window.LocationChanged += (_, _) => card.Reposition();
        window.SizeChanged += (_, _) => card.Reposition();
        window.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => card.Reposition()), true);
        window.Activated += (_, _) => Suspend();
        window.Deactivated += (_, _) => Suspend();
        window.StateChanged += (_, _) => Suspend();

        DependencyPropertyDescriptor
            .FromProperty(ContentPresenter.ContentProperty, typeof(ContentPresenter))
            .AddValueChanged(dialogs, (_, _) => Suspend());
    }

    public static void PageShown(Type? page)
    {
        if (_tour is not null) End(markSeen: true);

        var generation = ++_generation;
        var requested = _requested;
        _requested = null;

        if (Pick(page, requested) is { } tour) _ = StartWhenReadyAsync(tour, generation);
    }

    private static Tour? Pick(Type? page, string? requested)
    {
        var pageTour = TourCatalog.ForPage(page);
        if (pageTour is not null && string.Equals(requested, pageTour.Id, StringComparison.Ordinal)) return pageTour;

        var settings = Settings.Load();
        if (!settings.ShowFeatureTours) return null;

        if (pageTour is not null && !settings.SeenTours.Contains(pageTour.Id)) return pageTour;

        return TourCatalog.WhatsNewFor(page, _whatsNewAfter, AppVersion.Current)
            .FirstOrDefault(t => !settings.SeenTours.Contains(t.Id));
    }

    private static string? RecordRun()
    {
        var settings = Settings.Load();
        var after = settings.LastRunVersion
            ?? (string.IsNullOrWhiteSpace(settings.SptInstallPath) ? AppVersion.Current : null);

        if (!string.Equals(settings.LastRunVersion, AppVersion.Current, StringComparison.Ordinal))
        {
            settings.LastRunVersion = AppVersion.Current;
            Settings.Save(settings);
        }

        return after;
    }

    public static void Start(string id)
    {
        if (TourCatalog.ById(id) is not { } tour) return;

        _requested = tour.Id;
        if (AppNavigation.CurrentPage == tour.Page) PageShown(tour.Page);
        else AppNavigation.Navigate(tour.Page);
    }

    public static void Close()
    {
        if (_tour is not null) End(markSeen: true);
    }

    //
    // OPEN-75: one card at a target, outside any tour - shown once the page has settled and the target
    // is on screen, never over a tour, and never recorded as seen. False when it couldn't be shown.
    //
    public static async Task<bool> ShowHintAsync(string target, Func<string> title, Func<string> body)
    {
        if (_window is null || _card is null || _root is null) return false;
        if (_tour is not null && !_isHint) return false;

        await AppNavigation.AfterTransitionAsync();

        var step = new TourStep(target, title, body);
        FrameworkElement? found = null;
        for (var waited = 0; waited < 3000 && (found = FindTarget(step)) is null; waited += PollMilliseconds)
            await Task.Delay(PollMilliseconds);

        if (found is null || (_tour is not null && !_isHint)) return false;
        if (_tour is not null) End(markSeen: false);

        _tour = new Tour("hint." + target, AppNavigation.CurrentPage ?? typeof(object), [step]);
        _isHint = true;
        _step = 0;

        RemoveRing();
        found.BringIntoView();
        await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        if (!_isHint || _tour?.Steps[0] != step) return false;

        AddRing(found);
        _card.ShowHint(found, title(), body());
        Suspend();
        AppLog.Info("Tour", $"hint '{target}' shown");
        return true;
    }

    private static async Task StartWhenReadyAsync(Tour tour, int generation)
    {
        await AppNavigation.AfterTransitionAsync();

        var waited = 0;
        while (generation == _generation && !CanStart(tour))
        {
            if (tour.Ready?.Invoke() == false && waited >= ReadyTimeoutMilliseconds)
            {
                AppLog.Info("Tour", $"'{tour.Id}' not started: the page wasn't ready in time");
                return;
            }

            await Task.Delay(PollMilliseconds);
            waited += PollMilliseconds;
        }

        if (generation != _generation || _window is null) return;

        await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (generation != _generation || _tour is not null) return;

        _tour = tour;
        _step = -1;
        AppLog.Info("Tour", $"'{tour.Id}' started");
        Move(+1);
    }

    private static bool CanStart(Tour tour) =>
        tour.Ready?.Invoke() != false && !IsSuspended();

    private static bool IsSuspended() =>
        _window is null
        || !_window.IsActive
        || _window.WindowState == WindowState.Minimized
        || _dialogs?.Content is not null;

    private static void Suspend()
    {
        if (_card is null || _tour is null) return;

        var suspended = IsSuspended();
        _card.Suspend(suspended);
        if (_ring is not null) _ring.Visibility = suspended ? Visibility.Hidden : Visibility.Visible;
    }

    private static void OnActed(TourAction action)
    {
        if (_tour is null) return;

        switch (action)
        {
            case TourAction.Next:
                Move(+1);
                break;
            case TourAction.Back:
                Move(-1);
                break;
            default:
                End(markSeen: true);
                break;
        }
    }

    private static void Move(int direction)
    {
        if (_tour is null) return;

        var next = _step + direction;
        while (next >= 0 && next < _tour.Steps.Count && FindTarget(_tour.Steps[next]) is null) next += direction;

        if (next < 0) return;
        if (next >= _tour.Steps.Count)
        {
            if (_step < 0) AppLog.Info("Tour", $"'{_tour.Id}' had nothing on screen to show - left for next time");
            End(markSeen: _step >= 0);
            return;
        }

        _step = next;
        ShowStep();
    }

    private static void ShowStep()
    {
        if (_tour is null || _card is null || _window is null) return;

        var step = _tour.Steps[_step];
        if (FindTarget(step) is not { } target)
        {
            Move(+1);
            return;
        }

        RemoveRing();
        target.BringIntoView();

        var tour = _tour;
        var index = _step;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!ReferenceEquals(_tour, tour) || _step != index || _card is null) return;

            var shown = Enumerable.Range(0, tour.Steps.Count).Where(i => i == index || FindTarget(tour.Steps[i]) is not null).ToList();
            var position = shown.IndexOf(index) + 1;

            AddRing(target);
            _card.Show(target, step.Title(), step.Body(), position, shown.Count,
                canGoBack: shown.Any(i => i < index), isLast: !shown.Any(i => i > index), beside: step.Beside);
            Suspend();
        });
    }

    private static FrameworkElement? FindTarget(TourStep step) =>
        (_root is null ? null : TourTarget.Find(_root, step.Target))
        ?? (_window is null ? null : TourTarget.Find(_window, step.Target));

    private static void AddRing(FrameworkElement target)
    {
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer) return;

        _ring = new TourRing(target);
        _ringLayer = layer;
        layer.Add(_ring);
    }

    private static void RemoveRing()
    {
        if (_ring is not null) _ringLayer?.Remove(_ring);
        _ring = null;
        _ringLayer = null;
    }

    private static void End(bool markSeen)
    {
        if (_tour is not { } tour) return;

        var wasHint = _isHint;
        _tour = null;
        _isHint = false;
        _step = -1;
        RemoveRing();
        _card?.Hide();

        if (!markSeen || wasHint) return;

        var settings = Settings.Load();
        if (!settings.SeenTours.Contains(tour.Id))
        {
            settings.SeenTours.Add(tour.Id);
            Settings.Save(settings);
        }

        AppLog.Info("Tour", $"'{tour.Id}' ended");
    }
}
