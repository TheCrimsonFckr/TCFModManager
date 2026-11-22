The install queue. Up to three items download at once while the ones already downloaded install one at a time, in the order they were queued; each resolves its dependencies and queues those alongside it, and a library installs before the mods that need it. A download that breaks off is tried again twice, carrying on from where it stopped when the server allows it. One that gets a server error - sp-mod, or the site the file is hosted on, not answering or turning requests away - is tried three more times over about 50 seconds, or after the wait the server asks for, up to a minute. While one site is failing, other downloads from it wait instead of failing too. Monitor-mode saves and app updates are retried the same way.

While anything is downloading or installing, a bar along the bottom of the window says what and how far the whole queue has got; click it to come here. It goes a few seconds after the queue finishes.

- Live progress per item
- **Cancel** on any individual item - cancelling a mod also cancels the dependencies it dragged in
- **Clear finished** to tidy up
- **Pause** - downloads under way stop where they are, and **Resume** carries them on from there; an install already under way finishes. It's also on the downloads bar. Closing the app keeps any unfinished downloads, and they come back paused next time, with the questions you already answered remembered.
- **Unpacked size** - once an archive is in, its card shows how big it is unpacked, and the queue shows the total still to unpack.
- **Free space** - when the queue starts, it checks your SPT drive has room for about three times what it's about to download, plus 500 MB. If not, it pauses before anything starts and says how much it needs and how much is free; **Download anyway** goes ahead.
- Plain archive downloads, for when you'd rather install something by hand
- **Not for Fika:** on an install that runs Fika, a version sp-mod marks as not working with Fika is asked about before it downloads. **Cancel** (the default) leaves just that one out; **Install anyway** or **Download anyway** goes ahead.

## How an install actually runs
The archive is downloaded and extracted into a hidden scratch folder inside your SPT install (`.tcfmm-work\`, swept of stale runs each time), then moved into place.

SPT's, BepInEx's and the game's own files are never replaced. When a mod replaces a file you put there yourself - a hand-installed mod's, say - the original is kept and goes back if the mod is removed. The result line says what was left as it was.

When you're updating, the previous version is only removed **after** the new one has downloaded and extracted successfully - a failed or cancelled download can't leave you with neither. Once files start being placed, the operation runs to completion rather than tearing out a half-installed mod.

> [!WARNING]
> Nothing is placed while **T***** or the SPT server is running** from the install - those hold the very files being replaced. A queued install still downloads, then waits with its card saying what it's waiting for, and installs as soon as they close. Removing, disabling, enabling, going back and the profile actions ask first instead: with only the server running they offer to stop it and carry on; the game you close yourself.

> [!NOTE]
> Before anything is queued, the app asks you to open the mod's page on sp-mod first - same as installing manually, and it keeps mod authors' page views and instructions in the loop.
