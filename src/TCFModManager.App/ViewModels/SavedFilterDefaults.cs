using System.Collections.ObjectModel;

namespace TCFModManager.App.ViewModels;

//
// The small amount of translation between what a page's filter controls hold and what is written
// into settings.json - see Core's PageDefaults for why the file stores enum *names* rather than
// the enums themselves.
//
// Shared by Installed and Browse so the two pages can't disagree about what a saved default looks
// like, and so a name that no longer parses is ignored in one place rather than two.
//
internal static class SavedFilterDefaults
{
    /// <summary>The saved enum value, or null when nothing was saved or the saved name no longer
    /// exists - a filter renamed or dropped between releases must not stop a page from opening.</summary>
    public static T? Parse<T>(string? name) where T : struct, Enum =>
        Enum.TryParse<T>(name, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : null;

    //
    // Ticks exactly the saved boxes, untick everything else. An empty saved list is a real answer -
    // "no attribute filtering" - which is why the caller decides whether to call this at all rather
    // than this method treating empty as "nothing saved".
    //
    public static void ApplyAttributes(
        IEnumerable<ModAttributeOption> options,
        IReadOnlyCollection<string>? saved)
    {
        if (saved is null) return;

        var wanted = saved
            .Select(Parse<ModAttributeFilter>)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToHashSet();

        foreach (var option in options) option.IsSelected = wanted.Contains(option.Value);
    }

    /// <summary>The ticked boxes, as the names PageDefaults stores.</summary>
    public static List<string> CapturedAttributes(IEnumerable<ModAttributeOption> options) =>
        options.Where(o => o.IsSelected).Select(o => o.Value.ToString()).ToList();

    /// <summary>The saved page size, but only if it is one the dropdown actually offers - a
    /// hand-edited settings.json naming a size that isn't in the list would leave the ComboBox
    /// showing nothing.</summary>
    public static int PageSize(int? saved, IReadOnlyCollection<int> options, int fallback) =>
        saved is { } value && options.Contains(value) ? value : fallback;
}
