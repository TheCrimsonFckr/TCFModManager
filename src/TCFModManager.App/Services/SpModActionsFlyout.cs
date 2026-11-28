using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace TCFModManager.App.Services;

//
// OPEN-76 R9-R11: sp-mod's own Subscribe, Endorse and Add to list buttons and the mod's own sp-mod card
// (picture, title, counts, reactions) for one mod, under the card button that asked for them. One small window and one web view for the
// whole app, made on first use, hidden rather than closed, and pointed at the next mod's page. The
// page is loaded and everything but the strip hidden, the way the Comments tab keeps the comments.
// A window rather than a Popup: a Popup that closes on an outside click also closes on a click into
// the web view's own window.
//
internal static class SpModActionsFlyout
{
    private const double StripWidth = 860;
    private const double MinStripHeight = 56;
    private const double MaxStripHeight = 640;
    private const double MaxStripWidth = 1100;
    private const double BarHeight = 32;
    private const double Chrome = 10 + BarHeight;

    private static Window? _window;
    private static Border? _host;
    private static ProgressRing? _ring;
    private static TextBlock? _failed;
    private static TextBlock? _title;
    private static bool _swallowUp;
    private static WebView2? _view;
    private static Task<bool>? _starting;

    private static int? _shownId;
    private static Uri? _page;
    private static bool _loaded;

    public static void Open(FrameworkElement anchor, int modId)
    {
        if (PageFor(modId) is not { } page) return;

        var window = EnsureWindow();
        if (_shownId != modId) window.Width = StripWidth;
        if (_title is not null) _title.Text = NameFor(modId) ?? Strings.CardField_SpModActions;
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
        var url = mod?.DetailUrl
            ?? (mod?.Slug is { Length: > 0 } slug ? $"https://sp-mod.com/mod/{modId}/{slug}" : null)
            ?? (IsSelf(modId) ? SelfMod.ModPageUrl : null);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && WebViews.IsSpModPage(uri) ? uri : null;
    }

