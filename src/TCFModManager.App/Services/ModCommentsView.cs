using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-12 A9: a mod's comments, in its details' Comments tab - sp-mod's own comments section in a
// web view (WebViews). sp-mod has no API for comments and its page loads them with its own scripts,
// so this shows the real thing, which can be read, signed in to and posted from, rather than a copy
// that could not post and would break whenever the site changed. Everything on the page but the
// comments is hidden once it has loaded. Ported from SSPTMM's SpModComments.
//
// Links to anything that is not sp-mod open in the browser. Without the WebView2 runtime the tab
// says so and offers the comments in the browser instead.
//
internal sealed class ModCommentsView(Border host, FrameworkElement none, FrameworkElement fallback) : IDisposable
{
    private WebView2? _view;
    private string? _requested;
    private bool _starting;

    public async Task ShowAsync(string url)
    {
        if (!WebViews.IsAvailable)
        {
            Show(fallback);
            return;
        }

        if (_view is null)
        {
            if (_starting) return;
            _starting = true;

            try
            {
                _view = new WebView2();
                host.Child = _view;

                if (!await WebViews.InitializeAsync(_view))
                {
                    host.Child = null;
                    _view.Dispose();
                    _view = null;
                    Show(fallback);
                    return;
                }

                Hook(_view);
            }
            finally
            {
                _starting = false;
            }
        }

        Show(host);

        if (_view.CoreWebView2 is { } web && !string.Equals(_requested, url, StringComparison.OrdinalIgnoreCase))
        {
            _requested = url;
            web.Navigate(url);
        }
    }

    private void Hook(WebView2 view)
    {
        var core = view.CoreWebView2;

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            MarkupActions.OpenInBrowser(e.Uri);
        };

        core.NavigationStarting += (_, e) =>
        {
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && !WebViews.IsSpModPage(uri) && uri.Scheme != "about")
            {
                e.Cancel = true;
                MarkupActions.OpenInBrowser(e.Uri);
            }
        };

        core.DOMContentLoaded += async (_, _) =>
        {
            try
            {
                // A mod with no comments section (its author turned them off): said, rather than
                // showing the whole mod page under a Comments tab.
                if (await core.ExecuteScriptAsync(CommentsOnlyScript) == "\"none\"") Show(none);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                AppLog.Debug("Comments", $"could not trim the page: {ex.Message}");
            }
        };
    }

    private void Show(FrameworkElement which)
    {
        host.Visibility = which == host ? Visibility.Visible : Visibility.Collapsed;
        none.Visibility = which == none ? Visibility.Visible : Visibility.Collapsed;
        fallback.Visibility = which == fallback ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Dispose()
    {
        host.Child = null;
        _view?.Dispose();
        _view = null;
    }

    //
    // Keeps the comments and nothing else. From the comments section up to the page body, every
    // other element beside the path is hidden (hidden, not removed: the site's own scripts keep
    // working on them) and each container on the path is told to use the full width. A page with no
    // comments section answers "none".
    //
    private const string CommentsOnlyScript = """
        (() => {
          const comments = document.getElementById('comments');
          if (!comments) return 'none';
          let node = comments;
          while (node && node !== document.body) {
            const parent = node.parentElement;
            if (!parent) break;
            for (const sibling of parent.children) {
              if (sibling !== node && !['SCRIPT', 'STYLE', 'LINK', 'TEMPLATE'].includes(sibling.tagName)) sibling.style.display = 'none';
            }
            parent.style.maxWidth = 'none';
            parent.style.width = '100%';
            parent.style.margin = '0';
            parent.style.padding = '0';
            parent.style.gridColumn = '1 / -1';
            node = parent;
          }
          comments.style.display = '';
          window.scrollTo(0, 0);
          return 'shown';
        })();
        """;
}
