using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace TCFModManager.App.ViewModels;

//
// Fills a bound list a few items per frame instead of all at once, when every item in it would
// need a new container anyway.
//
// Building a row is the expensive part of showing a list - WPF instantiates its template and
// measures it - at roughly 10-20ms per Installed row or card on a real install (NavProbe, 2026-10-04).
// Done in one go that was a single freeze of ~1.3s for the 107-row List and ~450ms for a page of
// cards. Spread out, each batch gets its own layout pass and frame, so the page stays responsive,
// the first rows appear at once and the rest arrive below them. The total work is the same.
//
// Only when nothing in the list survives - the first build, or a rescan that replaced every view
// model. Anything else goes through ItemsSync as before, so a filter or sort change keeps the
// containers it can, and an open card doesn't replay its opening animation (see ItemsSync).
//
// Not virtualization on purpose: a virtualized row is rebuilt every time it scrolls back into
// view, which replays that same animation on every expanded row, and it would mean reworking the
// scroll-anywhere setup on the Installed page.
//
public sealed class GradualFill<T>(int batchSize) where T : class
{
    // Bumped by every Apply, so a fill still in progress stops as soon as a newer one starts.
    private int _generation;

    public void Apply(ObservableCollection<T> target, IReadOnlyList<T> wanted)
    {
        var generation = ++_generation;

        if (wanted.Count <= batchSize || Overlaps(target, wanted))
        {
            ItemsSync.Apply(target, wanted);
            return;
        }

        target.Clear();
        for (var i = 0; i < batchSize; i++) target.Add(wanted[i]);

        _ = ContinueAsync(target, wanted, generation);
    }

    private async Task ContinueAsync(ObservableCollection<T> target, IReadOnlyList<T> wanted, int generation)
    {
        var next = batchSize;

        while (next < wanted.Count)
        {
            // Below Render and Input, so the frame holding the last batch is drawn, and any click or
            // wheel is handled, before the next batch is built.
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (generation != _generation) return;

            var end = Math.Min(next + batchSize, wanted.Count);
            for (; next < end; next++) target.Add(wanted[next]);
        }
    }

    private static bool Overlaps(ObservableCollection<T> target, IReadOnlyList<T> wanted)
    {
        if (target.Count == 0) return false;

        var current = new HashSet<T>(target, ReferenceEqualityComparer.Instance);
        return wanted.Any(current.Contains);
    }
}
