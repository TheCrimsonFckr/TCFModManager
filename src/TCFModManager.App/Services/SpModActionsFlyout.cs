using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace TCFModManager.App.Services;

//
// OPEN-76 R9-R11: sp-mod's own Subscribe, Endorse and Add to list buttons and the reactions bar for
// one mod, under the card button that asked for them. One small window and one web view for the
// whole app, made on first use, hidden rather than closed, and pointed at the next mod's page. The
// page is loaded and everything but the strip hidden, the way the Comments tab keeps the comments.
// A window rather than a Popup: a Popup that closes on an outside click also closes on a click into
// the web view's own window.
//
internal static class SpModActionsFlyout
{
    private const double StripWidth = 460;
    private const double MinStripHeight = 56;
    private const double MaxStripHeight = 560;
    private const double Chrome = 10;

    private static Window? _window;
    private static Border? _host;
    private static ProgressRing? _ring;
    private static TextBlock? _failed;
    private static WebView2? _view;
    private static Task<bool>? _starting;

    private static int? _shownId;
    private static Uri? _page;
    private static bool _loaded;

    public static void Open(FrameworkElement anchor, int modId)
    {
        if (PageFor(modId) is not { } page) return;

        var window = EnsureWindow();
        Place(window, anchor);
        window.Show();
        window.Activate();

        _ = ShowAsync(modId, page);
    }

    public static void Preload(int modId)
    {
        if (_view?.CoreWebView2 is null || PageFor(modId) is not { } page) return;
        Navigate(modId, page);
    }

