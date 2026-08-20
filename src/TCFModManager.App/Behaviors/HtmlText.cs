using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace TCFModManager.App.Behaviors;

// 
// Attached property that renders an HTML fragment into a RichTextBox's FlowDocument.
// 
public static class HtmlText
{
    public static readonly DependencyProperty HtmlProperty = DependencyProperty.RegisterAttached(
        "Html", typeof(string), typeof(HtmlText), new PropertyMetadata(null, OnHtmlChanged));

    public static void SetHtml(DependencyObject element, string? value) => element.SetValue(HtmlProperty, value);

    public static string? GetHtml(DependencyObject element) => (string?)element.GetValue(HtmlProperty);

    private static void OnHtmlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox richTextBox) return;

        richTextBox.Document = HtmlFragmentParser.Parse(e.NewValue as string);
    }
}

// 
// A small hand-rolled HTML-fragment-to-FlowDocument converter for a limited tag set.
// 
internal static class HtmlFragmentParser
{
    // Matches a tag (open, close, or self-closing) or a run of plain text.
    private static readonly Regex TagOrText =
        new(@"<(?<close>/?)(?<tag>[a-zA-Z0-9]+)(?<attrs>[^>]*)>|(?<text>[^<]+)", RegexOptions.Compiled);

    private static readonly Regex HrefAttribute =
        new("href\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    public static FlowDocument Parse(string? html)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0) };
        if (string.IsNullOrWhiteSpace(html)) return document;

        // Where new top-level blocks (Paragraphs, Lists) get added.
        var blockTargets = new Stack<BlockCollection>();
        blockTargets.Push(document.Blocks);

        // The <ul>/<ol> currently open, if any.
        var openLists = new Stack<List>();

        // Inline formatting elements (Bold/Italic/Underline/Hyperlink) currently open, innermost on top.
        var openInlines = new Stack<Span>();

        Paragraph? currentParagraph = null;

        void EnsureParagraph()
        {
            if (currentParagraph is not null) return;
            currentParagraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            blockTargets.Peek().Add(currentParagraph);
        }

        void EndParagraph() => currentParagraph = null;

        InlineCollection InlineTarget()
        {
            if (openInlines.Count > 0) return openInlines.Peek().Inlines;
            EnsureParagraph();
            return currentParagraph!.Inlines;
        }

        void AddText(string raw)
        {
            var text = WhitespaceRun.Replace(WebUtility.HtmlDecode(raw), " ");
            if (text.Length == 0) return;

            // Drop a lone whitespace gap between block-level tags with no inline content yet.
            if (text == " " && currentParagraph is null && openInlines.Count == 0) return;

            InlineTarget().Add(new Run(text));
        }

        void OpenInline(Span span)
        {
            InlineTarget().Add(span);
            openInlines.Push(span);
        }

        void CloseInline()
        {
            if (openInlines.Count > 0) openInlines.Pop();
        }

        void OpenHeading(int level)
        {
            EndParagraph();
            currentParagraph = new Paragraph
            {
                FontWeight = FontWeights.SemiBold,
                FontSize = level switch { 1 => 20, 2 => 18, 3 => 16, _ => 14 },
                Margin = new Thickness(0, 8, 0, 4),
            };
            blockTargets.Peek().Add(currentParagraph);
        }

        void OpenBlockquote()
        {
            EndParagraph();
            currentParagraph = new Paragraph
            {
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(12, 4, 0, 4),
            };
            blockTargets.Peek().Add(currentParagraph);
        }

        foreach (Match match in TagOrText.Matches(html))
        {
            if (match.Groups["text"].Success)
            {
                AddText(match.Groups["text"].Value);
                continue;
            }

            var isClose = match.Groups["close"].Value == "/";
            var tag = match.Groups["tag"].Value.ToLowerInvariant();

            switch (tag)
            {
                case "p":
                case "div":
                    EndParagraph();
                    break;

                case "br":
                    InlineTarget().Add(new LineBreak());
                    break;

                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    if (!isClose) OpenHeading(tag[1] - '0');
                    else EndParagraph();
                    break;

                case "blockquote":
                    if (!isClose) OpenBlockquote();
                    else EndParagraph();
                    break;

                case "strong" or "b":
                    if (!isClose) OpenInline(new Bold()); else CloseInline();
                    break;

                case "em" or "i":
                    if (!isClose) OpenInline(new Italic()); else CloseInline();
                    break;

                case "u":
                    if (!isClose) OpenInline(new Underline()); else CloseInline();
                    break;

                case "code":
                    if (!isClose) OpenInline(new Span { FontFamily = new System.Windows.Media.FontFamily("Consolas") });
                    else CloseInline();
                    break;

                case "a":
                    if (!isClose)
                    {
                        var hyperlink = new Hyperlink { Foreground = System.Windows.Media.Brushes.DodgerBlue };
                        var href = HrefAttribute.Match(match.Groups["attrs"].Value);
                        if (href.Success && Uri.TryCreate(href.Groups[1].Value, UriKind.Absolute, out var uri))
                        {
                            hyperlink.NavigateUri = uri;
                            hyperlink.Click += OpenHyperlink;
                        }
                        OpenInline(hyperlink);
                    }
                    else
                    {
                        CloseInline();
                    }
                    break;

                case "ul" or "ol":
                    if (!isClose)
                    {
                        EndParagraph();
                        var list = new List
                        {
                            MarkerStyle = tag == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                            Margin = new Thickness(0, 0, 0, 8),
                        };
                        blockTargets.Peek().Add(list);
                        openLists.Push(list);
                    }
                    else if (openLists.Count > 0)
                    {
                        openLists.Pop();
                    }
                    break;

                case "li":
                    if (!isClose)
                    {
                        EndParagraph();
                        if (openLists.Count > 0)
                        {
                            var item = new ListItem();
                            openLists.Peek().ListItems.Add(item);
                            blockTargets.Push(item.Blocks);
                        }
                    }
                    else
                    {
                        EndParagraph();
                        if (blockTargets.Count > 1) blockTargets.Pop();
                    }
                    break;

                // Anything else (span, font, unknown tags) is unwrapped.
            }
        }

        return document;
    }

    private static void OpenHyperlink(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { NavigateUri: { } uri }) return;

        // UseShellExecute opens the URL in the OS's default browser.
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
