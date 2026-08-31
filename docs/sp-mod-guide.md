**TCF Mod Manager** is a Windows desktop app for finding, installing and keeping track of your SPT mods, built directly against the sp-mod catalog you're reading this on. Browse the full mod list, filter it down to what actually works on your SPT version, install with dependencies resolved for you, turn mods on and off without deleting anything, and see at a glance what's out of date.

WPF with Fluent Design, .NET 9, no account or API key needed.

#### information
The released build is self-contained - you don't need to install .NET separately.


## Guide {.tabset}

### Install

**You need**

- Windows
- An SPT install

**Steps**

1. Download `TCF-ModManager-<version>.zip` from this page.
2. Extract it into your SPT folder as `<SPT root>\TCFModManager\` - a sibling of `BepInEx\` and `user\`.
3. Run `TCFModManager.exe`.
4. Open **Options**, point it at your SPT install folder, and hit Save. The detected server version appears underneath - everything else keys off that.

#### warning
This is **not** a mod. Don't extract it into `BepInEx\plugins` or `user\mods`. It's a standalone application that manages that folder for you. Anywhere on disk works, really - it just needs to be told where SPT lives.


Once it's set up, the app keeps itself up to date from its own page here on sp-mod - see the **App updates** tab.

#### If the SPT version doesn't detect

The version is read from the server executable - `SPT.Server.exe`, or the older `Aki.Server.exe` - at the install root or under `SPT\` / `SPT_Runtime\`. Point Options at the folder that contains one of those, not at a subfolder.

### Browse

The whole sp-mod catalog, fetched once and cached to disk so it opens instantly next time.

- **Search** by name, or by author with `@author`
- **Filter** by SPT release line, category, Fika compatibility and featured status
- **Sort** by newest, last updated, most downloaded, most favourited or most endorsed
- **Toggles** for hiding ads and AI-generated content
- **Refresh cache** re-pulls the catalog when you want the newest listings

Each card shows the download count, the endorsement count when the mod has any, a status dot, a badge for mods that pull in dependencies, and a badge counting the mod's addons:

| Status | Meaning |
| --- | --- |
| Installed | You have it, and it's current |
| Update available | A newer version has been published |
| Disabled | You have it, but it's switched off - see the Disabling mods tab |
| Not installed | Available for your SPT version |
| Nothing compatible | The mod exists, but has no release for your SPT version |

Clicking a card opens its details, including version history and a link to its page here. Already have the mod and want a clean copy of it? Use **Redownload** - it fetches and reinstalls the version you're on, which is the quickest fix for files that got edited or corrupted.

#### information
SPT version constraints are resolved against the live SPT release list rather than parsed as version ranges, so what you see named is a release that actually exists - not the boundary version the mod author wrote the constraint against.


### Addons

Some mods have **addons** - extra content published against the mod itself rather than against an SPT release. A voice-command pack for a companion mod, a Linux build of a GUI, an overlay for a web tool.

Addons aren't listed separately, because an addon is no use without its parent. Open a mod - from Browse or from Installed - and its addons are listed at the bottom of the dialog, each with its own version picker and Install button.

The thing that makes an addon different from a mod is what it's measured against:

| A mod version says | An addon version says |
| --- | --- |
| which **SPT release** it needs | which version of its **parent mod** it needs |

So the app checks each addon version against the mod you actually have installed. If none of them fit, the addon is still listed - with the reason, rather than quietly missing:

> Needs Raid Review ^1.5.0 - you have 1.4.2

Installed addons get their own card on the Installed page, labelled with the mod they belong to, and update the same way anything else does. **Mod lists cover them too** - capturing a list records your addons, and applying one installs, updates or disables them alongside your mods.

#### information
Installing an addon won't drag its parent mod in behind it - install the mod first, then its addon. Anything else the addon needs is offered the usual way, on the Downloads page. Applying a list that names an addon whose parent mod isn't installed (and isn't on that list either) skips it and tells you why, rather than downloading something nothing would load.

#### warning
A mod list containing an addon needs this version of the app or newer to open. Sharing one with someone on an older build will tell them the file is too new - send them a list without addons, or ask them to update. Lists with no addons in them are unaffected.

#### warning
Only an addon installed **through this app** is tracked as one. Addons have no GUID on sp-mod, so an addon you unzipped by hand can't be told apart from an ordinary mod folder - it'll show up as a mod the app couldn't match.


### Installed

Scans your SPT folder for what's actually there - client mods in `BepInEx\plugins` and `BepInEx\patchers`, server mods in `user\mods` - and matches them back to the catalog.

Same search and filters as Browse, plus filters for update status, enabled or disabled, and which group a mod is in. Each card shows the installed version, the latest published one, and the folder it lives in when that differs from the mod name.

Clicking any mod opens a dialog with:

- the full version history, with changelogs rendered from the mod page's own rich text
- a link to the mod page
- an **Update** button when an update applies, or **Redownload** when it doesn't

Mods can be removed from here too.

#### Three ways to look at the list

The buttons at the top of the page switch between them. They all show the same filtered, sorted mods - only the layout changes.

- **Cards** - the paginated grid. Turn on **Select** mode to tick several mods and act on them together.
- **Groups** - your own MO2-style separators. Make a group, drag mods into it, collapse the ones you're not working on, and enable, disable or invert a whole group in one click. Drag a mod to the top edge of the window and the list scrolls for you.
- **List** - one row per mod, scrolling continuously. Open a row for everything the app knows about that mod: its GUID, installed and published versions, install date, group, content flags, whether this app installed it or you did by hand, and the exact folders it occupies.

Sort by name, author, group or install date. Every filter and sort applies to all three views.

#### information
Groups are yours to organise however you like - SPT never sees them. They do matter for one thing: disabling a whole group at once.


#### Patchers are shown with their mod

A BepInEx patcher belongs to a mod rather than being one, so a patcher folder is shown as part of the mod that placed it, labelled `Client + Patcher`, instead of turning up as a second entry. Patchers are usually named after their mod plus a word like `Patcher` or `Prepatch`, so that's how they're matched - `MoreBotsPrepatch` finds `MoreBotsAPI`. A patcher that can't be tied to a mod is still listed, labelled `Patcher only`.

Two things in `BepInEx\patchers` are deliberately left out, the same way core SPT plugins are: SPT's own preloader patcher, and general BepInEx utilities that mods bundle alongside themselves (currently FixPluginTypesSerialization). Neither is published on sp-mod, and neither is yours to manage from here.

#### warning
Mods installed with this app have every file they placed recorded, which is what makes a clean uninstall possible. Anything you installed by hand beforehand has no such record, so removing it deletes its whole folder rather than a known file list.


#### Your configs aren't thrown away

If a server mod has config files of its own (`user\mods\<mod>\config\*.json`), removing it asks what you want done with them: keep them - they're moved to a timestamped folder under `LegacyConfigs\` next to the exe, with their original paths intact so the folder can be copied back over your SPT install - or delete them with the rest of the mod. Updating a mod always keeps them, without asking.

Client mod settings live in `BepInEx\config`, outside the mod's own folder, so removing a mod never touches them.

### Disabling mods

Turn a mod off for a run without uninstalling it. Nothing is deleted, and nothing is lost.

Disabling moves the mod into a `.disabled` copy of the folder SPT loads it from - `user\mods` becomes `user\mods.disabled`, `BepInEx\plugins` becomes `BepInEx\plugins.disabled`. SPT doesn't look in those folders, so the mod simply isn't loaded. Enabling moves it straight back where it came from.

Its own files travel with it, server configs included, and client settings in `BepInEx\config` are never touched - so switching a mod off and back on again loses no settings.

**How to disable something**

- One mod, from its card, its List row, or its row in Groups view
- Several at once, by ticking them in Cards view's **Select** mode
- A whole group, with its **enable all** / **disable all** / **invert** buttons

Disabled mods stay in the list, dimmed and marked, and the enabled/disabled filter pulls up either set on its own.

**You get a warning before you break something.** If disabling a mod would take away something another mod depends on - or if you enable a mod whose own dependencies are still switched off - the app lists what's affected and offers to carry those along. Dependencies are read from the mods themselves, so this works offline and covers mods you installed by hand that were never matched to a listing here.

**Undo** puts the last change back.

#### information
Whether a mod is disabled is worked out purely from where it sits on disk. Move folders around by hand if you prefer - the app reads the result correctly on the next scan.


#### warning
**Update, Redownload and Remove are unavailable while a mod is disabled.** The record of what it installed points at folders it no longer occupies, so those actions would put files in the wrong place. Enable the mod first, then update it. Browse refuses a reinstall of a disabled mod for the same reason.


#### If a mod ends up in two places at once

A move interrupted partway - or one done by hand - can leave the same mod in both the normal folder and the `.disabled` one. When that happens the card says so and offers **Sort out**: pick which copy to keep, and the other is moved into a hidden `.tcfmm-duplicates` folder inside your SPT install rather than deleted. Undo puts that back too.

### Dependencies

Resolves the dependency tree of every installed mod that declares one, and reports each dependency's state against what's actually on disk.

That includes **version conflicts** - where two installed mods want incompatible versions of the same dependency - which is the failure mode that usually shows up as an unexplained crash on load rather than an error message. Dependencies you've disabled are called out as disabled rather than missing.

Anything missing can be installed straight from the list.

### Downloads

The install queue. Items process one at a time; each resolves its dependencies and queues those alongside it.

- Live progress per item
- **Cancel** on any individual item - cancelling a mod also cancels the dependencies it dragged in
- **Clear finished** to tidy up
- Plain archive downloads, for when you'd rather install something by hand

#### How an install actually runs

The archive is downloaded and extracted into a hidden scratch folder inside your SPT install (`.tcfmm-work\`, swept of stale runs each time), then moved into place.

When you're updating, the previous version is only removed **after** the new one has downloaded and extracted successfully - a failed or cancelled download can't leave you with neither. Once files start being placed, the operation runs to completion rather than tearing out a half-installed mod.

#### warning
Installing and removing both refuse to start while **Tarkov or the SPT server is running** - those hold the very files being replaced. Close them first. The check runs again after the download finishes, in case SPT was launched while it was in progress.


#### information
Before anything is queued, the app asks you to open the mod's page here on sp-mod first - same as installing manually, and it keeps mod authors' page views and instructions in the loop.


### App updates

The app has its own page here on sp-mod, the same as everything else you install through it. On launch it asks that page whether anything newer has been published. If there is, you get a banner and a badge on the **App update** item in the sidebar. That page is always there, with a **Check now** button, whether or not an update is waiting.

All of it goes through sp-mod: the check reads the public API, the download is the file this page's own Download button serves, and - exactly as with any other mod - **you're asked to open the mod page before anything is downloaded**.

**What the version number tells you.** The update page names the kind of change rather than leaving you to work it out:

| Change | Means |
| --- | --- |
| `x.x.`**`1`** | **Bug fix.** Fixes to how the current version already works. Nothing new to learn - safe to skip if nothing is broken for you. |
| `x.`**`1`**`.x` | **Feature update.** Something new, or something works differently. Worth reading the notes. |
| **`1`**`.x.x` | **Major update.** Significant changes. Read the notes and the mod page first. |

Closing the banner skips that release - it won't come back for that version, though anything published later will.

**How the swap works.** A running program can't overwrite itself, so this is done from outside it. The new release is downloaded into a hidden `.tcfmm-update\` folder next to the exe and checked before anything else happens. The app then starts a small script, closes itself, waits until it has fully exited, copies the new build in, and starts it again.

The copy only adds and replaces files - it never mirrors the folder - so `Data\`, `Staging\` and `LegacyConfigs\` are left exactly as they were. Your SPT path, install history and kept configs all survive an update.

#### information
**If anything goes wrong, the version you already have is left alone.** No write access, not enough disk space, a copy that doesn't complete - in every case the app comes back as it was, and the new build stays in `.tcfmm-update\payload\` so you can copy it over by hand. What happened is written to the app's log on the next launch.


#### warning
If the app lives somewhere Windows won't let it write to - inside `Program Files`, typically - it tells you up front instead of trying, and points you at this page. Keeping it in your SPT folder avoids this entirely.


The app doesn't list *itself* on the Browse page, since installing it into `BepInEx\plugins` would just leave SPT trying to load a second copy of the manager. Nothing else about Browse changes.

### Files & logs

Everything lives next to the exe, not in `%LocalAppData%`:

| Path | What |
| --- | --- |
| `Data\settings.json` | SPT install path and app settings |
| `Data\installed-mods.json` | What this app installed, and every file it placed |
| `Data\mod_cache.json` | Cached catalog |
| `Data\spt_versions.json` | Cached SPT release list, refetched daily |
| `Data\dependency_flags.json` | Per-mod "has dependencies" answers, re-checked when a mod publishes |
| `Data\mod_groups.json` | Your groups, and which mod is in which |
| `Data\logs\tcfmm-<date>.log` | Daily log |
| `Staging\` | Default destination for manually downloaded archives |
| `LegacyConfigs\` | Config files kept from removed mods, one timestamped folder per removal |
| `.tcfmm-update\` | Hidden. Only exists while an app update is downloading, or if one failed; cleaned up on the next launch |

Two more folders are created inside your **SPT install**, both hidden: `.tcfmm-work\` (scratch space while a mod installs, swept each run) and `.tcfmm-duplicates\` (copies set aside by **Sort out**, kept until you delete them).

`Data\installed-mods.json` - not folder names, not DLL file versions - is the authority on what's installed and at what version.

#### Logging

Info level by default, rotated daily as `tcfmm-<yyyyMMdd>.log`. To get Debug-level output in the same log, drop an empty file named `verbose` - no extension - next to the exe.

### Limitations

Worth knowing before you rely on it. None of these lose data quietly - they're places where the app either won't help or will tell you it can't.

#### What it can and can't see

- **Mods nested a folder deeper** - `BepInEx\plugins\Author\ModName\mod.dll` rather than `BepInEx\plugins\ModName\mod.dll` - are listed under the outer folder's name with an unknown version.
- **Mods you installed by hand are matched by folder name**, since there's no install record to read. If the folder name doesn't clearly point at one listing, the mod shows as not found on sp-mod: you can still see, group, disable and remove it, but not update it from here. A folder name that could plausibly be two different mods is deliberately left unmatched rather than guessed at.

#### Installing

- **Archives have to be packaged normally** - a `BepInEx\`, `user\`, `SPT\` or `SPT_Runtime\` folder at the top, optionally inside one wrapper folder. Anything else is refused with a message telling you to install it by hand, rather than being scattered into your install.
- **Everything in the archive gets installed.** Mods that ship optional variants in separate folders, or a readme, get all of it copied in. Choose-your-variant mods are worth installing by hand.
- **Files are overwritten without a backup.** If two mods ship the same file, the second one installed wins.
- **Removing a mod deletes the files it recorded.** If another mod happens to share one of those files, removing the first takes it with it.
- **You need roughly twice the archive's size free** on the SPT drive - the download and extraction are staged there before anything is placed.
- **Very large mods on a slow connection can time out** and have to be started again; downloads don't resume.

#### Groups and disabling

- **Groups follow folder names.** Renaming a mod's folder drops it out of its group - and out of anything you then disable by group. Drag it back into the group to fix it.
- **Copies set aside by "Sort out" stay put.** They sit in `.tcfmm-duplicates` inside your SPT install until you delete them; nothing prunes that folder for you.
- **Disabling doesn't reorder anything.** A server mod's `loadBefore` / `loadAfter` ordering relative to the mods still enabled is left to SPT.
- **Update, Redownload and Remove don't work on a disabled mod** - enable it first.

#### Versions and compatibility

- **Compatibility is judged from a mod's most recent releases**, not its whole history. A mod whose newest releases target a later SPT than yours reads as incompatible even if an older release of it would work - check the mod's page in that case.
- **Some version constraints can't be read.** Those mods show "SPT version unknown" and aren't filtered out, on the grounds that hiding something that might work is worse than showing it.
- **Beta and pre-release version numbers** aren't compared precisely, so an update may not be flagged for a mod you're running a pre-release of.
- **The catalog only covers SPT 3.10 and newer.** On older SPT, most of what you could install won't be listed.
- **"Installed" dates** come from the folder's creation date, so a mod updated in place still shows when you first installed it.

#### Scope

- **One SPT install at a time.** The record of what's installed belongs to the app, not to the install it points at, so pointing Options at a second SPT folder will carry the first one's records across. Use a separate copy of the app per install.
- **The catalog refreshes once per session** in the background. Mods published while the app is open won't appear until you press Refresh cache or restart.
- **It won't run while SPT does.** Installing or removing anything with Tarkov or the server open is refused, because those lock the files being replaced.

### Troubleshooting

#### The SPT version shows as unknown

Options needs the folder containing `SPT.Server.exe` (or `Aki.Server.exe`). It also looks under `SPT\` and `SPT_Runtime\`, but not deeper.

#### A mod I know exists shows "nothing compatible"

The mod has no version published for your SPT release line. That's a statement about the mod page, not about your install - check the mod's own versions list.

#### Downloads suddenly stall or fail

The sp-mod API is rate limited at the edge (roughly 40 requests per 10 seconds, 200 per minute). Heavy browsing can hit it. Give it a minute and retry; nothing is cached as a wrong answer in the meantime.

#### A mod I installed by hand isn't listed under Installed

It isn't in `BepInEx\plugins`, `BepInEx\patchers` or one of the `user\mods` layouts - see Limitations for the folder shapes that aren't scanned. A mod that *is* listed but shows as not found on sp-mod is there, just unmatched.

#### The Update or Remove button is greyed out

The mod is disabled. Enable it first - see the Disabling mods tab.

#### A mod I disabled is still loading in game

Check it isn't installed twice. If the same mod sits in both the normal folder and the `.disabled` one, its card says so and offers **Sort out** to keep one copy and set the other aside.

#### "Close Tarkov / SPT.Server before installing a mod"

Exactly what it says: those hold open the files being replaced. Close the game and the server window, then try again.

#### An install failed halfway

The files placed before it failed are recorded, so they stay under the app's control - install the mod again to complete it, or remove it to clear them out. The scratch folder `.tcfmm-work\` inside your SPT install is swept on the next run. The usual cause is SPT being started mid-install.

#### An app update didn't go through

Your existing version is untouched and still works - that's by design. The new build is sitting in `.tcfmm-update\payload\` next to the exe if you want to copy it over by hand, and `Data\logs\tcfmm-<date>.log` says what stopped it. The usual causes are no write access to the folder (move the app out of `Program Files`) and not enough free disk space.

#### Reporting a bug

Grab `Data\logs\tcfmm-<date>.log` - ideally after adding the `verbose` marker file and reproducing the problem - and open an issue at [github.com/TheCrimsonFckr/TCFModManager](https://github.com/TheCrimsonFckr/TCFModManager).

{.endtabset}
