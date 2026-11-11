This install's SPT server, the programs your mods ship, and - on a headless setup - the Fika headless client. Playing, and the checks before you do, are on [Profile](Profile).

- **Start server** runs this install's server in its own console window. It keeps running if you close the app - press **Stop** or close its window when you are done.
- **Restart** and **Stop** sit beside it, enabled only while the server is actually running. Both ask first, on the card, because a raid in progress does not survive either and there is no undo. Stop leaves the server down until you start it again.
- If **Stop the server when the game closes** is on (Options, below), the card counts down once the game has closed, with **Keep it running** to call it off this time.
- Only this install's server counts. A second SPT install's server running on the same machine is neither shown as running here nor stopped.
- **Server log** under the server card shows what the server is writing, live, and opens by itself when the server runs without its window. It reads like a console - dark, with errors in red and warnings in yellow - and the mouse wheel over it scrolls only the log; the page scrolls again once the log is at its end and the wheel has rested a moment. **Open logs folder** goes to the files.
- **Mod tools** lists every program a mod put in your SPT folder - SVM's Greed, give-ui, and the like - with **Open**, its folder, and **Hide** for anything that isn't meant to be opened. A tool that needs the server running, or stopped, says so.
- **Fika headless client** gets its own card, and only on a setup that has a headless launcher. A headless hosts raids for other people rather than being the one you play on, so it never shares a button with the game.

## When the app closes or opens with the server up
The server keeps running without the app. If this install's server is still up when you close the app - or quit it from the tray - you're asked: **Stop it and close**, **Leave it running**, or **Cancel**. If the app opens on a server that's already running (left up last time, or started some other way), it says so and offers **Go to Server**, **Stop it** or **Leave it running**. Nothing is asked when the app closes for its own update, or when Windows signs out.

## Starting SPT (Options)
**Options - Starting SPT** has these switches:
- **Open the launcher once the server is up** - after Start server, the page waits until the server is listening on its port (from its `http.json`, and 6969) and then opens the launcher. A heavily modded server gets three minutes.
- **Run the server without its window** - no console window; the server log on this page shows what it is doing, and Stop closes it properly. It still keeps running if you close the app.
- **Stop the server when the game closes** - stops this install's server once the game has been closed for the number of seconds beside it (60 unless you change it; 0 to 3600). The server card counts down with **Keep it running**, and if the app isn't in front a Windows notification offers the same. The game coming back during the wait cancels it. While this install's Fika headless client runs the server is left up and the wait starts once the headless closes too - unless **Don't wait for the headless client** is on (shown only where there is a headless), which stops the server anyway and leaves the headless as it is. Stopping works as **Stop** does, so your profile is saved, and it stops for everyone connected. The app has to be running (the tray counts).
- **Create desktop shortcut** - a Quick play shortcut on your desktop; see [Profile](Profile#quick-play).
- **Show the server on the Profile page** - only while the server runs in its own window. This page's cards move onto [Profile](Profile) and Server leaves the sidebar; anything that would open this page opens Profile at the server card instead. Running the server without its window brings the page back.

> [!NOTE]
> The Fika headless client can be restarted but not stopped from here - close its own window if you want it down.
