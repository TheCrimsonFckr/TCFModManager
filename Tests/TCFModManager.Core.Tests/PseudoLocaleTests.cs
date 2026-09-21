using Xunit;

namespace TCFModManager.Core.Tests;

//
// The pseudo-locale is the only thing that answers "is anything still hard-coded" without a
// translator, so it has to be complete. A missing form is not an error at runtime - resource
// fallback hands back the English value - so a half-generated pseudo-locale looks exactly like a
// half-keyed app.
//
// That cost a round of screenshots on 2026-09-21: the pseudo build was stale, every key added since
// fell back to English, and the app read as though S4 had never happened. A build now regenerates
// the file whether or not the flag is set, and this asserts it kept up.
//
public class PseudoLocaleTests
{
    // Not pseudo-ised on purpose: it names which languages the dropdown offers, and a mangled tag
    // list would offer none of them.
    private const string NotTranslated = "Meta_ShippedLanguages";

    [Fact]
    public void The_pseudo_locale_has_a_form_for_every_key()
    {
        Assert.True(
            File.Exists(AppSource.PseudoResx),
            $"No pseudo-locale resx at {AppSource.PseudoResx}. It is generated on every build of "
            + "TCFModManager.App, so build the app before running this.");

        var english = AppSource.Keys(AppSource.Resx).Where(k => k != NotTranslated).ToList();
        var pseudo = AppSource.Keys(AppSource.PseudoResx).ToHashSet(StringComparer.Ordinal);

        var missing = english.Where(k => !pseudo.Contains(k)).Order().ToList();

        Assert.True(
            missing.Count == 0,
            $"The pseudo-locale is behind Strings.resx by {missing.Count} keys - rebuild "
            + "TCFModManager.App:" + Environment.NewLine + string.Join(Environment.NewLine, missing.Take(20)));
    }

    //
    // The reverse: a key deleted from English but left in the pseudo-locale would keep a dead string
    // alive in the one language used to check for dead strings.
    //
    [Fact]
    public void The_pseudo_locale_holds_nothing_english_has_dropped()
    {
        if (!File.Exists(AppSource.PseudoResx)) return;

        var english = AppSource.Keys(AppSource.Resx).ToHashSet(StringComparer.Ordinal);
        var extra = AppSource.Keys(AppSource.PseudoResx).Where(k => !english.Contains(k)).Order().ToList();

        Assert.True(
            extra.Count == 0,
            "In the pseudo-locale but not in Strings.resx:" + Environment.NewLine + string.Join(Environment.NewLine, extra));
    }

    //
    // The point of the pseudo-locale is that untranslated text is obvious on sight. A form identical
    // to its English value is invisible, so it would read as a hard-coded string that had been found
    // and keyed - the exact confusion it exists to prevent.
    //
    // Values with nothing to mangle are the honest exception: "{0}", " · ", a bare number format.
    //
    [Fact]
    public void Every_pseudo_form_differs_from_its_english_value()
    {
        if (!File.Exists(AppSource.PseudoResx)) return;

        var english = Values(AppSource.Resx);
        var pseudo = Values(AppSource.PseudoResx);

        var identical = english
            .Where(pair => pair.Key != NotTranslated && pair.Key != "Meta_LanguageName")
            .Where(pair => pair.Value.Any(char.IsLetter))
            .Where(pair => pseudo.TryGetValue(pair.Key, out var p) && p == pair.Value)
            .Select(pair => pair.Key)
            .Order()
            .ToList();

        Assert.True(
            identical.Count == 0,
            "Pseudo-locale form is the same as English, so it would not stand out:"
            + Environment.NewLine + string.Join(Environment.NewLine, identical.Take(20)));
    }

    private static Dictionary<string, string> Values(string resx) =>
        System.Xml.Linq.XDocument.Load(resx).Root!.Elements("data")
            .Where(d => d.Attribute("name") is not null && d.Element("value") is not null)
            .ToDictionary(
                d => (string)d.Attribute("name")!,
                d => d.Element("value")!.Value,
                StringComparer.Ordinal);
}
