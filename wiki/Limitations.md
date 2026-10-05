## What it can and can't see
- **Mods nested a folder deeper** - `BepInEx\plugins\Author\ModName\mod.dll` rather than `BepInEx\plugins\ModName\mod.dll` - are listed under the outer folder's name with an unknown version.
- **Mods you installed by hand are matched by the ID they declare, then by folder name**, since there's no install record to read. If neither clearly points at one listing, the mod shows as not found on sp-mod: you can still see, group, disable and remove it, but not update it from here. A folder name that could plausibly be two different mods is deliberately left unmatched rather than guessed at.
- **A mod's version is only as good as what it declares.** Some authors forget to update it, and some mods work theirs out while the game runs, which the app can't read - those fall back to the DLL's file version. Either way, **Confirm as installed** puts it right.
- **Conflicts are found from files, not from what mods do in game.** Two mods changing the same thing - the same item, trader or setting - can't be told from their files, so they don't show as a conflict.
- **A listing can number its releases differently from the mod it installs.** A helper mod declaring 0.0.7 on a listing whose releases are 4.x shows an update that never clears. Confirm as installed ends it.

## Installing
- **Archives have to be packaged normally** - a `BepInEx\`, `user\`, `SPT\` or `SPT_Runtime\` folder at the top, optionally inside one wrapper folder. A `plugins\` or `patchers\` folder without `BepInEx\` around it is placed under `BepInEx\`. Anything else is refused with a message telling you to install it by hand, rather than being scattered into your install.
- **Everything in the archive gets installed** - except read-me files, licences and pictures sitting at the top of it, which would otherwise land in your SPT folder. Mods that ship optional variants in separate folders get all of them copied in. Choose-your-variant mods are worth installing by hand.
- **If two mods ship the same file, the second one installed wins.** Removing either one leaves the file for the other.
- **Mods installed before v1.19.0 are only fully protected after their next update or reinstall.** Until then their record has no fingerprints: removing one also takes a file you changed since (into `.tcfmm-removed\`, so it can be got back), and leaves its files in the install root or `EscapeFromTarkov_Data\Managed` in place.
- **You need roughly twice the archive's size free** on the SPT drive - the download and extraction are staged there before anything is placed.
- **Very large mods on a slow connection can time out** and have to be started again; downloads don't resume.

## Groups and disabling
- **Groups follow folder names.** Renaming a mod's folder drops it out of its group - and out of anything you then disable by group. Drag it back into the group to fix it.
- **Copies set aside by "Sort out" stay put.** They sit in `.tcfmm-duplicates` inside your SPT install until you delete them; nothing prunes that folder for you.
- **Disabling doesn't reorder anything.** A server mod's `loadBefore` / `loadAfter` ordering relative to the mods still enabled is left to SPT.
- **Update, Redownload and Remove don't work on a disabled mod** - enable it first.

## Versions and compatibility
- **Compatibility is judged from a mod's most recent releases**, not its whole history. A mod whose newest releases target a later SPT than yours reads as incompatible even if an older release of it would work - check the mod's page in that case.
- **Some version constraints can't be read.** Those mods show "SPT version unknown" and aren't filtered out, on the grounds that hiding something that might work is worse than showing it.
- **Beta and pre-release version numbers** aren't compared precisely, so an update may not be flagged for a mod you're running a pre-release of.
- **The catalog only covers SPT 3.10 and newer.** On older SPT, most of what you could install won't be listed.
- **"Installed" dates** come from the folder's creation date, so a mod updated in place still shows when you first installed it.

## Mod lists
- **A list records mods and versions, never files.** Anything it names has to be gettable from sp-mod for the receiver to install it; mods that aren't are listed by name so they can be fetched by hand.
- **Scope only narrows a list a server served you.** Your own lists apply whole on the machine that owns them - which is why a server box wants its own list as well as the one it publishes.
- **There is one undo point**, replaced by each apply and cleared by using it. It puts mods back where they were; it is not a history.
- **An addon that ships inside its parent's folder can be installed and updated by a list, but not set aside by one** - there is no folder of its own to move. Disabling the parent takes it along.
- **A list you edited but never applied exports under its old revision number**, since a revision counts an apply. Apply before sharing if you want the receiver's copy to read as newer.
- **An sp-mod list is read from its web page**, because sp-mod has no API for lists yet. If sp-mod changes that page, the import says the list may be incomplete rather than guessing.
- **An sp-mod list's versions are the ones its page showed on the day you imported it.** Refresh from sp-mod picks up newer ones.

## Configs
- **The first update of a mod after this app version can't merge.** Nothing recorded what its previous version shipped, so the update takes the new file and says so; from its next update on it merges. A copy of your file is always kept in `Data\LegacyConfigs\`.
- **A JSON5 config, or one large enough to be data rather than settings, is not merged** - it is replaced and reported. Set that mod to **Keep mine** if you edit its config.
- **A setting you added yourself is reported, not carried.** So is one the new version has dropped.
- **Settings kept somewhere unusual are only recognised once you say so** - a `Presets\` folder rather than `config\`, for instance. The three buttons on the Configs page are how you say so, and SVM comes set up already.
- **A folder of your own files is matched by name.** Renaming the mod's folder loses its entry, the same way a mod group does.

## Monitor mode
- **Your settings aren't carried across a hand install.** Config protection only runs on an install the app does; copying an update over by hand replaces the mod's configs.
- **A hand install is recognised by file size, not contents.** A file of the right size is taken as the right file.
- **A mod installed somewhere other than where its archive lays it out isn't recognised** - into a different folder, or straight into a disabled one. It stays waiting; **Confirm as installed** in the mod's update dialog records the version by hand.
- **"Not now" lasts until the app is closed.** The next time it starts, anything still waiting is asked about again.

## Update notifications
- **Checks only run while the app does.** Closing the window ends them unless the app is kept in the tray, and it doesn't start with Windows.
- **A mod you installed by hand can be announced once too often.** With no install record, its version is read from what the mod declares, and a declared version that lags behind the real one reads as out of date. It's announced once at most, and confirming the version on the mod's card ends it.
- **Checks are at least 30 minutes apart.** **Check now** is there when you want one sooner.

## Server map
- **Client and server must be on the same SPT line**, which is SPT's rule rather than this app's.
- **The server publishes a list, not files.** The page reports this machine to the server's map only after you say **Share**, and only to that server.
- **Only machines running this app appear on the map.** A player who installs by hand is invisible to it.
- **A server's certificate is pinned on first connect.** If it changes you are asked before anything else happens, because that is also what an interception would look like.

## Scope
- **One SPT install at a time.** The record of what's installed belongs to the app, not to the install it points at, so pointing Options at a second SPT folder will carry the first one's records across. Use a separate copy of the app per install.
- **The catalog refreshes once per session** in the background. Mods published while the app is open won't appear until you press Refresh cache or restart - except the mods you have installed, which update notifications re-read on every check while they're on.
- **It won't run while SPT does.** Installing or removing anything with T***** or the server open is refused, because those lock the files being replaced.
