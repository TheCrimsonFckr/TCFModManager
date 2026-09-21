using System.Globalization;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// D15: Plural() is the one part of the localization design that can be silently wrong in a way that
// is expensive to fix later, and a one- or two-form language would never exercise it. These are the
// assertions D15 asks for, against a three-form language, written while S5 was still open.
//
public class PluralRulesTests
{
    private static PluralCategory For(string tag, int count) =>
        PluralRules.For(CultureInfo.GetCultureInfo(tag), count);

    //
    // The case D15 names. Russian picks by the LAST DIGIT, so 21 takes the same form as 1 and 22 the
    // same as 2 - which is exactly what a ternary split at 1 gets wrong, and what English can never
    // catch.
    //
    [Theory]
    [InlineData(1, PluralCategory.One)]
    [InlineData(2, PluralCategory.Few)]
    [InlineData(5, PluralCategory.Many)]
    [InlineData(21, PluralCategory.One)]
    [InlineData(22, PluralCategory.Few)]
    [InlineData(25, PluralCategory.Many)]
    public void Russian_picks_by_the_last_digit(int count, PluralCategory expected) =>
        Assert.Equal(expected, For("ru", count));

    //
    // The teens are the exception to the last-digit rule and are the half of it most easily missed:
    // 11 ends in 1 but is "many", not "one".
    //
    [Theory]
    [InlineData(0, PluralCategory.Many)]
    [InlineData(11, PluralCategory.Many)]
    [InlineData(12, PluralCategory.Many)]
    [InlineData(14, PluralCategory.Many)]
    [InlineData(101, PluralCategory.One)]
    [InlineData(111, PluralCategory.Many)]
    [InlineData(112, PluralCategory.Many)]
    [InlineData(122, PluralCategory.Few)]
    public void Russian_teens_are_many_whatever_they_end_in(int count, PluralCategory expected) =>
        Assert.Equal(expected, For("ru", count));

    [Theory]
    [InlineData(0, PluralCategory.Other)]
    [InlineData(1, PluralCategory.One)]
    [InlineData(2, PluralCategory.Other)]
    [InlineData(21, PluralCategory.Other)]
    public void English_splits_at_one(int count, PluralCategory expected) =>
        Assert.Equal(expected, For("en", count));

    //
    // The pseudo-locale has to plural like English, or a pseudo build stops exercising the same
    // branches the English build takes and its whole point is lost.
    //
    [Theory]
    [InlineData(1, PluralCategory.One)]
    [InlineData(3, PluralCategory.Other)]
    public void Pseudo_locale_plurals_like_english(int count, PluralCategory expected) =>
        Assert.Equal(expected, For("qps-ploc", count));

    //
    // A language nobody has written a rule for falls back to the two-form shape, which is the same
    // shape as the English values resource fallback is already handing it.
    //
    [Fact]
    public void A_language_with_no_rule_uses_the_two_form_shape()
    {
        Assert.Equal(PluralCategory.One, For("de", 1));
        Assert.Equal(PluralCategory.Other, For("de", 4));
    }

    //
    // Every category has a distinct suffix, and they are the six the key schema reserves. A
    // duplicate here would silently point two categories at one key.
    //
    [Fact]
    public void Every_category_has_its_own_suffix()
    {
        var all = Enum.GetValues<PluralCategory>().Select(PluralRules.Suffix).ToList();

        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(
            new[] { "_zero", "_one", "_two", "_few", "_many", "_other" }.Order(),
            all.Order());
    }

    //
    // Counts are not negative in any of this app's messages, and CLDR defines its categories over
    // absolute values - so a negative must not fall somewhere different from its positive.
    //
    [Theory]
    [InlineData("ru", 22)]
    [InlineData("en", 1)]
    public void A_negative_count_lands_where_its_positive_does(string tag, int count) =>
        Assert.Equal(For(tag, count), For(tag, -count));
}
