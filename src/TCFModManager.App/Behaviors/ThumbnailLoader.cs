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
                if (GetSource(image) as string == url) image.Source = cached;
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
                bitmap.DecodePixelWidth = DecodePixelWidth;
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

            Cache[url] = bitmap;
            if (GetSource(image) as string == url) image.Source = bitmap;
        }
        finally
        {
            Gate.Release();
        }
    }
}
