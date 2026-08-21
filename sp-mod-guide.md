**TCF Mod Manager** is a Windows desktop app for finding, installing and keeping track of your SPT mods, built directly against the sp-mod catalogue you're reading this on. Browse the full mod list, filter it down to what actually works on your SPT version, install with dependencies resolved for you, and see at a glance what's out of date.

WPF with Fluent Design, .NET 9, no account or API key needed. The released build is self-contained you don't need to install .NET separately.

## Guide {.tabset}

### Install

**You need**

- Windows
- An SPT install

**Steps**

1. Download `TCF-ModManager-<version>.zip` from this page.
2. Extract it into your SPT folder as `<SPT root>\TCFModManager\`.
3. Run `TCFModManager.exe`.
4. Open **Options**, point it at your SPT install folder, and hit Save. The detected server version appears underneath everything else keys off that.

#### Warning
This is **not** a mod. Don't extract it into `BepInEx\plugins` or `user\mods`. It's a standalone application that manages that folder for you. Anywhere on disk works, really it just needs to be told where SPT lives.

#### If the SPT version doesn't detect

The version is read from the server executable `SPT.Server.exe`, or the older `Aki.Server.exe` at the install root or under `SPT\` / `SPT_Runtime\`. Point Options at the folder that contains one of those, not at a subfolder.

### Browse

The whole sp-mod catalog, fetched once and cached to disk so it opens instantly next time.

- **Search** by name, or by author with `@author`
- **Filter** by SPT release line, category, Fika compatibility and featured status
- **Toggles** for hiding ads and AI-generated content
- **Refresh cache** re-pulls the catalog when you want the newest listings

Each card carries a status dot and, where it applies, a badge for mods that pull in dependencies:

| Status | Meaning |
| --- | --- |
| Installed | You have it, and it's current |
| Update available | A newer version has been published |
| Not installed | Available for your SPT version |
| Nothing compatible | The mod exists, but has no release for your SPT version |

Clicking a card opens its details, including version history and a link to its page here.

#### Information
SPT version constraints are resolved against the live SPT release list rather than parsed as version ranges, so what you see named is a release that actually exists not the boundary version the mod author wrote the constraint against.


### Installed

Scans your SPT folder for what's actually there client mods in `BepInEx\plugins` and server mods in `user\mods` and matches them back to the catalog.

Same search and filters as Browse, plus a filter on update status so you can pull up "everything with an update waiting" in one click. Each card shows the installed version, the latest published one, and the folder it lives in when that differs from the mod name.

Clicking any card opens a dialog with:

- the full version history, with changelogs rendered from the mod page's own rich text
- a link to the mod page
- an **Update** button, when an update applies

Mods can be removed from here too.

#### Warning
Mods installed with this app have every file they placed recorded, which is what makes a clean uninstall possible. Anything you installed by hand beforehand has no such record.


### Dependencies

Resolves the dependency tree of every installed mod that declares one, and reports each dependency's state against what's actually on disk.

That includes **version conflicts** where two installed mods want incompatible versions of the same dependency which is the failure mode that usually shows up as an unexplained crash on load rather than an error message.

Anything missing can be installed straight from the list.

### Downloads

The install queue. Items process one at a time; each resolves its dependencies and queues those alongside it.

- Live progress per item
- **Cancel** on any individual item cancelling a mod also cancels the dependencies it dragged in
- **Clear finished** to tidy up
- Plain archive downloads, for when you'd rather install something by hand

#### How an install actually runs

The archive is downloaded and extracted into a hidden scratch folder inside your SPT install (`.tcfmm-work\`, swept of stale runs each time), then moved into place.

When you're updating, the previous version is only removed **after** the new one has downloaded and extracted successfully a failed or cancelled download can't leave you with neither. Once files start being placed, the operation runs to completion rather than tearing out a half-installed mod.

Installing and removing both refuse to start while Tarkov or the SPT server is running, since
those hold open the files being replaced; the check runs again once the download finishes, in case
SPT was launched while it was in progress. If a file placement fails anyway, what was placed
before the failure is still recorded and marked incomplete, so those files stay app-managed - a
reinstall completes the mod, a removal clears it out.

A server mod's own config files (`user\mods\<mod>\config\*.json`) aren't deleted with the
rest of it. Removing a mod asks whether to keep them - moved into a timestamped folder under
`LegacyConfigs\` with their install-relative paths intact, so the folder can be copied back over
an SPT install to restore them - or delete them. Updates always keep them, without asking.

#### Information
Before anything is queued, the app asks you to open the mod's page here on sp-mod first same as installing manually, so it keeps mod authors' page views and instructions in the loop as per best practice this does the same for dependacies as well.


### Files & logs

Everything lives next to the exe to contain it reach:

| Path | What |
| --- | --- |
| `Data\settings.json` | SPT install path and app settings |
| `Data\installed-mods.json` | What this app installed, and every file it placed |
| `Data\mod_cache.json` | Cached catalog |
| `Data\spt_versions.json` | Cached SPT release list, refetched daily |
| `Data\dependency_flags.json` | Per-mod "has dependencies" answers, re-checked when a mod publishes |
| `Data\logs\tcfmm-<date>.log` | Daily log |
| `Staging\` | Default destination for manually downloaded archives |
| `LegacyConfigs\` | Config files kept from removed mods, one timestamped folder per removal |

`Data\installed-mods.json` not folder names, not DLL file versions is the authority on what's installed and at what version.


#### Logging

Info level by default, rotated daily as `tcfmm-<yyyyMMdd>.log`. To get Debug-level output in the same log, drop an empty file named `verbose` no extension next to the exe.

### Troubleshooting

#### The SPT version shows as unknown

Options needs the folder containing `SPT.Server.exe` (or `Aki.Server.exe`). It also looks under `SPT\` and `SPT_Runtime\` depending on the version, but not deeper.

#### A mod I know exists shows "nothing compatible"

The mod has no version published for your SPT release line. That's a statement about the mod page, not about your install check the mod's own versions list.

#### Downloads suddenly stall or fail

The sp-mod API is rate limited at the edge (roughly 40 requests per 10 seconds, 200 per minute). Heavy browsing can hit it. Give it a minute and retry; nothing is cached as a wrong answer in the meantime.

#### A mod I installed by hand isn't listed under Installed

Two likely causes: it isn't in `BepInEx\plugins` or one of the `user\mods` layouts, or it can't be matched back to a catalog entry. Mods installed into `BepInEx\patchers` are **not** currently scanned.

#### An install failed halfway

Nothing needs cleaning up by hand. The scratch folder `.tcfmm-work\` inside your SPT install is swept on the next run, and if you were updating, your previous version is still in place.

#### Reporting a bug

Grab `Data\logs\tcfmm-<date>.log` ideally after adding the `verbose` marker file and reproducing the problem and open an issue at [github.com/TheCrimsonFckr/TCFModManager](https://github.com/TheCrimsonFckr/TCFModManager).


### Known limitations

**What it can see**

- `BepInEx\patchers` isn't scanned, so a patcher mod never appears on the Installed page - even one
  this app installed.
- Mods nested a level deeper than convention (`BepInEx\plugins\Author\ModName\`) are listed under
  the outer folder's name, with an unknown version.
- Mods installed by hand are matched to a catalog listing by folder name, since there's no record to
  read. An ambiguous folder name is deliberately left unmatched rather than guessed at, so such a
  mod can be listed and removed but not updated from here.

**Installing**

- Archives must be packaged conventionally - a `BepInEx\`, `user\`, `SPT\` or `SPT_Runtime\` folder
  at the top, optionally inside one wrapper folder. Anything else is refused rather than scattered
  into the install.
- Everything inside the archive is installed, including readmes and optional-variant folders;
  choose-your-variant mods are better installed by hand.
- Files are overwritten with no backup, and uninstall deletes the files the record lists - a file
  shared by two mods goes with whichever is removed first.
- Installing needs roughly twice the archive's size free on the SPT drive, and downloads don't
  resume, so a large mod on a slow connection can time out and need restarting.

**Versions**

- Compatibility is judged from the most recent versions the catalog returns per mod, not the full
  history, so a mod with an older release that would work on this SPT still reads as incompatible.
- Constraint forms the range parser can't read fall back to "SPT version unknown" and are left
  unfiltered rather than hidden.
- Pre-release/beta version numbers aren't compared precisely, so an update may not be flagged
  against one.
- The catalog is filtered to SPT 3.10 and newer.
- Installed dates come from folder creation time, so a mod updated in place keeps its original date.

**Scope**

- The install manifest belongs to the app rather than to the SPT install it points at, so pointing
  Options at a second install carries the first one's records across. Use a separate copy of the app
  per install.
- The catalog refreshes once per session in the background; Refresh cache forces it.

{.endtabset}
