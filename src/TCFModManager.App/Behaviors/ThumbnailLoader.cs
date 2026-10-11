using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using TCFModManager.Core.Services;

namespace TCFModManager.App.Behaviors;

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

    // Decode resolution for LargeSource - the hover preview's picture (OPEN-12 A6), 360 wide on screen.
    private const int LargeDecodePixelWidth = 720;

    // In-memory cache of decoded thumbnails, keyed by URL. Only touched from the UI thread.
    private static readonly Dictionary<string, BitmapImage> Cache = new();

    // files.sp-mod.com serves images only to requests carrying a Referer from sp-mod.com itself;
    // anything else gets a 403.
    private static readonly Uri Referrer = new("https://sp-mod.com/");

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"TCFModManager/{AppVersion.Current}");
        http.DefaultRequestHeaders.Referrer = Referrer;
        return http;
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(ThumbnailLoader), new PropertyMetadata(null, OnSourceChanged));

    public static void SetSource(DependencyObject element, string? value) => element.SetValue(SourceProperty, value);

    public static string? GetSource(DependencyObject element) => (string?)element.GetValue(SourceProperty);

    // The same picture decoded big enough to be shown large, cached apart from the small one.
    public static readonly DependencyProperty LargeSourceProperty = DependencyProperty.RegisterAttached(
        "LargeSource", typeof(string), typeof(ThumbnailLoader), new PropertyMetadata(null, OnLargeSourceChanged));

    public static void SetLargeSource(DependencyObject element, string? value) => element.SetValue(LargeSourceProperty, value);

    public static string? GetLargeSource(DependencyObject element) => (string?)element.GetValue(LargeSourceProperty);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        Begin(d, e.NewValue as string, SourceProperty, DecodePixelWidth);

    private static void OnLargeSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        Begin(d, e.NewValue as string, LargeSourceProperty, LargeDecodePixelWidth);

    private static void Begin(DependencyObject d, string? url, DependencyProperty property, int width)
    {
        if (d is not Image image) return;

        image.Source = null;
        if (string.IsNullOrWhiteSpace(url)) return;

        if (Cache.TryGetValue(Key(url, width), out var cached))
        {
            image.Source = cached;
            return;
        }

        AppLog.Debug("Thumbnails", $"ThumbnailLoader: queuing {url}");
        _ = LoadAsync(image, url, property, width);
    }

    private static string Key(string url, int width) => width == DecodePixelWidth ? url : $"{width}|{url}";

    private static async Task LoadAsync(Image image, string url, DependencyProperty property, int width)
    {
        var key = Key(url, width);
        var sw = Stopwatch.StartNew();
        await Gate.WaitAsync();
        AppLog.Debug("Thumbnails", $"ThumbnailLoader: gate acquired after {sw.ElapsedMilliseconds}ms for {url}");
        try
        {
            // Re-check the cache in case another card loaded this URL while waiting on the gate.
            if (Cache.TryGetValue(key, out var cached))
            {
                if (image.GetValue(property) as string == url) image.Source = cached;
                return;
            }

            byte[] bytes;
            try
            {
                bytes = await Http.GetByteArrayAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
            {
                AppLog.Debug("Thumbnails", $"ThumbnailLoader: {url} failed after {sw.ElapsedMilliseconds}ms - {ex.Message}");
                return;
            }

            BitmapImage bitmap;
            try
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.DecodePixelWidth = width;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.EndInit();

                // Freeze so the same instance can be shared with other Images.
                bitmap.Freeze();
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or IOException or OverflowException)
            {
                AppLog.Debug("Thumbnails", $"ThumbnailLoader: {url} could not be decoded - {ex.Message}");
                return;
            }

            AppLog.Debug("Thumbnails", $"ThumbnailLoader: loaded {bytes.Length} bytes after {sw.ElapsedMilliseconds}ms total for {url}");

            Cache[key] = bitmap;
            if (image.GetValue(property) as string == url) image.Source = bitmap;
        }
        finally
        {
            Gate.Release();
        }
    }
}
