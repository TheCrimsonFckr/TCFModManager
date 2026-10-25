This install's SPT server, the programs your mods ship, and - on a headless setup - the Fika headless client. Playing, and the checks before you do, are on [Profile](Profile).

- **Start server** runs this install's server in its own console window. It keeps running if you close the app - press **Stop** or close its window when you are done.
- **Restart** and **Stop** sit beside it, enabled only while the server is actually running. Both ask first, on the card, because a raid in progress does not survive either and there is no undo. Stop leaves the server down until you start it again.
- Only this install's server counts. A second SPT install's server running on the same machine is neither shown as running here nor stopped.
- **Server log** under the server card shows what the server is writing, live, and opens by itself when the server runs without its window. It reads like a console - dark, with errors in red and warnings in yellow - and the mouse wheel over it scrolls only the log; the page scrolls again once the log is at its end and the wheel has rested a moment. **Open logs folder** goes to the files.
- **Mod tools** lists every program a mod put in your SPT folder - SVM's Greed, give-ui, and the like - with **Open**, its folder, and **Hide** for anything that isn't meant to be opened. A tool that needs the server running, or stopped, says so.
- **Fika headless client** gets its own card, and only on a setup that has a headless launcher. A headless hosts raids for other people rather than being the one you play on, so it never shares a button with the game.

## When the app closes or opens with the server up
The server keeps running without the app. If this install's server is still up when you close the app - or quit it from the tray - you're asked: **Stop it and close**, **Leave it running**, or **Cancel**. If the app opens on a server that's already running (left up last time, or started some other way), it says so and offers **Go to Server**, **Stop it** or **Leave it running**. Nothing is asked when the app closes for its own update, or when Windows signs out.

## Starting SPT (Options)
**Options - Starting SPT** has two switches:
- **Open the launcher once the server is up** - after Start server, the page waits until the server is listening on its port (from its `http.json`, and 6969) and then opens the launcher. A heavily modded server gets three minutes.
- **Run the server without its window** - no console window; the server log on this page shows what it is doing, and Stop closes it properly. It still keeps running if you close the app.

> [!NOTE]
> The Fika headless client can be restarted but not stopped from here - close its own window if you want it down.
