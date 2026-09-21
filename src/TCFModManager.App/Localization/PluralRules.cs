using System.Globalization;

namespace TCFModManager.App.Localization;

//
// The CLDR plural categories. A language uses some subset of these - English uses two, Chinese one,
// Arabic all six - and which one a count falls into is a property of the language, not of the count.
//
// Reserved in full from the start (D11) even though nothing shipped today uses more than two: the
// key schema admitting six means adding Czech or Arabic later is a resx change plus one rule here,
// with no renaming at any call site. An unused category is simply a key that does not exist.
//
public enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other,
}

//
// Which category a count falls into, per language.
//
// .NET exposes no CLDR plural data - CultureInfo has nothing for it - so the rules are ours to
// write. Per R3 a rule is written only for a language actually shipped, so this holds English and
// nothing else today. The design doc's §8a table is the reference for the next one.
//
// A shipped language with no rule here would be looked up with English's categories and quietly
// read the wrong forms, so S6's test asserts every tag in Meta_ShippedLanguages has a rule.
//
public static class PluralRules
{
    public static PluralCategory For(CultureInfo culture, int count)
    {
        var language = culture.TwoLetterISOLanguageName;

        // qps-Ploc reports "qps". It is English with the letters mangled, so it plurals like
        // English - and it has to, or the pseudo build stops exercising the same code paths.
        if (language is "en" or "qps") return English(count);

        // No rule written for this language. English's categories are the honest default, because
        // resource fallback is already handing back English values for anything untranslated.
        return English(count);
    }

    //
    // English, and the two-form shape most of Europe shares: exactly one is "one", everything else
    // - including nought and every negative - is "other".
    //
    // Not a general default in disguise. French puts 0 in the singular and Russian picks by the
    // last digit, so either of those shipping means a rule of its own above, not a tweak here.
    //
    private static PluralCategory English(int count) =>
        count == 1 ? PluralCategory.One : PluralCategory.Other;

    //
    // The suffix a category contributes to a key: Installed_CountFound + _one. Lower case, because
    // the rest of the schema is Page_Thing and a category is not a word of the message.
    //
    public static string Suffix(PluralCategory category) => category switch
    {
        PluralCategory.Zero => "_zero",
        PluralCategory.One => "_one",
        PluralCategory.Two => "_two",
        PluralCategory.Few => "_few",
        PluralCategory.Many => "_many",
        _ => "_other",
    };
}
