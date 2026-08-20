using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using TCFModManagement.Core.Services;

namespace TCFModManagement.App.Behaviors;

// 
// Attached behavior for loading remote thumbnail images onto an &lt;Image&gt; with bounded
// concurrency and an in-memory cache.
// 
public static class ThumbnailLoader
{
    // Caps concurrent thumbnail downloads.
    private static readonly SemaphoreSlim Gate = new(6, 6);

    // Decode resolution for thumbnails, which are only ever shown small.
    private const int DecodePixelWidth = 128;

    // In-memory cache of decoded thumbnails, keyed by URL. Only touched from the UI thread.
    private static readonly Dictionary<string, BitmapImage> Cache = new();

    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(ThumbnailLoader), new PropertyMetadata(null, OnSourceChanged));

    public static void SetSource(DependencyObject element, string? value) => element.SetValue(SourceProperty, value);

    public static string? GetSource(DependencyObject element) => (string?)element.GetValue(SourceProperty);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;

        var url = e.NewValue as string;
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url)) return;

        if (Cache.TryGetValue(url, out var cached))
        {
            image.Source = cached;
            return;
        }

        AppLog.Debug("Thumbnails", $"ThumbnailLoader: queuing {url}");
        _ = LoadAsync(image, url);
    }

    private static async Task LoadAsync(Image image, string url)
    {
        var sw = Stopwatch.StartNew();
        await Gate.WaitAsync();
        AppLog.Debug("Thumbnails", $"ThumbnailLoader: gate acquired after {sw.ElapsedMilliseconds}ms for {url}");
        try
        {
            // Re-check the cache in case another card loaded this URL while waiting on the gate.
            if (Cache.TryGetValue(url, out var cached))
            {
                image.Source = cached;
                return;
            }

            var bitmap = new BitmapImage();
            var downloadFinished = new TaskCompletionSource();
            bitmap.DownloadCompleted += (_, _) => downloadFinished.TrySetResult();
            bitmap.DownloadFailed += (_, _) => downloadFinished.TrySetResult();

            bitmap.BeginInit();
            bitmap.DecodePixelWidth = DecodePixelWidth;
            bitmap.UriSource = new Uri(url, UriKind.Absolute);
            bitmap.EndInit();

            // Assign right away so it renders as it downloads.
            if (GetSource(image) as string == url) image.Source = bitmap;

            await downloadFinished.Task;
            AppLog.Debug("Thumbnails", $"ThumbnailLoader: download finished after {sw.ElapsedMilliseconds}ms total for {url}");

            // Freeze so the same instance can be shared with other Images.
            bitmap.Freeze();
            Cache[url] = bitmap;
        }
        catch (UriFormatException)
        {
            // Malformed/missing thumbnail URL - leave the Image blank.
        }
        finally
        {
            Gate.Release();
        }
    }
}
