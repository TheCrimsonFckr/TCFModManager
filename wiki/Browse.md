The whole sp-mod catalog, fetched once and cached to disk so it opens instantly next time.

- **Search** by name, or by author with `@author`
- **Filter** by SPT release line, category, Fika compatibility and featured status
- **Sort** by newest, last updated, most downloaded, most favourited or most endorsed
- **Filters and view options** - a panel down the left with the **Show only** tick boxes (hiding ads and **mods you already have installed**, among others), category, featured and page size, each a section you open like a group, plus **Save as default** and **Clear filters**. The button says how many are narrowing the list; the panel's X or the button again closes it. Search, SPT version and sort stay in the row
- **Filter pills** - every filter narrowing the list shows above the results ("Category: Bots", "Show only: Has addons"); a pill's X drops just that one
- **Refresh cache** re-pulls the catalog when you want the newest listings

Each card shows the download count, the endorsement count when the mod has any, a status dot, and a badge for mods that pull in dependencies. A **pin** under the status dot means you have pinned that mod, so no mod list you apply will set it aside - see [Mod lists](Mod-lists):

| Status | Meaning |
| --- | --- |
| Installed | You have it, and it's current |
| Update available | A newer version has been published |
| Disabled | You have it, but it's switched off - see [Disabling mods](Disabling-mods) |
| Not installed | Available for your SPT version |
| Nothing compatible | The mod exists, but has no release for your SPT version |

Clicking a card opens its details, including version history and a link to its page on sp-mod. Already have the mod and want a clean copy of it? Use **Redownload** - it fetches and reinstalls the version you're on, which is the quickest fix for files that got edited or corrupted.

> [!NOTE]
> SPT version constraints are resolved against the live SPT release list rather than parsed as version ranges, so what you see named is a release that actually exists - not the boundary version the mod author wrote the constraint against.
