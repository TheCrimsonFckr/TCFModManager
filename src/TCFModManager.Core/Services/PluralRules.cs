using System.Globalization;

namespace TCFModManager.Core.Services;

//
// The CLDR plural categories. A language uses some subset of these - English two, Chinese one,
// Arabic all six - and which one a count falls into is a property of the language, not of the count.
//
// Reserved in full from the start (D11) even though nothing shipped uses more than two: the key
// schema admitting six means adding Czech or Arabic later is a resx change plus one rule here, with
// no renaming at any call site. An unused category is simply a key that does not exist.
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
// write. Per R3 a rule is written only for a language actually shipped; Russian is here anyway
// because D15 asks for the machinery to be proved against a three-form language before one ships,
// and its last-digit rule is the case a two-form language would never exercise.
//
// Lives in Core rather than beside the resx for one reason: it is the part of this design that can
// be silently wrong in a way that is expensive to fix later, and Core is what the test project can
// reach. It holds no prose, so D10 is untouched.
//
public static class PluralRules
{
    public static PluralCategory For(CultureInfo culture, int count)
    {
        // Negative counts do not occur in this app's messages, and no CLDR rule is written for them
        // anyway - the categories are defined over absolute values.
        var n = count < 0 ? -count : count;

        return culture.TwoLetterISOLanguageName switch
        {
            // qps-Ploc reports "qps". It is English with the letters mangled, so it plurals like
            // English - and it has to, or the pseudo build stops exercising the same code paths.
            "en" or "qps" => TwoForm(n),
            "ru" or "uk" => EastSlavic(n),
            _ => TwoForm(n),
        };
    }

    //
    // English, and the shape most of western Europe shares: exactly one is "one", everything else -
    // including nought - is "other".
    //
    // Also the fallback for a language with no rule, because resource fallback is already handing
    // back English values for anything untranslated. A language that needs something else must say
    // so above; French puts 0 in the singular and would be wrong here.
    //
    private static PluralCategory TwoForm(int n) =>
        n == 1 ? PluralCategory.One : PluralCategory.Other;

    //
    // Russian and Ukrainian, by the last digit - except in the teens, which are all "many".
    //
    // 1, 21, 31 are "one"; 2-4, 22-24 are "few"; 0, 5-20, 25-30 are "many". This is the rule a
    // two-form language would never catch a mistake in, which is why D15 asks for it to be proved
    // before any three-form language ships.
    //
    private static PluralCategory EastSlavic(int n)
    {
        var lastTwo = n % 100;
        if (lastTwo is >= 11 and <= 14) return PluralCategory.Many;

        return (n % 10) switch
        {
            1 => PluralCategory.One,
            2 or 3 or 4 => PluralCategory.Few,
            _ => PluralCategory.Many,
        };
    }

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
