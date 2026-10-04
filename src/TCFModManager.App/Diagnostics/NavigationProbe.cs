using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Diagnostics;

//
// TEMPORARY - remove once the page transition lag is fixed. Times every sidebar navigation and
// writes one "NavProbe" line per page visit to the app log.
//
// WPF-UI starts its 200ms fade-and-slide when the content frame raises Navigated, before the page
// has been laid out. Animations run on the clock, not per frame, so anything the UI thread does
// between that moment and the first rendered frame is animation the user never sees. The line
// records, all in ms from the click (NavigationView.Navigating):
//
//   anim       the transition started (content frame Navigated)
//   loaded     the page's Loaded handlers began and finished (class handler runs before the
//              page's own handlers, the instance handler added here runs after them)
//   frame      the first frame rendered after Loaded - the page's first appearance on screen
//   shown      how much of the transition was left to see at that first frame
//   frames     frames rendered while the transition was running, and the longest gap between two
//   worst      the longest gap between frames in the first 1.5s, and when it started - catches
//              async refreshes landing back on the UI thread after the animation
//
internal static class NavigationProbe
{
    private const int TransitionMs = 200;
    private const int WatchMs = 1500;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly HashSet<Type> Visited = [];

    private static Visit? _current;
    private static bool _classHandlerRegistered;

    public static void Attach(NavigationView navigation)
    {
        if (!_classHandlerRegistered)
        {
            EventManager.RegisterClassHandler(typeof(Page), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnAnyPageLoadedStart));
            _classHandlerRegistered = true;
        }

        navigation.Navigating += (_, e) => Begin(e.Page);

        void HookFrame()
        {
            if (navigation.Template?.FindName("PART_NavigationViewContentPresenter", navigation) is Frame frame)
                frame.Navigated += (_, _) => Mark(v => v.Anim ??= Now(v));
        }

        if (navigation.IsLoaded) HookFrame();
        else navigation.Loaded += (_, _) => HookFrame();
    }

    private static void Begin(object page)
    {
        if (_current is not null) Finish(_current);

        var type = page.GetType();
        var visit = new Visit(page, type.Name.Replace("Page", ""), Visited.Add(type), Clock.Elapsed.TotalMilliseconds);
        _current = visit;

        if (page is FrameworkElement element)
        {
            RoutedEventHandler? after = null;
            after = (_, _) =>
            {
                element.Loaded -= after;
                if (_current == visit) visit.LoadedEnd ??= Now(visit);
            };
            element.Loaded += after;
        }

        visit.Rendering = (_, _) => OnFrame(visit);
        CompositionTarget.Rendering += visit.Rendering;
    }

    private static void OnAnyPageLoadedStart(object sender, RoutedEventArgs e)
    {
        if (_current is { } visit && ReferenceEquals(sender, visit.Page))
            visit.LoadedStart ??= Now(visit);
    }

    private static void OnFrame(Visit visit)
    {
        var at = Now(visit);

        if (visit.LastFrame is { } last)
        {
            var gap = at - last;
            if (gap > visit.WorstGap)
            {
                visit.WorstGap = gap;
                visit.WorstGapAt = last;
            }

            if (visit.Anim is { } anim && at > anim && last < anim + TransitionMs)
                visit.AnimGap = Math.Max(visit.AnimGap, gap);
        }

        if (visit.Anim is { } a && at >= a && at <= a + TransitionMs) visit.AnimFrames++;
        if (visit.LoadedEnd is not null && visit.FirstFrame is null) visit.FirstFrame = at;

        visit.LastFrame = at;

        if (at >= WatchMs) Finish(visit);
    }

    private static void Finish(Visit visit)
    {
        if (visit.Done) return;
        visit.Done = true;

        if (visit.Rendering is not null) CompositionTarget.Rendering -= visit.Rendering;
        if (_current == visit) _current = null;

        var shown = visit.Anim is { } anim && visit.FirstFrame is { } first
            ? $"{Math.Max(0, TransitionMs - (first - anim)):0}/{TransitionMs}ms"
            : "?";

        AppLog.Info("NavProbe",
            $"{visit.Name} ({(visit.FirstVisit ? "first visit" : "return")}): " +
            $"anim +{F(visit.Anim)}, loaded +{F(visit.LoadedStart)}..+{F(visit.LoadedEnd)}, " +
            $"frame +{F(visit.FirstFrame)}, shown {shown}, " +
            $"frames {visit.AnimFrames} (gap {visit.AnimGap:0}ms), " +
            $"worst {visit.WorstGap:0}ms at +{visit.WorstGapAt:0}");
    }

    private static void Mark(Action<Visit> action)
    {
        if (_current is { } visit) action(visit);
    }

    private static double Now(Visit visit) => Clock.Elapsed.TotalMilliseconds - visit.Start;

    private static string F(double? ms) => ms is { } v ? v.ToString("0") : "?";

    private sealed class Visit(object page, string name, bool firstVisit, double start)
    {
        public object Page { get; } = page;
        public string Name { get; } = name;
        public bool FirstVisit { get; } = firstVisit;
        public double Start { get; } = start;

        public double? Anim { get; set; }
        public double? LoadedStart { get; set; }
        public double? LoadedEnd { get; set; }
        public double? FirstFrame { get; set; }
        public double? LastFrame { get; set; }
        public double WorstGap { get; set; }
        public double WorstGapAt { get; set; }
        public double AnimGap { get; set; }
        public int AnimFrames { get; set; }
        public bool Done { get; set; }
        public EventHandler? Rendering { get; set; }
    }
}
