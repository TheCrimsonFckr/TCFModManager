namespace TCFModManager.App.ViewModels;

//
// Which of the Installed page's three views is showing. All three read the same filtered, sorted
// set of mods - they differ only in how much of each mod they show and how the list is arranged.
//
public enum InstalledViewMode
{
    // Paginated grid of summary cards.
    Cards,

    // Collapsible user-defined groups, drag-sortable, one compact row per mod.
    Groups,

    // One continuous scrollable list, each mod an expander opening onto its full details.
    List,
}
