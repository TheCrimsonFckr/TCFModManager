using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// OPEN-36: which addons a Browse search matches, on the same fields and rules as a mod - name, slug,
// and teaser unless Title only; "@name" against the owner and co-authors. No search text matches
// nothing, so the Addons strip only appears while searching.
//
public static class AddonSearch
{
    public static List<Addon> Match(IEnumerable<Addon> addons, string? query, bool titleOnly)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length == 0) return [];

        if (text.StartsWith('@'))
        {
            var author = text[1..].Trim();
            return author.Length == 0
                ? []
                : [.. addons.Where(a => Contains(a.Owner?.Name, author)
                    || (a.AdditionalAuthors?.Any(o => Contains(o.Name, author)) ?? false))];
        }

        return [.. addons.Where(a => Contains(a.Name, text) || Contains(a.Slug, text) || (!titleOnly && Contains(a.Teaser, text)))];
    }

    // The mods the matched addons belong to (D1). A detached addon has none.
    public static HashSet<int> ParentIds(IEnumerable<Addon> matches) =>
        [.. matches.Where(a => a.IsDetached != true && a.ModId is not null).Select(a => a.ModId!.Value)];

    private static bool Contains(string? haystack, string needle) =>
        haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;
}
