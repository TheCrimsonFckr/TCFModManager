Scans your SPT folder for what's actually there - client mods in `BepInEx\plugins` and `BepInEx\patchers`, server mods in `user\mods` - and matches them back to the catalog.

Same search and filters as Browse, plus filters for update status, enabled or disabled, and which group a mod is in. Each card shows the installed version, the latest published one, and the folder it lives in when that differs from the mod name.

Clicking any mod opens a dialog with:

- the full version history, with changelogs rendered from the mod page's own rich text
- a link to the mod page
- an **Update** button when an update applies, or **Redownload** when it doesn't
- for a mod with addons installed, how each of them fits the version you pick - see [Addons](Addons#updating-a-mod-that-has-addons)

Picking a version that isn't for your SPT asks before installing it, the same as Browse.

Mods can be removed from here too.

**An update that would break another mod isn't offered.** sp-mod knows which versions of a mod each of your other mods accepts, and holds back a release that one of them can't take - CommonLib 3.0.6, say, while Black and Blue needs a 2.0.x. The card then says so ("Update to 3.0.6 held back: Black and Blue needs up to 2.0.31") and isn't counted as an update - not in the update filter, not by **Update selected**, not in a notification. When an older release that every one of those mods accepts is still newer than yours, that one is offered instead. You can still pick the held release in the dialog; it shows the same note.

**Some held-back updates go together.** Two mods that each need an exact version of the other - ISB Aishi and ISB Special Forces, say - can never update one at a time, so sp-mod holds both back. The manager asks sp-mod whether they'd be fine updated together, and when they would, both show as updates again with "Updates together with ..." on the card. **Update**, **Update selected** and the update dialog then install them together. To find every update still held back, pick **Held back by sp-mod** in the update status filter.

**Skip an update you don't want.** Right-click a mod with an update and pick **Skip this update (version)**. Until a newer release comes out it isn't counted as an update - not in the update filter, not by **Update selected**, not in a notification - and the card says which version you're skipping. **Stop skipping** on the same menu takes it back, and the skipped release can still be installed from **Details and versions**.

**Go back after an update.** Right-click a mod you updated and pick **Go back to (version)** - the menu says how long the old version is kept. The old version comes back from the copy kept when you updated, with its configs as they were before the update, keeping any setting you changed since. A file of another mod that the update replaced goes back too. The version you left is kept the same way, so the menu can take you forward again. It's offered for as long as removed mods are kept (**Options, Keep removed mods**), and not while part of the mod is disabled or an install is running.

A line under the toolbar says when the versions shown were last read from sp-mod.

**A version that isn't for your SPT is called out.** Upgrade SPT in place and every mod used to look fine. Now a mod whose installed version is made for another SPT says so on its card - "Your version is for SPT 4.0.x, not 4.1.6. 3.0.6 runs on it." When a newer release runs on your SPT it shows as an update. When none does - or only an older one, which is named but never offered as an update - it gets the grey "nothing fits" status, so **Update selected** never downgrades anything. The answer comes from sp-mod first, then the release's listed SPT range, then the range a server mod declares in its own files; when sp-mod and the files disagree, sp-mod decides and the card names both. Where Fika is installed, a mod sp-mod marks as not working with Fika says that too. **Show - Not for this SPT** lists them, and the status line counts them with a link to that filter. Plenty of mods work on a newer SPT than they list: **Dismiss** on the card, or **Dismiss all** on the status line, stops the warning for that version on that SPT - it comes back if the mod or SPT changes, and **Warn again** on the card, or **Show all warnings** on the status line, brings it back sooner.

## Mods you installed by hand
A mod this app installed is known exactly - its listing and version come from the install record. For one you installed yourself, the app reads what the mod says about itself: a client plugin's ID, name and version, and on SPT 4 a server mod's ID, name, version and the other server mods it needs. The ID matches it to its listing on sp-mod, whatever you named the folder. When nothing on sp-mod matches, the card is titled with the name the mod gives itself, with the folder shown as **Installed as**.

If a mod says it is a different version from the one you know you have, **Confirm as installed** in its details records the right one, and that wins from then on.

**Link it to its sp-mod page.** When nothing matches, right-click the mod, pick **Link to its sp-mod page...** and paste the link of its mod or addon page. The card takes that page's name, page and updates; nothing on disk changes. An addon that lives inside its mod's folder is linked from the mod's menu - **Link an addon in this folder...**, paste the addon's link, and tick which files are the addon's - and then gets a card of its own. **Files of (addon)...** on the mod's menu changes the files. **Clear override** in the details takes a link off, and once the app installs or updates the mod itself, its own record takes over. Links are kept in `Data\mod-links.json`.

## Three ways to look at the list
The buttons at the top of the page switch between them. They all show the same filtered, sorted mods - only the layout changes.

- **Cards** - the paginated grid, up to 64 a page, or **Endless** - no pages, more cards load as you scroll down. Resting the mouse on a card shows the same preview as Browse.
- **Groups** - your own MO2-style separators. Make a group, drag mods into it, collapse the ones you're not working on, and enable, disable or invert a whole group in one click. Drag a mod to the top edge of the window and the list scrolls for you. See [Groups](#groups) below.
- **List** - one row per mod, scrolling continuously. Open a row for everything the app knows about that mod: its GUID, installed and published versions, install date, group, content flags, whether this app installed it or you did by hand, and the exact folders it occupies.

Installed has the same **Filter presets** section at the top of its Filters panel as [Browse](Browse); here a preset also keeps which view you were in. A preset naming a group you've since deleted shows all groups and says so.

## Groups
- **Putting mods in a group** - drag them in, or right-click a mod and open **Add to group**: pick a group, **New group...** to make one, or **Remove from group** to put it back in Ungrouped. With several mods ticked, the menu and a drag both move all of them.
- **Icons** - click the small picture button before a group's name to give it an icon. There are 31, drawn for the app in the same style as the rest of its icons, with a search box; **No icon** takes it off. The icon shows on the group's header, on its mods' group chips in every view, in the Group filter and in the Add to group menu. Groups have no icon until you give them one.
- **Colours** - click the dot after a group's icon to give it one of 11 colours; the dashed circle takes it off. The group's header and its chip on cards and List rows take a soft tint of it - the text keeps your theme's colour - and the Group filter and the Add to group menu show a dot. Groups have no colour until you give them one.
- **Renaming** a group to a name another group already has offers to merge the two.

## Notes on a mod
Right-click a mod and pick **Add a note...** for a line of your own - *needs a config change*, *broke with X*. It shows under the mod's name in every view and the search finds it. **Edit note...** changes it; clear the box to take it off. Notes are kept in `Data\mod_notes.json`.

## Acting on several mods at once
Turn on **Multi select**, or just Ctrl-click a mod - in any of the three views. Shift-click ticks every mod between the last one you clicked and this one, and Ctrl+A ticks everything the filters match; Esc clears it. Ticked mods carry an accent edge, and the bar above the list acts on all of them: **Update selected**, **Enable selected**, **Disable selected** and **Remove selected**. Remove selected asks once for the lot, keeps their config files, and names any mod sp-mod warns may have changed your profile. If a selected mod has addons you didn't tick, it asks first whether those go too.

## Updating and the right-click menu
A mod with an update shows an **Update** button on its card and its List row - one click queues it. A disabled mod has the button too, and stays disabled after the update. Right-click any mod for the same actions its card has: details and versions, update, enable or disable, pin, open its folders, its sp-mod page, and remove. A mod with addons installed, and each of those addons, also has **Show <mod> and its addons**, which narrows the page to that family. Right-click one of several ticked mods and the menu acts on all of them.

The menu also copies: **Copy "..."** at the top takes the text you right-clicked, and **Copy name**, **Copy version**, **Copy mod page link** and **Copy GUID** take the mod's own. With several mods ticked, **Copy list** copies each one with its version, a line each - handy for a bug report.

Sort by name, author, group, install date, or **Enabled first** / **Disabled first** - **Last installed (newest)** puts what you've just installed at the top. To see only those, tick **Installed in the last 7 days** under **Show only**. **Has installed addons** there lists every mod with addons installed, together with those addons. Every filter and sort applies to all three views.

Search sits in the row above the list. Everything else is in the **Filters and view options** panel down the left - update status, enabled or disabled, the **Show only** tick boxes, category, group, sort, how the groups are ordered (Groups view) and page size (Cards view), with **Save as default** and **Clear filters**. Each is a section you open like a group, and all start closed. **Clear filters** puts everything back to the app's own settings, even when you've saved defaults for the page; **Restore my defaults** puts back what you saved. The button says how many filters are narrowing the list, and the panel's X or the button again closes it. On a wide window the panel takes its own column and the mods move over; on a narrow one it lies over them and a click elsewhere closes it.

Every filter narrowing the list also shows as a pill above the mods - "Update status: Updates available", "Show only: Has conflicts". A pill's X drops just that filter.

Pin the sections you use most and they sit in a row under the search bar as dropdowns, the same as on [Browse](Browse) - the pin is at the right of each section's name in the panel. A pinned **Sort groups** shows only in Groups view and **Per page** only in Cards view, as in the panel.

Disabled mods carry a red **DISABLED** tag, in every view.

A mod's author is a link to their page, and the right-click menu has **More by** and **Follow** for them - see [Browse](Browse#authors).

> [!NOTE]
> Groups are yours to organise however you like - SPT never sees them. They do matter for one thing: disabling a whole group at once.

## Patchers are shown with their mod
A BepInEx patcher belongs to a mod rather than being one, so a patcher folder is shown as part of the mod that placed it, labelled `Client + Patcher`, instead of turning up as a second entry. Patchers are usually named after their mod plus a word like `Patcher` or `Prepatch`, so that's how they're matched - `MoreBotsPrepatch` finds `MoreBotsAPI`. A patcher that can't be tied to a mod is still listed, labelled `Patcher only`.

Two things in `BepInEx\patchers` are deliberately left out, the same way core SPT plugins are: SPT's own preloader patcher, and general BepInEx utilities that mods bundle alongside themselves (currently FixPluginTypesSerialization). Neither is published on sp-mod, and neither is yours to manage from here.

## Mods that share a folder
Some mods install into another mod's folder. SPT Stance Overhaul and SPT Realism Common Lib, for example, both go into `BepInEx\plugins\Realism`. Each one still gets its own card, with its own version, updates and dependencies, and a line under its name saying where it lives: *Installed in the Realism folder, shared with SPT Realism Common Lib*.

The app works out which files are whose from what it installed, and for mods you installed by hand, from the ID each plugin declares. Everything you do to one of them touches only its files:

- **Disable** moves just that mod's files into `plugins.disabled`. If another mod in the folder needs it, you're asked whether to disable that one too.
- **Remove** takes only that mod's files. The folder and the other mods stay.
- **Update** replaces only that mod's files.

The first mod to arrive keeps the folder's name for groups and pins. Later arrivals are named after their DLL, such as `spt-stance-overhaul`, so a mod already in your groups isn't renamed when another one moves in.

Addons that install into their mod's folder get their own card the same way, with *Addon for* and the mod under their name. The mod's card doesn't repeat the shared-folder line for its own addons. Removing or disabling the mod asks whether to take its addons along - see [Addons](Addons#installed-addons).

> [!WARNING]
> Mods installed with this app have every file they placed recorded, which is what makes a clean uninstall possible. Anything you installed by hand beforehand has no such record, so removing it takes its whole folder rather than a known file list.

## Your configs aren't thrown away
If a server mod has config files of its own (`user\mods\<mod>\config\*.json`), removing it asks what you want done with them: keep them - they're moved to a timestamped folder under `Data\LegacyConfigs\`, with their original paths intact so the folder can be copied back over your SPT install - or delete them with the rest of the mod. Updating a mod always keeps a copy, without asking, and carries your settings into the new version - see [Configs](Configs).

Client mod settings live in `BepInEx\config`, outside the mod's own folder, so removing a mod never touches them. If a mod's download ships its own settings file, it's only placed when you don't have one yet - an install or update never puts the mod's defaults over settings you've already got.

## Removing a mod can be undone
Remove doesn't delete anything straight away. The mod's files move to a hidden `.tcfmm-removed\` folder inside your SPT install, and **Undo** on the Installed page puts a removal back exactly as it was - with several held, it lists them so you can pick which, in any order. It never overwrites something that has taken a file's place since. A disabled mod doesn't need enabling first: it is removed from where it is, and Undo puts it back disabled. A mod in both an enabled and a disabled folder has to be sorted out with **Sort out** first. How long removed mods are kept is up to you under **Options, Keep removed mods**: delete straight away, 1, 7, 14 (the default) or 30 days, or until you clear them; **Clear removed mods** frees the space now.

A mod whose sp-mod page warns that it may make permanent changes to your profile - many traders, SVM, Skills Extended - says so at the top of its removal question, with where to find Undo and the profile backup if the profile won't load afterwards.

A removal only takes what is provably the mod's. It leaves a file that changed since it was installed, a file another installed mod also uses, and anything belonging to SPT, BepInEx or the game - and it puts back any file the mod had replaced when it went in. The result line says what was left and why. If the mod's folder has to stay because it holds files the mod didn't install - a note you added, say - the result says so, and that folder's card is marked as left behind by the removal.

## Profiles are checked before mods go
Before mods are removed or disabled - here or by a mod list - the app reads this install's profiles (`user\profiles`, headless ones included) and, when any of them hold something from those mods, says so in the question: each profile and mod with how many items and traders, opening to the items by name. A mod that adds its own values to the server through an SPT 4.1 prepatch - Skills Extended's skills, ISB Special Forces' bot types - is named too: once it's off, a profile holding those values won't open in the game, and once it's removed, the server can't load it.

**Clean the profiles while you're at it.** The same question lists every profile with something to take out, each ticked: the mods' items (with everything inside or attached to them, from the stash, mail, insurance and builds), their traders, their skills, and a bot role of theirs in the last cause of death. Untick any profile you'd rather leave alone. Once the mods have gone, the ticked profiles are cleaned and a **Profiles cleaned** window lists what came out of each. Every profile is backed up first, and it's all or none - if one can't be cleaned, none are changed. SPT's own clean-up of mod items in `core.json` is turned on too when it's off, so it catches anything left. Skill progress taken out doesn't come back if you turn the mod on again; the backup is the way back. For mods already gone, see [Diagnose logs](Diagnose-logs#a-profile-spt-wont-load).

## Your SPT profiles are backed up
Before the app installs, updates, removes, disables or enables mods, or applies a mod list, it copies your SPT profiles (`user\profiles`) - whenever they have changed since the last copy, so a list of twenty mods takes one copy, not twenty. Removing a mod can leave a profile holding items SPT no longer knows, and then it won't load; putting back a copy from before brings your character back. The copies are under **Options, SPT profile backups**: **Restore** on any of them (with SPT closed), **Back up now** for one of your own, and **Open folder**. The last 10 are kept. SPT's own backups inside the profiles folder are left out.
