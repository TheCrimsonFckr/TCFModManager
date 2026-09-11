**TCF Mod Manager** is a Windows desktop app for finding, installing and keeping track of your SPT mods, built directly against the sp-mod catalog you're reading this on. Browse the full mod list, filter it down to what actually works on your SPT version, install with dependencies resolved for you, turn mods on and off without deleting anything, and see at a glance what's out of date.

It also keeps **mod lists** - save the set you run, switch between sets, send one to a friend, or follow the list a server publishes and know what you are missing before you launch. Plus a config editor, a start button for the server and the launcher, and an optional page that reads what each installed mod actually ships.

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
4. Open **Options**, point it at your SPT install folder - the top one, with `EscapeFromTarkov.exe` and `BepInEx\` in it - and hit Save. The detected server version appears underneath - everything else keys off that.

#### warning
This is **not** a mod. Don't extract it into `BepInEx\plugins` or `user\mods`. It's a standalone application that manages that folder for you. Anywhere on disk works, really - it just needs to be told where SPT lives.

Once it's set up, the app keeps itself up to date from its own page here on sp-mod - see the **App updates** tab.

#### If the SPT version doesn't detect
The version is read from the server executable - `SPT.Server.exe`, or the older `Aki.Server.exe` - at the install root or under `SPT\` / `SPT_Runtime\`. Point Options at the install root, the folder with `EscapeFromTarkov.exe` in it, not at the server folder or any other subfolder. If you do pick `SPT\` or `SPT_Runtime\` itself, the app moves the setting up to the folder above it.


### Play
Starts the install rather than making you go and find it. It is the first page in the sidebar, and on a machine that only plays it is the only one you need before a session.

- **Start server** runs this install's server in its own console window. It keeps running if you close the app, and nothing on this page stops it - close its window when you are done.
- **Open launcher** opens `SPT.Launcher.exe`, where you pick a profile and start the game. Start the server first; the launcher has nothing to log in to without one.
- **Restart the server** sits beside it, enabled only while the server is actually running. It asks first, on the card, because a raid in progress does not survive one and there is no undo.
- **Fika headless client** gets its own card, and only on a setup that has a headless launcher. A headless hosts raids for other people rather than being the one you play on, so it never shares a button with the game.

#### Before you join
The page checks your install against the mod list you are following - your own, or one a server published - and says what is outstanding. **Check again** re-runs it.

It never blocks a launch. The mods it names may not matter for the raid you are about to play, the server may be wrong, and a launch held up by a warning nobody can override is worse than a mismatch.

#### information
Stopping on its own is deliberately not offered. A restart puts back what it took; a server left down is a state somebody has to notice, and this app is not the thing that should decide to leave it that way.

### Browse
The whole sp-mod catalog, fetched once and cached to disk so it opens instantly next time.

- **Search** by name, or by author with `@author`
- **Filter** by SPT release line, category, Fika compatibility and featured status
- **Sort** by newest, last updated, most downloaded, most favourited or most endorsed
- **Show** options for hiding ads, AI-generated content and **mods you already have installed**
- **Refresh cache** re-pulls the catalog when you want the newest listings

Each card shows the download count, the endorsement count when the mod has any, a status dot, and a badge for mods that pull in dependencies. A **pin** under the status dot means you have pinned that mod, so no mod list you apply will set it aside - see the Mod lists tab:

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


### Addons
Some mods have **addons** - extra content published against the mod itself rather than against an SPT release. A voice-command pack for a companion mod, a Linux build of a GUI, an overlay for a web tool.

Addons aren't listed separately, because an addon is no use without its parent. Open a mod - from Browse or from Installed - and its addons are listed at the bottom of the dialog, each with its own version picker and Install button.

The thing that makes an addon different from a mod is what it's measured against:

##### A mod version says: which **SPT release** it needs.
##### An addon version says: which version of its **parent mod** it needs

So the app checks each addon version against the mod you actually have installed. If none of them fit, the addon is still listed - with the reason, rather than quietly missing:

> Needs Raid Review ^1.5.0 - you have 1.4.2

Installed addons get their own card on the Installed page, labelled with the mod they belong to, and update the same way anything else does. **Mod lists cover them too** - capturing a list records your addons, and applying one installs, updates or disables them alongside your mods.

#### information
Installing an addon won't drag its parent mod in behind it - install the mod first, then its addon. Anything else the addon needs is offered the usual way, on the Downloads page. Applying a list that names an addon whose parent mod isn't installed (and isn't on that list either) skips it and tells you why, rather than downloading something nothing would load.

#### warning
A mod list containing an addon needs this version of the app or newer to open. Sharing one with someone on an older build will tell them the file is too new - send them a list without addons, or ask them to update. Lists with no addons in them are unaffected.

#### warning
**Update, Redownload and Remove are unavailable while a mod is disabled.** The record of what it installed points at folders it no longer occupies, so those actions would put files in the wrong place. Enable the mod first, then update it. Browse refuses a reinstall of a disabled mod for the same reason.

#### If a mod ends up in two places at once
A move interrupted partway - or one done by hand - can leave the same mod in both the normal folder and the `.disabled` one. When that happens the card says so and offers **Sort out**: pick which copy to keep, and the other is moved into a hidden `.tcfmm-duplicates` folder inside your SPT install rather than deleted. Undo puts that back too.


### Mod lists
A **mod list** is a named set of mods and the versions of them you run. Capture what you have now, switch between sets, send one to a friend, or follow the one a server publishes. It records mods and versions, never files, so capturing is instant however big your install is.

**Capture** saves whatever is enabled right now. **Preview** works out what applying it would do and shows you one row per mod - install, update, enable, set aside - and nothing moves until you press **Apply**.

**Apply** downloads what the list names and you do not have, enables what you have but had switched off, and moves anything the list does not name into the disabled folder. **Nothing is ever deleted.** Setting a mod aside is the same move the Installed page's disable button makes, and it is near-instant however large the mod is.

**Pin** the mods you want kept whatever list you apply - a HUD, a sound pack, the quality-of-life mods that don't belong on any one list. A pinned mod is never set aside: Preview lists it as **Pinned** instead, with a button to unpin it. Pin from that Preview row, or with **Pin** on the mod's opened card on the Installed page. A pinned mod shows a pin under its disable button on Installed, beside its name in any list's contents, and on its Browse card. Pins belong to your install and never travel with a list you share.

**Undo** puts the install back the way it was before the last apply. There is one undo point, kept up to date for you: applying a list replaces it, and using it clears it.

#### warning
The disable half needs the game and the server closed - BepInEx holds the files it has loaded open. Preview says so before you apply.

#### Editing a list
Three steps, and they are separate on purpose:

1. **Add mods** and the **X** on a row change the list on screen only.
2. **Save** writes the list. No downloads, nothing moved - the only thing it changes is what the list says.
3. **Apply** is the only thing on this page that touches your game folder.

**Add mods** offers what is installed here *and* the whole sp-mod catalog, so a list can name a mod this machine has never had. A mod added from the catalog is left unlocked - it means "the newest published version", because a version nobody here has run is not a version anybody has tested. **Refresh versions** re-reads the versions of every mod on the list that is installed here, for after an update round has moved a dozen of them.

Preview and Apply are switched off while there are unsaved changes, and the page says why: they work off the stored list, and running them against something the panel no longer agrees with is the one genuinely confusing state this page can reach.

#### Sharing a list
**Export** writes a `.tcfmodlist` file. Send it to whoever you like; **Import** reads it back.

What travels is a manifest - "install mod 2426 at version 5" - and never mod files. The receiving app downloads from sp-mod.com exactly as it would for any other install, which is faster than anything a person could serve you, costs the sender no bandwidth, raises no redistribution question, and keeps the mod author's download count honest.

- **A list you imported is read-only**, and so is one a server served you. **Make a copy** turns it into one of your own, pointing back at where it came from.
- **Mods the catalog cannot resolve** - GitHub-only mods, hand-built things - are listed by name rather than quietly dropped, so you know what to go and fetch yourself.
- **A locked version that has since been withdrawn** asks rather than failing: it offers the nearest version and lets you untick anything you would rather skip.

#### Who each mod is for
Every entry names the machines it is for. It is worked out for you at capture time from where the mod's files actually live, and shown on every row. The **scope button** cycles a row through the six, and the filter above the list matches one exactly - which answers the question you have when tidying a list: what have I already pruned, and what is still carrying the capture default?

| Scope | Who gets it |
| --- | --- |
| Server + Client + Headless | Everything. A mod with both halves, by default |
| Server + Client | The server and the players; not the headless |
| Client + Headless | Every machine running the game. A plugin, by default |
| Client only | Players only - HUD tweaks, sound packs, anything drawn at a person |
| Headless only | The headless box alone |
| Server only | The server's own mods - `fika-server` and friends |

What is in the name gets it; what is not, does not. Nothing on disk separates a bot overhaul from a HUD widget - both are a DLL in `BepInEx\plugins` - so a capture gives every plugin to the headless and you take away what it does not need. That direction is deliberate: a headless carrying a spare mod costs nothing anyone can see, while one missing an item or bot mod is felt by everybody in the raid it is hosting.

A **headless still takes Server only entries**. It is a full SPT install, and a server-only entry is a whole mod rather than half of one. **Server + Client** is how you say the server and the players need this and the headless does not.

#### information
Scope only ever narrows a list a server served you. **Your own lists describe your own install and apply whole** - which matters if you host and play on the same machine, see below.

#### If you run the server
Publishing needs the Server Map mod on the server - see the Server map tab. The steps here are the mod list half.

1. **Capture the list on the machine the server runs on**, so the server's own mods are in it and scoped `Server only` for you.
2. **Prune it for the people receiving it.** Take the headless off anything it does not need; leave the players everything they do.
3. **Publish to this server** writes it where the server serves it from. The list gets a red **Serving** badge so you can see which of a dozen personal lists is the one going out.
4. **Edit, Save, and publish again** to hand out a new version. The revision moves only when what you published actually changed, so republishing an unchanged list is a no-op and the number still means something.

On the joining side, a served list is stored read-only and marked as coming from that server. **Refresh from server** asks for it again whether or not the revision has moved - for a copy you doubt - and names what came back. It installs nothing; applying is still a separate step.

#### warning
**Do not apply your published client list on the machine that hosts.** A list of your own applies whole - every entry, whatever its scope - so one pruned down to what players need names none of your server's mods, and applying it there sets aside `fika-server`, SVM and the Server Map mod itself, which is the thing publishing the list. Nothing is deleted and **Undo** puts it straight back, but your server stops serving until you do.

The shape that works is **two lists on the server box**: the full one you apply there, and the pruned one you publish. They are cheap - a list is a manifest, not a copy of anything.

#### Following a server and your own list at once
Both can be active. Following a server does not cost you the personal list you were already following, and applying a list of your own will not sweep away what the server requires - the mods it asks for are spared.

A list a server hands you **never disables anything**, whatever its author chose. An operator writing a list is describing their install; applying that verbatim on your machine would set aside mods the server has never heard of and has no opinion about.


### Configs
Edit your mods' settings without leaving the app. The page finds every config file your installed mods actually have and groups them by where they live, because that turns out to be the thing that matters:

- **Client** - the files in `BepInEx\config`, one per plugin.
- **Server** - the files inside a server mod's own folder under `user\mods`.
- **BepInEx** - BepInEx's own settings, which are not a mod's.
- **Unclaimed** - a `BepInEx\config` file no installed plugin answers to. Almost always a mod you removed at some point, leaving its settings behind.

Each row shows the mod and the file's path inside its folder, so the twenty mods that all ship a `config.json` are never mixed up. There is a search box and a filter to narrow it to one kind.

The two halves behave differently, and the page says so above the editor: **a client mod's settings live outside its folder**, so they survive disabling and removing it; **a server mod's settings live inside its folder** and travel with it.

#### Editing is careful with your files
- **Nothing is ever reformatted.** For a lot of mods the comments in the config file are the only documentation those settings have. Saving changes the lines you changed and leaves everything else exactly as it was.
- **Broken JSON is refused, not written**, with the line that stopped making sense. Comments and trailing commas are fine - plenty of server mods ship both.
- **Every save keeps a copy of what was there first**, in `Data\config-backups\` inside the app's own folder, one timestamped folder per save. Copying one back over your install undoes a round of edits.
- **If the file changed underneath you** - the game wrote it, or you edited it in Notepad - the save stops and asks whether to overwrite, reload or leave it. Whichever you pick, what is on disk is copied aside first.
- **Shipped defaults** (`config.default.json` and the like) are listed but dimmed: they are the pristine copy, so editing one does nothing. They are shown rather than hidden because they are what you look at when you want the original value back.

#### warning
Changing a mod's settings is your call. If a change breaks the mod or your game that is not the author's problem to put right - only change settings you understand, and say so if you report a bug after editing a config.

#### information
BepInEx writes its config files back out when the game closes, so a client config edited mid-session is likely to be undone. A server config takes effect on the next server restart.

#### Updating a mod still replaces its config
Installing a newer version of a mod places that version's files, defaults included. The config you had is set aside into `Data\LegacyConfigs\` rather than deleted - but nothing puts it back for you, and nothing on the Configs page reads that folder. If a mod's settings matter to you, copy them out before updating it, or take them from `Data\LegacyConfigs\` afterwards. Carrying them across for you is designed and not yet built.

### Dependencies
Resolves the dependency tree of every installed mod that declares one, and reports each dependency's state against what's actually on disk.

That includes **version conflicts** - where two installed mods want incompatible versions of the same dependency - which is the failure mode that usually shows up as an unexplained crash on load rather than an error message. Dependencies you've disabled are called out as disabled rather than missing.

Anything missing can be installed straight from the list.


### Mod footprint
An optional page - **off by default**, turned on under **Options - Mod footprint page** - with one row per installed mod, showing what each mod's files contain. Open a row for the full breakdown; rows sort by patch classes, components or size.

**Nothing is measured.** The app does not launch the game, load a mod or run any mod's code. Nothing is timed or profiled, no frame rates are reported, and there are no scores, ratings or rankings. Everything is read from the files on disk:

- **Patch classes shipped**, and components the engine would call on a timer - per-frame updates, physics steps, on-screen interface drawing, camera hooks - broken down by which.
- **Asset bundles** and their size, held in memory once loaded.
- **Whether it ships a preloader patcher**, which runs before the game loads.
- **Whether it has a server half** under `user\mods`, counted separately and never folded into the client figures.
- **Disk usage**, in files and megabytes.

#### What the figures do not cover
- It counts patch classes, not patched methods. One class can target several methods, so the real number can be higher.
- It cannot see what a patch touches. A patch on a per-frame method and a patch on a menu button are counted the same way.
- A declared component only runs if the mod creates and enables one, which the files do not show. Every line says *declares* or *ships*.
- Some mods are packed or obfuscated in ways it cannot parse. The breakdown says so rather than guessing.

#### information
Readings are cached in `Data\mod_footprints.json` and a mod is re-read only when its files change; **Rescan** forces a fresh read. Disabled mods are included and marked as not loaded. Nothing is uploaded - the page makes no network requests at all.


### Server map
An optional page - **off by default**, turned on in Options - that connects to an SPT server running the **Server Map mod** and shows what that server runs, so you can be ready before you launch rather than after a raid fails to load.

- **The mod goes on the server**, not on your machine. It is a separate download, published as an addon of this mod - **Options - Server map - Get the Server Map mod** opens its page - and a player joining a server needs none of it.
- **Connecting sends nothing about your install.** It asks the server who it is; the server never asks anything about you.
- **The server serves a list, never files.** Mods are still only ever downloaded from sp-mod.com. A server that could push files at you would break the one rule this app is built on, so there is no route for it to do so.
- **Certificates are pinned on first use.** SPT serves a self-signed certificate, so the app remembers the exact one your server presented and tells you if it ever changes - which is what a machine-in-the-middle would look like. Trust the new one or refuse it.
- **A shared key** guards everything but the handshake. The operator gives it to you; on the server's own machine the app finds it by itself.

**Connecting fetches the list for you** and saves it as a read-only mod list, marked as coming from that server; it is fetched again on its own whenever the server's revision moves. **Fetch again** asks for it even when the revision hasn't moved, for a copy you have edited or deleted.

Saving is not applying. From there it is an ordinary list - preview it on the Mod lists page, apply it, keep your own alongside it - and the Play page's check compares against it before you launch.

#### If you run the server
The Server Map mod's own page carries the operator guide - installing the payload and the stub for your SPT line, where the key and the published list live (`TCFModManager\Data\ServerMap\`), rotating the key, and opening a port. The mod list half - capturing, pruning, publishing, and the one thing not to apply on the machine that hosts - is on the Mod lists tab.

#### warning
Client and server must be on the same SPT line: a 4.1 client cannot join a 4.0.13 server, and that is SPT's rule rather than this app's. A published list only ever reaches people already on your version.

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
Installing and removing both refuse to start while **T***** or the SPT server is running** - those hold the very files being replaced. Close them first. The check runs again after the download finishes, in case SPT was launched while it was in progress.

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
| `Data\mod_lists.json` | Your mod lists, which ones you follow, your pinned mods, and the single undo point |
| `Data\addon_cache.json` | Cached addon catalog |
| `Data\mod_footprints.json` | Cached footprint readings, only if that page is on |
| `Data\config-backups\` | One timestamped folder per config save, laid out like your install |
| `Data\logs\tcfmm-<date>.log` | Daily log |
| `Staging\` | Default destination for manually downloaded archives |
| `Data\LegacyConfigs\` | Config files kept from removed and updated mods, one timestamped folder each |
| `Data\ServerMap\` | On a server: the shared key (`servermap-key.txt`) and the list it publishes |
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

#### Mod lists
- **A list records mods and versions, never files.** Anything it names has to be gettable from sp-mod for the receiver to install it; mods that aren't are listed by name so they can be fetched by hand.
- **Scope only narrows a list a server served you.** Your own lists apply whole on the machine that owns them - which is why a server box wants its own list as well as the one it publishes.
- **There is one undo point**, replaced by each apply and cleared by using it. It puts mods back where they were; it is not a history.
- **An addon that ships inside its parent's folder can be installed and updated by a list, but not set aside by one** - there is no folder of its own to move. Disabling the parent takes it along.
- **A list you edited but never applied exports under its old revision number**, since a revision counts an apply. Apply before sharing if you want the receiver's copy to read as newer.

#### Configs
- **Updating a mod places that version's config defaults.** Your previous file is set aside in `Data\LegacyConfigs\` rather than deleted, but nothing puts it back for you.
- **Settings kept somewhere unusual aren't recognised as configs** - a `Presets\` folder rather than `config\`, for instance. Those are edited by hand.

#### Server map
- **Client and server must be on the same SPT line**, which is SPT's rule rather than this app's.
- **The server publishes a list, not files**, and the page reports nothing about your install back to it.
- **A server's certificate is pinned on first connect.** If it changes you are asked before anything else happens, because that is also what an interception would look like.

#### Scope
- **One SPT install at a time.** The record of what's installed belongs to the app, not to the install it points at, so pointing Options at a second SPT folder will carry the first one's records across. Use a separate copy of the app per install.
- **The catalog refreshes once per session** in the background. Mods published while the app is open won't appear until you press Refresh cache or restart.
- **It won't run while SPT does.** Installing or removing anything with T***** or the server open is refused, because those lock the files being replaced.


### Troubleshooting

#### The SPT version shows as unknown
Options needs your SPT install root - the folder with `EscapeFromTarkov.exe` and `BepInEx\` in it. The server exe (`SPT.Server.exe`, or `Aki.Server.exe`) is found from there, at the top or under `SPT\` / `SPT_Runtime\`, but not deeper.

#### Mods went into `SPT\SPT\...` or `SPT\BepInEx\...`
The install folder was set to the server folder - `SPT\` on 4.0, `SPT_Runtime\` on 4.1 - instead of the install root, so everything installed went one level too deep. Before v1.13.2 Options accepted that without complaint; from v1.13.2 the setting is moved up to the install root by itself the next time the app starts. Mods that already went into the wrong place stay there: look inside the server folder for a stray `BepInEx\` or a second `SPT\` / `SPT_Runtime\` / `user\` folder, and move what is in them into the real `BepInEx\plugins` at the install root and your server's `user\mods` - or delete them and install those mods again.

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

#### "Close T***** / SPT.Server before installing a mod"
Exactly what it says: those hold open the files being replaced. Close the game and the server window, then try again.

#### An install failed halfway
The files placed before it failed are recorded, so they stay under the app's control - install the mod again to complete it, or remove it to clear them out. The scratch folder `.tcfmm-work\` inside your SPT install is swept on the next run. The usual cause is SPT being started mid-install.

#### An app update didn't go through
Your existing version is untouched and still works - that's by design. The new build is sitting in `.tcfmm-update\payload\` next to the exe if you want to copy it over by hand, and `Data\logs\tcfmm-<date>.log` says what stopped it. The usual causes are no write access to the folder (move the app out of `Program Files`) and not enough free disk space.

#### I applied a list and my server's mods switched off
You applied a list that names none of them - usually the pruned one meant for players - on the machine that hosts. Nothing was deleted: press **Undo** on the Mod lists page and everything comes straight back. Keep two lists on that machine, one to apply there and one to publish.

#### The list from my server never updates
Press **Refresh from server** on the list, on the Mod lists page. It asks again whether or not the revision has moved. If you are the operator and clients aren't seeing a change, publish again after saving - publishing is what hands the new version out.

#### My headless installed a pile of mods it doesn't need
It is reading as an ordinary player. **Options - What this machine is** says whether anybody plays there and whether it runs a headless client; a served list is only trimmed once that is answered. If the app never asked, it didn't find `FikaHeadlessManager.exe` at the top of the install folder - point **Options - Fika headless launcher** at it.

#### Reporting a bug
Grab `Data\logs\tcfmm-<date>.log` - ideally after adding the `verbose` marker file and reproducing the problem - and open an issue at [github.com/TheCrimsonFckr/TCFModManager](https://github.com/TheCrimsonFckr/TCFModManager).


### Planning
- Mod lists / profiles - Completed
- Mod list sharing and handling (if you have played Arma modded or Total War modded, think like that) - Completed
- Mod syncing, getting on the same level as the server you are joining - Completed
- Server mapping - Released, and being built on
- Fika headless support, so a headless is served only what it needs - Completed
- Window default sizes - Completed
- Default filtering and page defaults - Completed
- Config protection, so updating a mod stops replacing your settings - Next
- Per-mod pins, so a mod you always want on survives any list you apply - Completed

{.endtabset}
