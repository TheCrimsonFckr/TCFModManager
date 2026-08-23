**TCF Mod Manager** is a Windows desktop app for finding, installing and keeping track of your SPT mods, built directly against the sp-mod catalog you're reading this on. Browse the full mod list, filter it down to what actually works on your SPT version, install with dependencies resolved for you, and see at a glance what's out of date.

WPF with Fluent Design, .NET 9, no account or API key needed.

::: information
The released build is self-contained — you don't need to install .NET separately.
:::

## Guide {.tabset}

### Install

**You need**

- Windows
- An SPT install

**Steps**

1. Download `TCF-ModManager-<version>.zip` from this page.
2. Extract it into your SPT folder as `<SPT root>\TCFModManager\` — a sibling of `BepInEx\` and `user\`.
3. Run `TCFModManager.exe`.
4. Open **Options**, point it at your SPT install folder, and hit Save. The detected server version appears underneath — everything else keys off that.

::: warning
This is **not** a mod. Don't extract it into `BepInEx\plugins` or `user\mods`. It's a standalone application that manages that folder for you. Anywhere on disk works, really — it just needs to be told where SPT lives.
:::

#### If the SPT version doesn't detect

The version is read from the server executable — `SPT.Server.exe`, or the older `Aki.Server.exe` — at the install root or under `SPT\` / `SPT_Runtime\`. Point Options at the folder that contains one of those, not at a subfolder.

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

::: information
SPT version constraints are resolved against the live SPT release list rather than parsed as version ranges, so what you see named is a release that actually exists — not the boundary version the mod author wrote the constraint against.
:::

### Installed

Scans your SPT folder for what's actually there — client mods in `BepInEx\plugins` and server mods in `user\mods` — and matches them back to the catalog.

Same search and filters as Browse, plus a filter on update status so you can pull up "everything with an update waiting" in one click. Each card shows the installed version, the latest published one, and the folder it lives in when that differs from the mod name.

Clicking any card opens a dialog with:

- the full version history, with changelogs rendered from the mod page's own rich text
- a link to the mod page
- an **Update** button, when an update applies

Mods can be removed from here too.

::: warning
Mods installed with this app have every file they placed recorded, which is what makes a clean uninstall possible. Anything you installed by hand beforehand has no such record, so removing it deletes its whole folder rather than a known file list.
:::

#### Your configs aren't thrown away

If a server mod has config files of its own (`user\mods\<mod>\config\*.json`), removing it asks what you want done with them: keep them — they're moved to a timestamped folder under `LegacyConfigs\` next to the exe, with their original paths intact so the folder can be copied back over your SPT install — or delete them with the rest of the mod. Updating a mod always keeps them, without asking.

Client mod settings live in `BepInEx\config`, outside the mod's own folder, so removing a mod never touches them.

### Dependencies

Resolves the dependency tree of every installed mod that declares one, and reports each dependency's state against what's actually on disk.

That includes **version conflicts** — where two installed mods want incompatible versions of the same dependency — which is the failure mode that usually shows up as an unexplained crash on load rather than an error message.

Anything missing can be installed straight from the list.

### Downloads

The install queue. Items process one at a time; each resolves its dependencies and queues those alongside it.

- Live progress per item
- **Cancel** on any individual item — cancelling a mod also cancels the dependencies it dragged in
- **Clear finished** to tidy up
- Plain archive downloads, for when you'd rather install something by hand

#### How an install actually runs

The archive is downloaded and extracted into a hidden scratch folder inside your SPT install (`.tcfmm-work\`, swept of stale runs each time), then moved into place.

When you're updating, the previous version is only removed **after** the new one has downloaded and extracted successfully — a failed or cancelled download can't leave you with neither. Once files start being placed, the operation runs to completion rather than tearing out a half-installed mod.

::: warning
Installing and removing both refuse to start while **Tarkov or the SPT server is running** — those hold the very files being replaced. Close them first. The check runs again after the download finishes, in case SPT was launched while it was in progress.
:::

::: information
Before anything is queued, the app asks you to open the mod's page here on sp-mod first — same as installing manually, and it keeps mod authors' page views and instructions in the loop.
:::

### Files & logs

Everything lives next to the exe, not in `%LocalAppData%`:

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

`Data\installed-mods.json` — not folder names, not DLL file versions — is the authority on what's installed and at what version.

#### Logging

Info level by default, rotated daily as `tcfmm-<yyyyMMdd>.log`. To get Debug-level output in the same log, drop an empty file named `verbose` — no extension — next to the exe.

### Limitations

Worth knowing before you rely on it. None of these lose data quietly — they're places where the app either won't help or will tell you it can't.

#### What it can and can't see

- **`BepInEx\patchers` isn't scanned.** A patcher mod won't appear on the Installed page at all, even if this app installed it. Prepatcher-based mods are effectively invisible.
- **Mods nested a folder deeper** — `BepInEx\plugins\Author\ModName\mod.dll` rather than `BepInEx\plugins\ModName\mod.dll` — are listed under the outer folder's name with an unknown version.
- **Mods you installed by hand are matched by folder name**, since there's no install record to read. If the folder name doesn't clearly point at one listing, the mod shows as not found on sp-mod: you can still see and remove it, but not update it from here. A folder name that could plausibly be two different mods is deliberately left unmatched rather than guessed at.

#### Installing

- **Archives have to be packaged normally** — a `BepInEx\`, `user\`, `SPT\` or `SPT_Runtime\` folder at the top, optionally inside one wrapper folder. Anything else is refused with a message telling you to install it by hand, rather than being scattered into your install.
- **Everything in the archive gets installed.** Mods that ship optional variants in separate folders, or a readme, get all of it copied in. Choose-your-variant mods are worth installing by hand.
- **Files are overwritten without a backup.** If two mods ship the same file, the second one installed wins.
- **Removing a mod deletes the files it recorded.** If another mod happens to share one of those files, removing the first takes it with it.
- **You need roughly twice the archive's size free** on the SPT drive — the download and extraction are staged there before anything is placed.
- **Very large mods on a slow connection can time out** and have to be started again; downloads don't resume.

#### Versions and compatibility

- **Compatibility is judged from a mod's most recent releases**, not its whole history. A mod whose newest releases target a later SPT than yours reads as incompatible even if an older release of it would work — check the mod's page in that case.
- **Some version constraints can't be read.** Those mods show "SPT version unknown" and aren't filtered out, on the grounds that hiding something that might work is worse than showing it.
- **Beta and pre-release version numbers** aren't compared precisely, so an update may not be flagged for a mod you're running a pre-release of.
- **The catalog only covers SPT 3.10 and newer.** On older SPT, most of what you could install won't be listed.
- **"Installed" dates** come from the folder's creation date, so a mod updated in place still shows when you first installed it.

#### Scope

- **One SPT install at a time.** The record of what's installed belongs to the app, not to the install it points at, so pointing Options at a second SPT folder will carry the first one's records across. Use a separate copy of the app per install.
- **The catalog refreshes once per session** in the background. Mods published while the app is open won't appear until you press Refresh cache or restart.
- **It won't run while SPT does.** Installing or removing anything with Tarkov or the server open is refused, because those lock the files being replaced.

### Troubleshooting

#### The SPT version shows as unknown

Options needs the folder containing `SPT.Server.exe` (or `Aki.Server.exe`). It also looks under `SPT\` and `SPT_Runtime\`, but not deeper.

#### A mod I know exists shows "nothing compatible"

The mod has no version published for your SPT release line. That's a statement about the mod page, not about your install — check the mod's own versions list.

#### Downloads suddenly stall or fail

The sp-mod API is rate limited at the edge (roughly 40 requests per 10 seconds, 200 per minute). Heavy browsing can hit it. Give it a minute and retry; nothing is cached as a wrong answer in the meantime.

#### A mod I installed by hand isn't listed under Installed

It isn't in `BepInEx\plugins` or one of the `user\mods` layouts — see Limitations for the folder shapes that aren't scanned. A mod that *is* listed but shows as not found on sp-mod is there, just unmatched.

#### "Close Tarkov / SPT.Server before installing a mod"

Exactly what it says: those hold open the files being replaced. Close the game and the server window, then try again.

#### An install failed halfway

The files placed before it failed are recorded, so they stay under the app's control — install the mod again to complete it, or remove it to clear them out. The scratch folder `.tcfmm-work\` inside your SPT install is swept on the next run. The usual cause is SPT being started mid-install.

#### Reporting a bug

Grab `Data\logs\tcfmm-<date>.log` — ideally after adding the `verbose` marker file and reproducing the problem — and open an issue at [github.com/TheCrimsonFckr/TCFModManager](https://github.com/TheCrimsonFckr/TCFModManager).

{.endtabset}
