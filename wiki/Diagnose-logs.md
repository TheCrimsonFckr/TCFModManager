**Diagnose logs**, in the sidebar, reads the server's last run, the last game session's BepInEx log and the game's errors log, and says in plain words what went wrong and which mod it came from.

- Each card names the problem, the mod when the log points at one, how often it was seen, and the log line it was read from. **Show in Installed** finds that mod; **Open its folder** and **Open the log** go to the files.
- Things it knows include a profile SPT won't load (and why wiping it doesn't help), raid results that weren't saved, a server that couldn't start because its port is taken, a server that gave up while starting (and the mod it points at - one made for a newer SPT, or one missing something it needs), Fika refusing to let the game join (below), plugins BepInEx didn't load (missing or too-old dependencies, incompatible or duplicate mods), server mods that aren't running, and bundle problems.
- **Copy a report for help** puts what was found, and the errors it didn't recognise, on the clipboard - with your Windows user name, folders, profile and item ids and other players' addresses taken out. Paste it where you ask for help.
- The logs only describe the last run. After changing something, start the server or the game again, then **Read the logs again**.

Nothing on this page changes any file. Works on SPT 4.0 and 4.1 layouts (`SPT\user\logs` and `SPT_Runtime\user\logs`).

> [!NOTE]
> The SPT launcher deletes the game's `Logs` folder each time it starts the game. When the game is started from the app's own Play card, the last few sessions' game logs are kept in `Data\GameLogs\` so this page can still read them.

## A profile SPT won't load
When a profile is refused because it holds items, traders or clothing from a mod that's gone, the card has **Let SPT clean the profile**. After asking, it turns on SPT's own `removeModItemsFromProfile` and `removeInvalidTradersFromProfile` in the server's `core.json` - only those two, with the file as it was kept beside it as `core.json.before-tcfmm` - so the server takes what's missing out of the profile when it next starts. Those items are gone for good, for every profile on that server. Restart the server, then try the profile again. If both are already on, the card says so. Play offers the same button when it stops on a profile SPT won't load.

**Clean profiles**, on the same cards and on a card saying the game couldn't read the server's answer, checks every profile for skills and bot roles that neither SPT nor an installed, turned-on mod adds - left behind by a mod that's gone, say Skills Extended removed by hand. It lists each profile it found, ticked, with what would come out; untick any to leave alone, then **Clean**. Every profile is backed up first, and a window lists what came out. Items and traders are left to SPT's own clean-up above.

## Fika
Fika checks the game against the server and the raid host from inside the game, so what it says is in the game's BepInEx log, not the server's. Each of its problems gets its own card: mods the server requires that the game doesn't have, no Fika on the server, the server's Fika too old, a different Fika or game version from the raid host, and Fika's own files changed. The wording is read from the installed Fika, so it follows Fika's updates and every language Fika ships. The game rewrites its log each time it starts, so read the logs before starting it again.

## When something crashes
When the server, the game or the Fika headless crashes, a red bar comes up at the top of the window - with a notification if the app isn't in front - and names the mod when it can. **Diagnose logs** on the bar opens this page; **Show in Installed** goes to the mod.

- The crash is read from Windows' own crash record (the Application event log), which for the server usually names the mod from the error's stack trace: *The SPT server crashed in Live Flea Prices*. A server that gave up while starting is read from its log. Where Windows keeps no record - under Wine - the exit code decides.
- Stopping something from the app (Stop, Restart, Close game, stopping the server after the game, closing the app) is never a crash, and neither is ending it in Task Manager or closing the server's window.
- Every exit is written to the app's log with its exit code, as `[Crash] ... exited with 0x...`.
- A game that crashes while quitting is still recorded by Windows as a crash, so the bar comes up then too.
