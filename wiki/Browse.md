The whole sp-mod catalog, fetched once and cached to disk so it opens instantly next time.

- **Search** by name and teaser, or by author with `@author`. Addons are searched too: a mod whose addon matches is listed with *Addon: CAG Scribbles!* on its card, and the matching addons are in an **Addons** bar above the results - collapsed until you open it, and turned off with the **Addon matches** switch. Click one for a small window with its versions and **Install**, or **Open** its mod. See [Addons](Addons#finding-an-addon)
- **Filter** by SPT release line, category, Fika compatibility, featured status, when a mod was published or last updated, and authors you follow
- **Sort** by newest, last updated, most downloaded, most favourited or most endorsed
- **Filters and view options** - a panel down the left with **SPT version** (tick the release lines to show; none ticked shows them all), **Search in** (title and teaser, or title only), the **Show only** tick boxes (hiding ads and **mods you already have installed**, **By authors you follow**, **Ignored mods**, among others), category, featured, **Published** and **Updated** (any time, the last 7, 30 or 90 days, the last year, or your own dates), **Sort by** and page size (up to 64 a page, or **Endless** - no pages, more cards load as you scroll down), each a section you open like a group - all closed when the panel opens - plus **Save as default** and **Clear filters**. The button says how many are narrowing the list; the panel's X or the button again closes it. Only search and **Refresh cache** stay in the row. **Clear filters** puts everything back to the app's own settings - your SPT line ticked, newest first - even when you've saved defaults for the page, and **Restore my defaults** puts back what you saved
- **Filter presets** - the first section of the panel. Type a name and press the save button beside it (or Enter) to keep the page's filters, sort, page size, search and pinned filters (a custom date range keeps its days); click a preset to set the page up as it, and the one the page matches is ticked. Right-click one to save the current filters to it, rename it or delete it. Pin the section to switch from the row under the search bar. Separate from **Save as default** - a preset never changes what the page opens at
- **Filter pills** - every filter narrowing the list shows above the results ("Category: Bots", "Show only: Has addons"); a pill's X drops just that one. A changed SPT version gets a pill too
- **Pinned filters** - the pin at the right of a section's name in the panel puts that filter in a row under the search bar, as a dropdown with all of its choices that reads what it's set to ("Category: Bots", "Sort by: Newest"). Pin as many as you like; they keep the panel's order, wrap onto more lines, stay pinned between launches, and stay in the panel too. **Clear filters**, **Save as default** and **Restore my defaults** don't change what's pinned. Press the pin again, or right-click the dropdown, to unpin
- **Refresh cache** re-pulls the catalog when you want the newest listings

Each card shows the download count, the endorsement count when the mod has any, a status dot, and a badge for mods that pull in dependencies. A **pin** under the status dot means you have pinned that mod, so no mod list you apply will set it aside - see [Mod lists](Mod-lists):

| Status | Meaning |
| --- | --- |
| Installed | You have it, and it's current |
| Update available | A newer version has been published |
| Disabled | You have it, but it's switched off - see [Disabling mods](Disabling-mods) |
| Not installed | Available for your SPT version |
| Nothing compatible | The mod exists, but has no release for your SPT version |

Rest the mouse on a card for about three seconds and a preview opens beside it - the whole picture, the teaser, the download count and the SPT versions it runs on. It goes as soon as the mouse moves on.

Clicking a card opens its details - the mod's full description as sp-mod lays it out, with its tabs, tables, pictures and GIFs, and a link to its page on sp-mod. Links, pictures and videos in a description open in your browser. The **Comments** tab shows the mod's comments from sp-mod itself, so you can read and post there without leaving the app (sign in once inside the tab; it remembers you). Without Microsoft's WebView2 runtime the tab offers to open them in your browser instead. Already have the mod and want a clean copy of it? Use **Redownload** - it fetches and reinstalls the version you're on, which is the quickest fix for files that got edited or corrupted.

Right-click a card for its menu: **Details and versions**, **Install** (**Reinstall** once you have it), **Download only**, **View mod page** on sp-mod, **View author's page**, **Copy mod page link**, **Copy GUID** and **Ignore**. Install and Download only do exactly what they say, whatever [Monitor mode](Monitor-mode) is set to.

> [!NOTE]
> SPT version constraints are resolved against the live SPT release list rather than parsed as version ranges, so what you see named is a release that actually exists - not the boundary version the mod author wrote the constraint against.

## Authors
Click an author's name - on a card, in a mod's details, or on the Installed page - to open their page: everything of theirs in the catalog, newest first, with the ones you have installed marked, and their addons. **Follow** them there, or from any mod's right-click menu on Installed. Following an author lets **By authors you follow** show only their mods, and their new mods come with your [update notifications](Update-notifications). Everyone you follow is listed in **Options - Authors you follow**.

## Ignoring mods
Right-click a card and pick **Ignore** to hide a mod you never want to see. It leaves Browse, its author's page and the new-mod [update notifications](Update-notifications). Ignored mods are hidden whatever else you filter by. To see them, tick **Ignored mods** under **Show only** - the page then shows only your ignored mods - and right-click one and pick **Stop ignoring** to bring it back. They're also listed in **Options - Ignored mods**, each with **View mod page** and **Stop ignoring**.

Ignoring only changes what you browse. An installed mod you ignore still updates, and can still be offered when another mod needs it. The list is kept with the app's other settings in `Data\settings.json`.