    private static string? NameFor(int modId) =>
        AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == modId)?.Name ?? (IsSelf(modId) ? SelfMod.Name : null);

    private static bool IsSelf(int modId) => modId.ToString(System.Globalization.CultureInfo.InvariantCulture) == SelfMod.ModId;

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
                var root = json.RootElement;
                var h = root.TryGetProperty("height", out var height) && height.TryGetDouble(out var hv) ? hv : 0;
                var w = root.TryGetProperty("width", out var width) && width.TryGetDouble(out var wv) ? wv : 0;
                Resize(h, w);

                if (_shownId is { } id && root.TryGetProperty("endorsed", out var endorsed)
                    && endorsed.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    AppServices.EndorsedMods.Set(id, endorsed.GetBoolean());
                }
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
        _window?.Owner?.Activate();

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

        var content = new Grid();
        content.Children.Add(_host);
        content.Children.Add(_ring);
        content.Children.Add(_failed);

        _title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        var close = new Wpf.Ui.Controls.Button
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24, FontSize = 14 },
            Appearance = ControlAppearance.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Width = 32,
            Height = 28,
            ToolTip = Strings.Common_Close,
        };

        var bar = new DockPanel { Height = BarHeight, LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        bar.Children.Add(_title);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(bar, 0);
        Grid.SetRow(content, 1);
        layout.Children.Add(bar);
        layout.Children.Add(content);

        var frame = new Border { Child = layout, BorderThickness = new Thickness(1), Padding = new Thickness(4) };
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
        close.Click += (_, _) => Dismiss(window);
        window.Deactivated += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(window.Hide));
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Dismiss(window);
        };

        if (window.Owner is { } owner)
        {
            owner.PreviewMouseDown += SwallowWhileOpen;
            owner.PreviewMouseUp += SwallowRelease;
        }
        window.Closed += (_, _) =>
        {
            _view?.Dispose();
            _window = null;
            _host = null;
            _ring = null;
            _failed = null;
            _title = null;
            _view = null;
            _starting = null;
            _shownId = null;
            _page = null;
            _loaded = false;
        };

        _window = window;
        return window;
    }

    private static void Dismiss(Window window)
    {
        window.Hide();
        window.Owner?.Activate();
    }

    // A click on the main window while the strip is open only closes the strip: it doesn't also open
    // the card or the page under the pointer.
    private static void SwallowWhileOpen(object sender, MouseButtonEventArgs e)
    {
        if (_window is not { IsVisible: true } window) return;

        e.Handled = true;
        _swallowUp = true;
        window.Hide();
    }

    private static void SwallowRelease(object sender, MouseButtonEventArgs e)
    {
        if (!_swallowUp) return;

        _swallowUp = false;
        e.Handled = true;
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

    private static void Resize(double contentHeight, double contentWidth)
    {
        if (_host is null || _window is null) return;

        if (contentHeight > 0)
        {
            var height = Math.Clamp(Math.Ceiling(contentHeight), MinStripHeight, MaxStripHeight);
            _host.Height = height;
            _window.Height = height + Chrome;
        }

        var width = Math.Min(Math.Ceiling(contentWidth) + 12, MaxStripWidth);
        if (width > _window.Width)
        {
            var grow = width - _window.Width;
            _window.Left = Math.Max(SystemParameters.VirtualScreenLeft, _window.Left - grow);
            _window.Width = width;
        }
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
    // Keeps sp-mod's action row (without Ignore mod and Create Addon, R9) and the mod's own card on the
    // page (the one holding the reactions bar), and hides the rest. Hidden, not removed, so the site's
    // own scripts keep working on it. sp-mod re-renders parts of the page after an endorsement or a
    // reaction, which undoes the hiding, so it is done again after every change to the page. Reports
    // the height of what is left, and again whenever it changes (a reaction picker or a dialog). A
    // page with neither part answers "none".
    //
    private const string StripScript = """
        (() => {
          const id = '__ID__';
          const find = () => {
            const anchor = document.querySelector(`[wire\\:key="mod-endorse-${id}"]`) || document.querySelector(`[wire\\:key="mod-subscribe-${id}"]`);
            const reactions = document.querySelector(`[data-test="reaction-bar-mod-${id}"]`);
            const card = reactions ? (reactions.closest('div.rounded-xl') || reactions) : null;
            return { row: anchor ? anchor.parentElement : null, card };
          };

          if (!find().row && !find().card) return 'none';

          const unwanted = (child) => {
            const text = (child.textContent || '').trim().toLowerCase();
            return text.includes('ignore') || text.includes('create addon') ||
              child.querySelector('a[href*="addon/create"], [wire\\:name*="ignore"]') !== null ||
              (child.getAttribute('wire:name') || '').includes('ignore');
          };

          const set = (element, styles) => {
            for (const [name, value] of Object.entries(styles)) if (element.style[name] !== value) element.style[name] = value;
          };

          const trim = () => {
            const { row, card } = find();
            const keep = [row, card].filter(Boolean);
            if (!keep.length) return keep;

            if (row) for (const child of row.children) if (unwanted(child)) set(child, { display: 'none' });

            const path = new Set();
            for (const part of keep) for (let node = part.parentElement; node && node !== document.documentElement; node = node.parentElement) path.add(node);

            const hide = (element) => {
              for (const child of element.children) {
                if (keep.includes(child)) continue;
                if (path.has(child)) {
                  set(child, { display: 'block', margin: '0', padding: '0', border: '0', maxWidth: 'none', width: 'auto', minHeight: '0', position: 'static', boxShadow: 'none', background: 'transparent' });
                  hide(child);
                } else if (!['SCRIPT', 'STYLE', 'LINK', 'TEMPLATE', 'DIALOG', 'UI-MODAL'].includes(child.tagName)) {
                  set(child, { display: 'none' });
                }
              }
            };

            hide(document.body);
            set(document.body, { margin: '0', padding: '8px', minHeight: '0', overflow: 'hidden' });
            if (row) set(row, { justifyContent: 'flex-start', margin: '0 0 8px 0' });
            if (card) set(card, { margin: '0' });
            return keep;
          };

          const report = () => {
            const keep = [find().row, find().card].filter(Boolean);
            let bottom = 0;
            let right = 0;
            for (const part of keep) {
              for (const element of [part, ...part.querySelectorAll('*')]) {
                const box = element.getBoundingClientRect();
                if (!box.width || !box.height) continue;
                bottom = Math.max(bottom, box.bottom + window.scrollY);
                right = Math.max(right, box.right);
              }
            }
            const open = document.querySelector('dialog[open]');
            if (open) bottom = Math.max(bottom, open.getBoundingClientRect().height + 32);
            const width = right > window.innerWidth ? Math.ceil(right + 16) : 0;
            const icon = document.querySelector(`[wire\\:key="mod-endorse-${id}"] button [data-flux-icon]:not(.animate-spin)`);
            const endorsed = icon ? icon.getAttribute('fill') === 'currentColor' : null;
            window.chrome.webview.postMessage(JSON.stringify({ height: Math.ceil(bottom + 8), width, endorsed }));
          };

          let pending = false;
          const observer = new MutationObserver(() => {
            if (pending) return;
            pending = true;
            setTimeout(() => {
              pending = false;
              trim();
              observer.takeRecords();
              report();
            }, 30);
          });

          trim();
          observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class', 'open'] });
          observer.takeRecords();
          new ResizeObserver(report).observe(document.body);
          window.scrollTo(0, 0);
          report();
          return 'shown';
        })();
        """;
}
