The install queue. Items process one at a time; each resolves its dependencies and queues those alongside it.

- Live progress per item
- **Cancel** on any individual item - cancelling a mod also cancels the dependencies it dragged in
- **Clear finished** to tidy up
- Plain archive downloads, for when you'd rather install something by hand
- **Not for Fika:** on an install that runs Fika, a version sp-mod marks as not working with Fika is asked about before it downloads. **No** leaves just that one out; the default is No.

## How an install actually runs
The archive is downloaded and extracted into a hidden scratch folder inside your SPT install (`.tcfmm-work\`, swept of stale runs each time), then moved into place.

SPT's, BepInEx's and the game's own files are never replaced. When a mod replaces a file you put there yourself - a hand-installed mod's, say - the original is kept and goes back if the mod is removed. The result line says what was left as it was.

When you're updating, the previous version is only removed **after** the new one has downloaded and extracted successfully - a failed or cancelled download can't leave you with neither. Once files start being placed, the operation runs to completion rather than tearing out a half-installed mod.

> [!WARNING]
> Installing and removing both refuse to start while **T***** or the SPT server is running** - those hold the very files being replaced. Close them first. The check runs again after the download finishes, in case SPT was launched while it was in progress.

> [!NOTE]
> Before anything is queued, the app asks you to open the mod's page on sp-mod first - same as installing manually, and it keeps mod authors' page views and instructions in the loop.