    private static Uri? PageFor(int modId)
    {
        var mod = AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == modId);
        var url = mod?.DetailUrl ?? (mod?.Slug is { Length: > 0 } slug ? $"https://sp-mod.com/mod/{modId}/{slug}" : null);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && WebViews.IsSpModPage(uri) ? uri : null;
    }

    private static async Task ShowAsync(int modId, Uri page)
    {
        if (_view is null || _host is null) return;

        _starting ??= StartAsync();
        if (!await _starting)
        {
            _starting = null;
            ShowFailed();
            return;
        }

        if (_shownId == modId && _loaded) return;
        Navigate(modId, page);
    }

    private static async Task<bool> StartAsync()
    {
        if (_view is null || !await WebViews.InitializeAsync(_view)) return false;

        Hook(_view.CoreWebView2);
        return true;
    }

    private static void Navigate(int modId, Uri page)
    {
        if (_view?.CoreWebView2 is not { } core) return;
        if (_shownId == modId && _page == page) return;

        _shownId = modId;
        _page = page;
        _loaded = false;
        ShowLoading();

        AppLog.Debug("SpModActions", $"loading {page}");
        core.Navigate(page.AbsoluteUri);
    }

    private static void Hook(CoreWebView2 core)
    {
        core.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme == "about") return;
            if (_page is not null && uri.GetLeftPart(UriPartial.Path).Equals(_page.GetLeftPart(UriPartial.Path), StringComparison.OrdinalIgnoreCase)) return;

            e.Cancel = true;
            Leave(uri);
        };

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) Leave(uri);
        };

        core.DOMContentLoaded += async (_, _) =>
        {
            if (_shownId is not { } id) return;

            try
            {
                var result = await core.ExecuteScriptAsync(StripScript.Replace("__ID__", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                AppLog.Debug("SpModActions", $"{core.Source}: {result}");

                if (result == "\"shown\"")
                {
                    _loaded = true;
                    ShowStrip();
                }
                else
                {
                    ShowFailed();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                AppLog.Debug("SpModActions", $"could not trim the page: {ex.Message}");
                ShowFailed();
            }
        };

        core.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var json = JsonDocument.Parse(e.TryGetWebMessageAsString());
                if (json.RootElement.TryGetProperty("height", out var height) && height.TryGetDouble(out var h)) Resize(h);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
            {
                AppLog.Debug("SpModActions", $"ignored a message from the page: {ex.Message}");
            }
        };
    }

    private static void Leave(Uri uri)
    {
        _window?.Hide();

        if (WebViews.IsSignInPage(uri)) SpModWindow.Open(Application.Current?.MainWindow, uri);
        else MarkupActions.OpenInBrowser(uri.AbsoluteUri);
    }

    private static Window EnsureWindow()
    {
        if (_window is not null) return _window;

        _view = new WebView2();
        _host = new Border { Child = _view, Height = MinStripHeight };
        _ring = new ProgressRing { IsIndeterminate = true, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _failed = new TextBlock
        {
            Text = Strings.SpModActions_CouldNotLoad,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _failed.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

        var grid = new Grid();
        grid.Children.Add(_host);
        grid.Children.Add(_ring);
        grid.Children.Add(_failed);

        var frame = new Border { Child = grid, BorderThickness = new Thickness(1), Padding = new Thickness(4) };
        frame.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        var window = new Window
        {
            Content = frame,
            Width = StripWidth,
            Height = MinStripHeight + Chrome,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = true,
            Owner = Application.Current?.MainWindow,
            Title = Strings.CardField_SpModActions,
        };
        window.Deactivated += (_, _) => window.Hide();
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            window.Hide();
        };
        window.Closed += (_, _) =>
        {
            _view?.Dispose();
            _window = null;
            _host = null;
            _ring = null;
            _failed = null;
            _view = null;
            _starting = null;
            _shownId = null;
            _page = null;
            _loaded = false;
        };

        _window = window;
        return window;
    }

    private static void Place(Window window, FrameworkElement anchor)
    {
        if (PresentationSource.FromVisual(anchor) is not { CompositionTarget: { } target }) return;

        var toDips = target.TransformFromDevice;
        var below = toDips.Transform(anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight)));
        var above = toDips.Transform(anchor.PointToScreen(new Point(anchor.ActualWidth, 0)));

        var left = Math.Max(SystemParameters.VirtualScreenLeft, below.X - window.Width);
        var top = below.Y + 4;
        if (top + MaxStripHeight > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight) top = Math.Max(SystemParameters.VirtualScreenTop, above.Y - window.Height - 4);

        window.Left = left;
        window.Top = top;
    }

    private static void Resize(double contentHeight)
    {
        if (_host is null || _window is null) return;

        var height = Math.Clamp(Math.Ceiling(contentHeight), MinStripHeight, MaxStripHeight);
        _host.Height = height;
        _window.Height = height + Chrome;
    }

    private static void ShowLoading()
    {
        if (_host is null || _ring is null || _failed is null) return;

        _host.Visibility = Visibility.Hidden;
        _ring.Visibility = Visibility.Visible;
        _failed.Visibility = Visibility.Collapsed;
    }

    private static void ShowStrip()
    {
        if (_host is null || _ring is null || _failed is null) return;

        _host.Visibility = Visibility.Visible;
        _ring.Visibility = Visibility.Collapsed;
        _failed.Visibility = Visibility.Collapsed;
    }

    private static void ShowFailed()
    {
        if (_host is null || _ring is null || _failed is null) return;

        _host.Visibility = Visibility.Hidden;
        _ring.Visibility = Visibility.Collapsed;
        _failed.Visibility = Visibility.Visible;
    }

    //
    // Keeps sp-mod's action row (without Ignore mod and Create Addon, R9) and the reactions bar, and
    // hides the rest. Hidden, not removed, so the site's own scripts keep working on it. Reports the
    // height left, and again whenever it changes (a reaction picker or a dialog opening). The row is
    // trimmed again after sp-mod re-renders it. A page with neither part answers "none".
    //
    private const string StripScript = """
        (() => {
          const id = '__ID__';
          const endorse = document.querySelector(`[wire\\:key="mod-endorse-${id}"]`);
          const subscribe = document.querySelector(`[wire\\:key="mod-subscribe-${id}"]`);
          const row = (endorse || subscribe) ? (endorse || subscribe).parentElement : null;
          const reactions = document.querySelector(`[data-test="reaction-bar-mod-${id}"]`);
          const keep = [row, reactions].filter(Boolean);
          if (!keep.length) return 'none';

          const trimRow = () => {
            if (!row) return;
            for (const child of row.children) {
              const text = (child.innerText || '').trim().toLowerCase();
              if (text.includes('ignore') || text.includes('create addon') || child.querySelector('a[href*="addon/create"]')) child.style.display = 'none';
            }
          };

          const path = new Set();
          for (const part of keep) for (let node = part.parentElement; node && node !== document.documentElement; node = node.parentElement) path.add(node);

          const hide = (element) => {
            for (const child of element.children) {
              if (keep.includes(child)) continue;
              if (path.has(child)) {
                Object.assign(child.style, { display: 'block', margin: '0', padding: '0', border: '0', maxWidth: 'none', width: 'auto', minHeight: '0', position: 'static' });
                hide(child);
              } else if (!['SCRIPT', 'STYLE', 'LINK', 'TEMPLATE', 'DIALOG', 'UI-MODAL'].includes(child.tagName)) {
                child.style.display = 'none';
              }
            }
          };

          hide(document.body);
          trimRow();
          Object.assign(document.body.style, { margin: '0', padding: '8px', minHeight: '0', overflow: 'hidden' });
          for (const part of keep) Object.assign(part.style, { margin: '0 0 8px 0', justifyContent: 'flex-start' });

          const report = () => {
            const open = document.querySelector('dialog[open]');
            const height = Math.max(document.body.scrollHeight, open ? open.getBoundingClientRect().height + 32 : 0);
            window.chrome.webview.postMessage(JSON.stringify({ height: Math.ceil(height) }));
          };

          if (row) new MutationObserver(trimRow).observe(row, { childList: true, subtree: true });
          new ResizeObserver(report).observe(document.body);
          new MutationObserver(report).observe(document.body, { attributes: true, subtree: true, attributeFilter: ['open'] });
          window.scrollTo(0, 0);
          report();
          return 'shown';
        })();
        """;
}
