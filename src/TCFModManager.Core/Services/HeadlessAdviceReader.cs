using System.Net;
using System.Text.RegularExpressions;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public static partial class HeadlessAdviceReader
{
    public const int Version = 1;

    private const RegexOptions Loose = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string Hl = @"(headless|dedicated[ -]client)";

    private const string Any = @"(?:[^.]|\.(?!\s|$))";

    private const string Apos = @"[\u2019']";

    [GeneratedRegex(@"<\s*(br|/p|/li|/h\d|/div|/td|/tr|li|p)\b[^>]*>", Loose)]
    private static partial Regex BlockTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z])")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\b" + Hl + @"\b", Loose)]
    private static partial Regex Headless();

    [GeneratedRegex(@"\b(untested|not tested|unsure|unknown|may cause|might|issues? with|maybe|experimental|known issues?)\b", Loose)]
    private static partial Regex Hedging();

    [GeneratedRegex(@"\b(inert|disables itself|is detected|does nothing)\b", Loose)]
    private static partial Regex Harmless();

    [GeneratedRegex(@"\b(don" + Apos + @"?t|do not|dont|never|shouldn" + Apos + @"?t|should not|not|no need|isn" + Apos + @"?t|useless)\b", Loose)]
    private static partial Regex Negation();

    [GeneratedRegex(
        @"(\b(don" + Apos + @"?t|do not|dont|never|shouldn" + Apos + @"?t|should not|not)\b" + Any + @"{0,60}\b(install|put|add|need|needed|required|necessary)\b" + Any + @"{0,60}\b" + Hl + ")"
        + @"|(\bnot (on|onto|to|in|for) (the |a |your )?(fika )?" + Hl + ")"
        + @"|(\b(only|just)\b" + Any + @"{0,30}\b(player|playing|graphical)\b" + Any + @"{0,20}\b(client|clients|pcs?|machines?)\b)"
        + @"|(\b(useless|not needed|isn" + Apos + @"?t needed|not required)\b" + Any + @"{0,20}\bon (the |a )?" + Hl + ")"
        + @"|(\b(do not|don" + Apos + @"?t) install anything there\b)"
        + @"|(\b(will not|won" + Apos + @"?t) load\b)|(\bcause crashes\b)|(\bbreaks when\b)",
        Loose)]
    private static partial Regex KeepOff();

    [GeneratedRegex(
        @"(\b(including|included|as well|also|every|all)\b" + Any + @"{0,40}\b" + Hl + ")"
        + @"|(\b" + Hl + @"\b" + Any + @"{0,25}\b(included|as well|too(?! +(much|many|long|slow)))\b)"
        + @"|(\b(must|needs? to|should|required)\b" + Any + @"{0,30}\b(be )?(installed|install|on|run)\b" + Any + @"{0,40}\b" + Hl + ")"
        + @"|(\binstall\b" + Any + @"{0,40}\bon the " + Hl + ")"
        + @"|(\b" + Hl + @"\b" + Any + @"{0,30}\brequires\b)",
        Loose)]
    private static partial Regex PutOn();

    public static HeadlessAdvice Read(string? descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml)) return HeadlessAdvice.Nothing;

        var avoid = new List<string>();
        var needs = new List<string>();
        var all = new List<string>();

        foreach (var unit in Units(Sentences(descriptionHtml)))
        {
            all.Add(unit);

            if (Hedging().IsMatch(unit) || Harmless().IsMatch(unit)) continue;
            if (KeepOff().IsMatch(unit)) avoid.Add(unit);
            else if (PutOn().IsMatch(unit) && !Negation().IsMatch(unit)) needs.Add(unit);
        }

        if (avoid.Count > 0 && needs.Count == 0) return new(HeadlessVerdict.Avoid, Distinct(avoid));
        if (needs.Count > 0 && avoid.Count == 0) return new(HeadlessVerdict.Needs, Distinct(needs));
        if (all.Count > 0) return new(HeadlessVerdict.Mentions, Distinct(all));
        return HeadlessAdvice.Nothing;
    }

    internal static List<string> Sentences(string html)
    {
        var text = WebUtility.HtmlDecode(AnyTag().Replace(BlockTag().Replace(html, "\n"), " "));
        var sentences = new List<string>();

        foreach (var line in text.Split('\n'))
        {
            foreach (var piece in SentenceEnd().Split(line))
            {
                var sentence = Whitespace().Replace(piece, " ").Trim();
                if (sentence.Length > 0) sentences.Add(sentence);
            }
        }

        return sentences;
    }

    private static IEnumerable<string> Units(List<string> sentences)
    {
        for (var i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            if (!Headless().IsMatch(sentence)) continue;

            var isHeading = sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 6
                || sentence.TrimEnd().EndsWith(':');

            yield return isHeading
                ? string.Join(' ', sentences.Skip(i).Take(3))
                : sentence;
        }
    }

    private static List<string> Distinct(List<string> quotes) => [.. quotes.Distinct(StringComparer.Ordinal)];
}
