using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TCFModManager.App.Behaviors;

//
// The Play page's server log drawn as a console would draw it: a dark panel whatever the app's
// theme, each line coloured by its level - errors red, warnings yellow, debug dim, the server's
// "happy playing" green - with the timestamp dimmed and the source in blue.
//
// SPT writes "[2026-10-11 17:25:02.205][Warning][Source] message"; a line that doesn't start that
// way (a stack trace, a continued message) takes the colour of the line above it.
//
// Rebuilt only when the lines change. Scrolled to the bottom it follows new lines; scrolled up to
// read, it stays where it is.
//
public static class ConsoleLog
{
    public static readonly Brush Background = Frozen(0x0C, 0x0C, 0x0C);

    private static readonly Brush Text = Frozen(0xCC, 0xCC, 0xCC);
    private static readonly Brush Dim = Frozen(0x76, 0x76, 0x76);
    private static readonly Brush Source = Frozen(0x3B, 0x8E, 0xEA);
    private static readonly Brush Error = Frozen(0xE7, 0x48, 0x56);
    private static readonly Brush Warning = Frozen(0xF9, 0xF1, 0xA5);
    private static readonly Brush Success = Frozen(0x16, 0xC6, 0x0C);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static readonly DependencyProperty LinesProperty = DependencyProperty.RegisterAttached(
        "Lines", typeof(IReadOnlyList<string>), typeof(ConsoleLog), new PropertyMetadata(null, OnLinesChanged));

    public static void SetLines(DependencyObject element, IReadOnlyList<string>? value) => element.SetValue(LinesProperty, value);

    public static IReadOnlyList<string>? GetLines(DependencyObject element) => (IReadOnlyList<string>?)element.GetValue(LinesProperty);

    private static void OnLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox box) return;

        var lines = e.NewValue as IReadOnlyList<string> ?? [];
        var old = e.OldValue as IReadOnlyList<string>;
        if (old is not null && old.Count == lines.Count && (lines.Count == 0 || old[^1] == lines[^1])) return;

        var scroll = FindScrollViewer(box);
        var atBottom = scroll is null || scroll.VerticalOffset + scroll.ViewportHeight >= scroll.ExtentHeight - 4;
        var offset = scroll?.VerticalOffset ?? 0;

        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 16 };
        var colour = Text;

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            colour = AddLine(paragraph.Inlines, lines[i], colour);
        }

        box.Document = new FlowDocument(paragraph)
        {
            PagePadding = new Thickness(0),
            Background = Background,
            Foreground = Text,
            FontFamily = box.FontFamily,
            FontSize = box.FontSize,
            PageWidth = 4000,
        };

        box.Dispatcher.BeginInvoke(() =>
        {
            if (atBottom) box.ScrollToEnd();
            else FindScrollViewer(box)?.ScrollToVerticalOffset(offset);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // One line's runs; returns the colour a following continuation line should take.
    private static Brush AddLine(InlineCollection inlines, string line, Brush carried)
    {
        if (line.Length > 0 && line[0] == '[' && line.IndexOf(']') is var stampEnd && stampEnd > 0
            && stampEnd + 1 < line.Length && line[stampEnd + 1] == '['
            && line.IndexOf(']', stampEnd + 1) is var levelEnd && levelEnd > stampEnd)
        {
            var level = line[(stampEnd + 2)..levelEnd];
            var rest = line[(levelEnd + 1)..];
            var colour = ColourFor(level, rest);

            inlines.Add(new Run(line[..(stampEnd + 1)]) { Foreground = Dim });
            inlines.Add(new Run(line[(stampEnd + 1)..(levelEnd + 1)]) { Foreground = colour == Text ? Dim : colour });

            if (rest.StartsWith('[') && rest.IndexOf(']') is var sourceEnd && sourceEnd > 0)
            {
                inlines.Add(new Run(rest[..(sourceEnd + 1)]) { Foreground = Source });
                rest = rest[(sourceEnd + 1)..];
            }

            inlines.Add(new Run(rest) { Foreground = colour });
            return colour;
        }

        inlines.Add(new Run(line) { Foreground = carried });
        return carried;
    }

    private static Brush ColourFor(string level, string message)
    {
        if (level.StartsWith("Err", StringComparison.OrdinalIgnoreCase)
            || level.StartsWith("Crit", StringComparison.OrdinalIgnoreCase)
            || level.StartsWith("Fatal", StringComparison.OrdinalIgnoreCase)) return Error;
        if (level.StartsWith("Warn", StringComparison.OrdinalIgnoreCase)) return Warning;
        if (level.StartsWith("Debug", StringComparison.OrdinalIgnoreCase)
            || level.StartsWith("Trace", StringComparison.OrdinalIgnoreCase)) return Dim;
        if (level.StartsWith("Success", StringComparison.OrdinalIgnoreCase)
            || message.Contains("happy playing", StringComparison.OrdinalIgnoreCase)) return Success;
        return Text;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } found) return found;
        }

        return null;
    }
}
