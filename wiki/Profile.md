Everything you do just before a raid, on one page: what's wrong with the install, which profile you're playing, and the way into the game. It is the first page in the sidebar. The server itself has its own page - see [Server](Server).

## Warnings
Cards at the top appear only when there's something to say: a server mod whose dependency is missing (SPT then loads **no server mods at all**), installed mods that aren't for this SPT version, and mods that conflict. Each links to where you can fix it. None of them stops you launching.

## Before you join
The page checks your install against the mod list you are following - your own, or one a server published - and says what is outstanding, with versions where a version is wrong: *SAIN (3.0.0 → 3.1.2)*. **Check again** re-runs it. When you are behind a server's list, **Review and install** opens that list on the Mod lists page with its preview already done.

It never blocks a launch. The mods it names may not matter for the raid you are about to play, the server may be wrong, and a launch held up by a warning nobody can override is worse than a mismatch.

## Profile stats
What SPT's launcher shows on its profile page, read straight from the profile file, so the server doesn't need to be running:

- **Who** - name, level, side, edition, and the profile ID with a copy button.
- **Money** - roubles, dollars, euros and GP across the PMC's whole inventory (stash and gear), as the launcher counts it.
- **Level** - how much EXP is left to the next level, with a bar for progress through the current one. While the server is running the figures come from it, so a level table a mod has changed (SVM and the like) counts; with the server down they come from SPT's stock table. Hover the row to see which.
- **Overall / PMC / Scav** - raids, survived, KIA, run through, AWOL, MIA, kills, K/D, survival and leave rates, average life span, best survival streak, time in raid and account age.

The figures are counted the way SPT's server counts them for its launcher, with two differences: times don't wrap at 24 hours or 60 minutes, and Overall's best streak is the better of the PMC's and the Scav's rather than the two added together. A profile that hasn't made its character yet says so. The figures update when the profile file does - SPT saves it at the end of a raid and when the server stops.

Pick the profile in the card's own picker, or in the Play card's when that's switched on - they're the same choice.

## Game launcher
- **Open launcher** opens `SPT.Launcher.exe`, where you pick a profile and start the game. Start the server first; the launcher has nothing to log in to without one.
- **Close game** closes Escape From Tarkov, after asking. Tick **Close the game when the server stops** and the game goes whenever this install's server stops - **Stop**, its window closed, or a crash. Stop then closes the game first, while the server can still answer it; a restart from the Server page doesn't count.

## Play from the app (experimental)
**Options - Start the game from this app** (off by default) adds a **Play** card: pick a profile and press **PLAY**. It starts the server if it isn't running, waits for it, does what the SPT launcher does - checks the server and `spt-core.dll` are the same SPT version, cleans up the same files, applies SPT's game patch, fetches missing mod bundles - and starts the game, with no SPT launcher window. If the server stops or doesn't come up in time, **Show server log** opens the Server page with its log. The **...** beside PLAY has wipe on next start, new profile, delete profile (a copy of your profiles is kept first), clear cache, and the SPT launcher. The SPT launcher's own settings (files it leaves alone, Clear cache on launch) are followed.

> [!WARNING]
> This copies what SPT 4.1's launcher does, read from its source - it is not made or supported by the SPT team. It works on SPT 4.1.3 and later in 4.1 only; SPT 4.0's launcher runs a game ownership check this app does not stand in for, so on 4.0 the card points you at the launcher. If the game won't start or acts strangely, switch it off and use the SPT launcher, and say it was started from this app when asking for help.
