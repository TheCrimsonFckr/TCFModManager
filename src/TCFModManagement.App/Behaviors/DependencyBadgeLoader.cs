using System.Windows;
using System.Windows.Threading;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Behaviors;

// 
// Attached behavior that shows/hides a badge based on whether a mod's latest version has any
// dependencies, with bounded concurrency and a cache that persists to disk between launches -
// same shape as ThumbnailLoader, just toggling Visibility instead of setting an image Source.
// 
// 
// The badge is set to Hidden rather than Collapsed when a mod has no dependencies, so its row
// keeps its height and cards don't reflow as answers arrive.
// 
public static class DependencyBadgeLoader
{
    // Caps concurrent dependency lookups - kept modest since this hits sp-mod.com's rate limit,
    // unlike thumbnails which load from a CDN.
    private static readonly SemaphoreSlim Gate = new(4, 4);

    private static readonly DependencyFlagStore Store = new();

    // Cache of "does this mod's latest version have dependencies", keyed by mod id, seeded from
    // disk on first use. Only touched from the UI thread.
    private static Dictionary<int, DependencyFlagStore.Entry>? _cache;

    private static Dictionary<int, DependencyFlagStore.Entry> Cache => _cache ??= Store.Load();

    private static DispatcherTimer? _saveTimer;

    private static bool _dirty;

    public static readonly DependencyProperty ModProperty = DependencyProperty.RegisterAttached(
        "Mod", typeof(Mod), typeof(DependencyBadgeLoader), new PropertyMetadata(null, OnModChanged));

    public static void SetMod(DependencyObject element, Mod? value) => element.SetValue(ModProperty, value);

    public static Mod? GetMod(DependencyObject element) => (Mod?)element.GetValue(ModProperty);

    // Writes any pending flags to disk immediately. Called on app exit so a close right
    // after a page load doesn't lose that page's results.
    public static void Flush()
    {
        _saveTimer?.Stop();
        if (!_dirty || _cache is null) return;

        _dirty = false;
        Store.Save(_cache);
    }

    private static void OnModChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        element.Visibility = Visibility.Hidden;
        if (e.NewValue is not Mod mod) return;

        if (TryGetFresh(mod, out var hasDependencies))
        {
            element.Visibility = hasDependencies ? Visibility.Visible : Visibility.Hidden;
            return;
        }

        _ = LoadAsync(element, mod);
    }

    // A cached answer counts only while it's at least as new as the mod itself - a mod
    // that has published a version since we last looked may have gained or dropped dependencies.
    private static bool TryGetFresh(Mod mod, out bool hasDependencies)
    {
        hasDependencies = false;
        if (!Cache.TryGetValue(mod.Id, out var entry)) return false;
        if (mod.UpdatedAt is { } updatedAt && entry.CheckedAt < updatedAt) return false;

        hasDependencies = entry.HasDependencies;
        return true;
    }

    private static async Task LoadAsync(UIElement element, Mod mod)
    {
        await Gate.WaitAsync();
        try
        {
            // Re-check in case another card already resolved this mod id while waiting on the gate.
            if (TryGetFresh(mod, out var cached))
            {
                if (GetMod(element)?.Id == mod.Id) element.Visibility = cached ? Visibility.Visible : Visibility.Hidden;
                return;
            }

            bool hasDependencies;
            try
            {
                var result = await AppServices.SpModApi.GetModVersionsAsync(
                    mod.Id.ToString(),
                    new ModVersionsQuery { Include = "dependencies", Sort = "-published_at", PerPage = 1 });
                hasDependencies = result.Data.FirstOrDefault()?.Dependencies?.Count > 0;
            }
            catch (Exception)
            {
                // Rate limited, network error, etc. - hide the badge rather than fail the card, and
                // don't cache the answer so it's retried rather than remembered as "no".
                if (GetMod(element)?.Id == mod.Id) element.Visibility = Visibility.Hidden;
                return;
            }

            Remember(mod.Id, hasDependencies);
            if (GetMod(element)?.Id == mod.Id) element.Visibility = hasDependencies ? Visibility.Visible : Visibility.Hidden;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void Remember(int modId, bool hasDependencies)
    {
        Cache[modId] = new DependencyFlagStore.Entry
        {
            HasDependencies = hasDependencies,
            CheckedAt = DateTimeOffset.UtcNow,
        };

        _dirty = true;
        ScheduleSave();
    }

    // Coalesces the writes from a whole page of cards into one file write a second after
    // the last card resolves.
    private static void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer!.Stop();
                if (!_dirty || _cache is null) return;

                _dirty = false;
                var snapshot = new Dictionary<int, DependencyFlagStore.Entry>(_cache);
                _ = Task.Run(() => Store.Save(snapshot));
            };
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
