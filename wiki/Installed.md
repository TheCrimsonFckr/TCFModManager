Scans your SPT folder for what's actually there - client mods in `BepInEx\plugins` and `BepInEx\patchers`, server mods in `user\mods` - and matches them back to the catalog.

Same search and filters as Browse, plus filters for update status, enabled or disabled, and which group a mod is in. Each card shows the installed version, the latest published one, and the folder it lives in when that differs from the mod name.

Clicking any mod opens a dialog with:

- the full version history, with changelogs rendered from the mod page's own rich text
- a link to the mod page
- an **Update** button when an update applies, or **Redownload** when it doesn't

Mods can be removed from here too.

**An update that would break another mod isn't offered.** sp-mod knows which versions of a mod each of your other mods accepts, and holds back a release that one of them can't take - CommonLib 3.0.6, say, while Black and Blue needs a 2.0.x. The card then says so ("Update to 3.0.6 held back: Black and Blue needs up to 2.0.31") and isn't counted as an update - not in the update filter, not by **Update selected**, not in a notification. When an older release that every one of those mods accepts is still newer than yours, that one is offered instead. You can still pick the held release in the dialog; it shows the same note.

## Mods you installed by hand
A mod this app installed is known exactly - its listing and version come from the install record. For one you installed yourself, the app reads what the mod says about itself: a client plugin's ID, name and version, and on SPT 4 a server mod's ID, name, version and the other server mods it needs. The ID matches it to its listing on sp-mod, whatever you named the folder. When nothing on sp-mod matches, the card is titled with the name the mod gives itself, with the folder shown as **Installed as**.

If a mod says it is a different version from the one you know you have, **Confirm as installed** in its details records the right one, and that wins from then on.

## Three ways to look at the list
The buttons at the top of the page switch between them. They all show the same filtered, sorted mods - only the layout changes.

- **Cards** - the paginated grid.
- **Groups** - your own MO2-style separators. Make a group, drag mods into it, collapse the ones you're not working on, and enable, disable or invert a whole group in one click. Drag a mod to the top edge of the window and the list scrolls for you.
- **List** - one row per mod, scrolling continuously. Open a row for everything the app knows about that mod: its GUID, installed and published versions, install date, group, content flags, whether this app installed it or you did by hand, and the exact folders it occupies.

## Acting on several mods at once
Turn on **Multi select**, or just Ctrl-click a mod - in any of the three views. Shift-click ticks every mod between the last one you clicked and this one, and Ctrl+A ticks everything the filters match; Esc clears it. Ticked mods carry an accent edge, and the bar above the list acts on all of them: **Update selected**, **Enable selected**, **Disable selected** and **Remove selected**. Remove selected asks once for the lot, keeps their config files, and names any mod sp-mod warns may have changed your profile.

## Updating and the right-click menu
A mod with an update shows an **Update** button on its card and its List row - one click queues it. Right-click any mod for the same actions its card has: details and versions, update, enable or disable, pin, open its folders, its sp-mod page, and remove. Right-click one of several ticked mods and the menu acts on all of them.

Sort by name, author, group or install date - **Last installed (newest)** puts what you've just installed at the top. To see only those, tick **Installed in the last 7 days** under **Show**. Every filter and sort applies to all three views.

> [!NOTE]
> Groups are yours to organise however you like - SPT never sees them. They do matter for one thing: disabling a whole group at once.

## Patchers are shown with their mod
A BepInEx patcher belongs to a mod rather than being one, so a patcher folder is shown as part of the mod that placed it, labelled `Client + Patcher`, instead of turning up as a second entry. Patchers are usually named after their mod plus a word like `Patcher` or `Prepatch`, so that's how they're matched - `MoreBotsPrepatch` finds `MoreBotsAPI`. A patcher that can't be tied to a mod is still listed, labelled `Patcher only`.

Two things in `BepInEx\patchers` are deliberately left out, the same way core SPT plugins are: SPT's own preloader patcher, and general BepInEx utilities that mods bundle alongside themselves (currently FixPluginTypesSerialization). Neither is published on sp-mod, and neither is yours to manage from here.

> [!WARNING]
> Mods installed with this app have every file they placed recorded, which is what makes a clean uninstall possible. Anything you installed by hand beforehand has no such record, so removing it takes its whole folder rather than a known file list.

## Your configs aren't thrown away
If a server mod has config files of its own (`user\mods\<mod>\config\*.json`), removing it asks what you want done with them: keep them - they're moved to a timestamped folder under `Data\LegacyConfigs\`, with their original paths intact so the folder can be copied back over your SPT install - or delete them with the rest of the mod. Updating a mod always keeps a copy, without asking, and carries your settings into the new version - see [Configs](Configs).

Client mod settings live in `BepInEx\config`, outside the mod's own folder, so removing a mod never touches them. If a mod's download ships its own settings file, it's only placed when you don't have one yet - an install or update never puts the mod's defaults over settings you've already got.

## Removing a mod can be undone
Remove doesn't delete anything straight away. The mod's files move to a hidden `.tcfmm-removed\` folder inside your SPT install, and **Undo** on the Installed page puts a removal back exactly as it was - with several held, it lists them so you can pick which, in any order. It never overwrites something that has taken a file's place since. How long removed mods are kept is up to you under **Options, Keep removed mods**: delete straight away, 1, 7, 14 (the default) or 30 days, or until you clear them; **Clear removed mods** frees the space now.

A mod whose sp-mod page warns that it may make permanent changes to your profile - many traders, SVM, Skills Extended - says so at the top of its removal question, with where to find Undo and the profile backup if the profile won't load afterwards.

A removal only takes what is provably the mod's. It leaves a file that changed since it was installed, a file another installed mod also uses, and anything belonging to SPT, BepInEx or the game - and it puts back any file the mod had replaced when it went in. The result line says what was left and why. If the mod's folder has to stay because it holds files the mod didn't install - a note you added, say - the result says so, and that folder's card is marked as left behind by the removal.

## Your SPT profiles are backed up
Before the app installs, updates, removes, disables or enables mods, or applies a mod list, it copies your SPT profiles (`user\profiles`) - whenever they have changed since the last copy, so a list of twenty mods takes one copy, not twenty. Removing a mod can leave a profile holding items SPT no longer knows, and then it won't load; putting back a copy from before brings your character back. The copies are under **Options, SPT profile backups**: **Put back** on any of them (with SPT closed), **Back up now** for one of your own, and **Open folder**. The last 10 are kept. SPT's own backups inside the profiles folder are left out.
