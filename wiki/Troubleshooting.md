## The SPT version shows as unknown
Options needs your SPT install root - the folder with `EscapeFromTarkov.exe` and `BepInEx\` in it. The server exe (`SPT.Server.exe`, or `Aki.Server.exe`) is found from there, at the top or under `SPT\` / `SPT_Runtime\`, but not deeper.

## Mods went into `SPT\SPT\...` or `SPT\BepInEx\...`
The install folder was set to the server folder - `SPT\` on 4.0, `SPT_Runtime\` on 4.1 - instead of the install root, so everything installed went one level too deep. Before v1.13.2 Options accepted that without complaint; from v1.13.2 the setting is moved up to the install root by itself the next time the app starts. Mods that already went into the wrong place stay there: look inside the server folder for a stray `BepInEx\` or a second `SPT\` / `SPT_Runtime\` / `user\` folder, and move what is in them into the real `BepInEx\plugins` at the install root and your server's `user\mods` - or delete them and install those mods again.

## A mod I know exists shows "nothing compatible"
The mod has no version published for your SPT release line. That's a statement about the mod page, not about your install - check the mod's own versions list.

## Downloads suddenly stall or fail
The sp-mod API is rate limited at the edge (roughly 40 requests per 10 seconds, 200 per minute). Heavy browsing can hit it. Give it a minute and retry; nothing is cached as a wrong answer in the meantime.

## A mod I installed by hand isn't listed under Installed
It isn't in `BepInEx\plugins`, `BepInEx\patchers` or one of the `user\mods` layouts - see Limitations for the folder shapes that aren't scanned. A mod that *is* listed but shows as not found on sp-mod is there, just unmatched.

## The Update or Remove button is greyed out
The mod is disabled. Enable it first - see [Disabling mods](Disabling-mods).

## A mod I disabled is still loading in game
Check it isn't installed twice. If the same mod sits in both the normal folder and the `.disabled` one, its card says so and offers **Sort out** to keep one copy and set the other aside.

## A mod is installed but doesn't load in game
Open **Dependencies and Conflicts**. The usual reason is under **Conflicts**: the same mod installed twice - SPT loads neither copy of a server mod it finds twice - or two mods shipping different copies of one file. **Keep this one** clears a mod installed twice. With no conflicts, check **Version conflicts** and **Dependencies** for what it needs, then that it's turned on and made for your SPT version.

## "Close T***** / SPT.Server before installing a mod"
Exactly what it says: those hold open the files being replaced. Close the game and the server window, then try again.

## An install failed halfway
The files placed before it failed are recorded, so they stay under the app's control - install the mod again to complete it, or remove it to clear them out. The scratch folder `.tcfmm-work\` inside your SPT install is swept on the next run. The usual cause is SPT being started mid-install.

## An app update didn't go through
Your existing version is untouched and still works - that's by design. The new build is sitting in `.tcfmm-update\payload\` next to the exe if you want to copy it over by hand, and `Data\logs\tcfmm-<date>.log` says what stopped it. The usual causes are no write access to the folder (move the app out of `Program Files`) and not enough free disk space.

## I applied a list and my server's mods switched off
You applied a list that names none of them - usually the pruned one meant for players - on the machine that hosts. Nothing was deleted: press **Undo** on the Mod lists page and everything comes straight back. Keep two lists on that machine, one to apply there and one to publish.

## The list from my server never updates
Press **Refresh from server** on the list, on the Mod lists page. It asks again whether or not the revision has moved. If you are the operator and clients aren't seeing a change, publish again after saving - publishing is what hands the new version out.

## My headless installed a pile of mods it doesn't need
It is reading as an ordinary player. **Options - What this machine is** says whether anybody plays there and whether it runs a headless client; a served list is only trimmed once that is answered. If the app never asked, it didn't find `FikaHeadlessManager.exe` at the top of the install folder - point **Options - Fika headless launcher** at it.

## No update notifications appear
First make sure a check has actually run: press **Check now** in **Options - Update notifications** and read the line under it. The first check after switching on only notes what's already there, so it never shows one. A check is also skipped while downloads are running, while no SPT install is set, and while sp-mod can't be reached - the log's `Updates` lines say which. If checks are finding updates and still nothing appears, Windows is holding them back: check **Windows Settings - System - Notifications** allows TCF Mod Manager, and that Do not disturb is off.

## Reporting a bug
Grab `Data\logs\tcfmm-<date>.log` - ideally after adding the `verbose` marker file and reproducing the problem - and open an issue on the new [issues tab](https://sp-mod.com/mod/2945/tcf-mod-manager#issues).
