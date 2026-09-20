using System.Globalization;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Services;

//
// Joining a handful of names into one readable run of text - "A", "A and B", "A, B and C".
//
// Written out rather than done with string.Join(" and ", ...) because both the separator and the
// conjunction are per-language: the comma is a different character in Chinese and Arabic, some
// languages drop the space around it, and where the conjunction goes is not universal either. A
// hardcoded " and " translates to nothing at all.
//
// The names themselves - processes, files, mods - are data and are never translated.
//
public static class TextLists
{
    public static string Join(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => string.Format(
            CultureInfo.CurrentCulture,
            Strings.Common_ListPairFormat,
            string.Join(Strings.Common_ListSeparator, items.Take(items.Count - 1)),
            items[^1]),
    };
}
