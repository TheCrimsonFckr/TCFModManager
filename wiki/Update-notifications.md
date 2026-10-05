Off until you switch it on. With it on, the app asks sp-mod every so often whether any mod you have installed has a new release, and shows a Windows notification when one has - so you hear about an update without going to look for it. It works the same whether the app installs your mods or Monitor mode has you install them.

**Turning it on.** In **Options - Update notifications**, switch on **Notify me when an installed mod has an update**, and pick how often under **Check every** - from 30 minutes to 12 hours, every hour to start with. The first check runs one interval after the app starts, never at launch. **Check now** runs one straight away and says underneath what it found.

**What gets announced.**
- **Only what you have installed is asked about** - one request per check for the lot, not a trawl through the whole catalog. Addons are checked too.
- **An update means what the Installed page means by it:** the newest release that runs on your SPT. A notification never names an update the Installed page doesn't show.
- **Each release is announced once.** Restarting the app doesn't repeat it; a newer release of the same mod is news again.
- **Switching it on doesn't announce what's already there.** The first check notes every update the Installed page already shows, and from then on only new releases are announced. Updates that come out while the app is closed are announced on the first check after it starts.
- **Disabled mods aren't announced**, and neither is an update you've already downloaded in Monitor mode but not installed yet.
- **One notification per check**, however many it found - "3 mod updates available: SAIN 4.5.2, UI Fixes 6.0.2 and 1 more". A newer one replaces an older one still waiting in the Notification Centre.

**Clicking it** - the notification or its **Open** button - brings the app forward on the **Installed** page with **Show** set to **Needs update**. If the app has been closed since, clicking it starts the app on that page. There's no "update all" on the notification: updating goes through the app, where the running-SPT check and the dependency prompt can do their jobs.

When a check finds something, the Installed page and Browse's status dots pick up the new version without a refresh.

## Keeping it running in the tray
Closing the window normally quits the app, and the checks stop with it. Under the notifications switch, turn on **Closing the window keeps the app running in the tray** and closing hides the window instead, leaving the app's icon in the notification area by the clock. The first time it happens, a notification says so. The switch is only available while notifications are on.

- **Click the icon** to open the window again.
- **Right-click it** for **Open**, **Check for updates now** and **Quit**. While this is on, Quit is how you actually close the app.
- **Launching the app again** while it's in the tray just brings the window back.

> [!NOTE]
> Only one copy of the app runs from a folder at a time - launching it again brings the running one forward instead of opening a second. A separate copy kept beside a second SPT install still runs alongside it.

> [!WARNING]
> Windows has the last word on notifications. If none appear, check that TCF Mod Manager is allowed under **Windows Settings - System - Notifications**, and that Do not disturb isn't on. Windows also holds notifications back while a game is full screen, and the app doesn't try to get around that.
