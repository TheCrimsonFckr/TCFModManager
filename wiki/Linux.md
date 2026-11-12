TCF Mod Manager is a Windows app, but it runs on Linux under **Wine** or **Proton** - the same way the SPT client does. Support is best effort: it is tested by people playing on Linux, not before every release.

> [!WARNING]
> Run it in the **same Wine prefix as the game**. The app only sees processes in its own prefix. In a different prefix it can't tell the game or the server is running, and it will change mods under a running game.

## You need
- **Wine 10 or newer**, or **Proton 10 / GE-Proton10 or newer**. Wine 9 and Proton 9 don't start the release build.
- SPT already working in that prefix.

## Install
1. Extract `TCF-ModManager-<version>.zip` into your SPT folder as `<SPT root>\TCFModManager\`, as on Windows - see [Install](Install).
2. Start `TCFModManager.exe` in the game's prefix. Any of these work:
   - Wine: `WINEPREFIX=/path/to/prefix wine "/path/to/SPT/TCFModManager/TCFModManager.exe"`
   - Steam / Proton: `protontricks-launch --appid <the game's app id> TCFModManager.exe` (find the id with `protontricks -l`)
   - umu / Lutris / Bottles / Heroic: run the exe inside the same prefix the game uses.
3. In **Options**, set the SPT install folder as the prefix sees it - `Z:\home\you\...` for a folder on your Linux drive, or the prefix's own `C:\...` path.

## What works
Browsing, installing, updating, disabling, mod lists, configs, the Play page and Server, and [App updates](App-updates). Links open in your Linux browser.

## What doesn't
- **Update notifications and the tray.** Hidden under Wine - neither is reliable there. Closing the window quits the app. The Installed page still shows updates.
- **Comments inside the app.** Wine has no WebView2, so the Comments tab offers **Open comments on sp-mod** instead.
- **Some symbols and non-Latin titles** can show as boxes, depending on the fonts installed.
- **A native Linux SPT server** (`SPT.Server.Linux`) isn't seen. Play and the running-server checks only know about `SPT.Server.exe` in the same prefix.

## If something is wrong
- **The app closes as soon as it opens.** Check your Wine or Proton version - see **You need**.
- **Mods change while the game is running.** The app is in a different prefix from the game.
- Reporting a problem: the log's header names your Wine version, so include it - see [Reporting issues](Reporting-issues).
