**Diagnose logs**, in the sidebar, reads the server's last run, the last game session's BepInEx log and the game's errors log, and says in plain words what went wrong and which mod it came from.

- Each card names the problem, the mod when the log points at one, how often it was seen, and the log line it was read from. **Show in Installed** finds that mod; **Open its folder** and **Open the log** go to the files.
- Things it knows include a profile SPT won't load (and why wiping it doesn't help), raid results that weren't saved, a server that couldn't start because its port is taken, plugins BepInEx didn't load (missing or too-old dependencies, incompatible or duplicate mods), server mods that aren't running, and bundle problems.
- **Copy a report for help** puts what was found, and the errors it didn't recognise, on the clipboard - with your Windows user name, folders, profile and item ids and other players' addresses taken out. Paste it where you ask for help.
- The logs only describe the last run. After changing something, start the server or the game again, then **Read the logs again**.

Nothing on this page changes any file. Works on SPT 4.0 and 4.1 layouts (`SPT\user\logs` and `SPT_Runtime\user\logs`).

> [!NOTE]
> The SPT launcher deletes the game's `Logs` folder each time it starts the game. When the game is started from the app's own Play card, the last few sessions' game logs are kept in `Data\GameLogs\` so this page can still read them.
