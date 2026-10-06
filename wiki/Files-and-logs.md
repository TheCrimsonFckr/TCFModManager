Everything lives next to the exe:

| Path | What |
| --- | --- |
| `Data\settings.json` | SPT install path and app settings |
| `Data\installed-mods.json` | What this app installed, and every file it placed |
| `Data\mod_cache.json` | Cached catalog |
| `Data\spt_versions.json` | Cached SPT release list, refetched daily |
| `Data\dependency_flags.json` | Per-mod "has dependencies" answers, re-checked when a mod publishes |
| `Data\mod_groups.json` | Your groups, and which mod is in which |
| `Data\mod_lists.json` | Your mod lists, which ones you follow, your pinned mods, and the single undo point |
| `Data\downloads.json` | Monitor mode: each archive saved for you to install, what it would place, and whether you've confirmed it |
| `Data\update_notifications.json` | Update notifications: which releases have already been announced, so none is announced twice |
| `Data\addon_cache.json` | Cached addon catalog |
| `Data\mod_footprints.json` | Cached footprint readings, only if that page is on |
| `Data\config-backups\` | One timestamped folder per config save, laid out like your install |
| `Data\logs\tcfmm-<date>.log` | Daily log |
| `Staging\` | Default destination for manually downloaded archives |
| `Data\LegacyConfigs\` | Config files kept from removed and updated mods, one timestamped folder each |
| `Data\ProfileBackups\` | Copies of your SPT profiles taken before mods change, one folder per SPT install, the last 10 kept - see **Options, SPT profile backups** |
| `Data\install-journal\` | A note for an install in progress, gone once it finishes; one left over means the app was stopped part way, and that mod is shown as partly installed |
| `Data\overwritten\` | Files an install replaced that weren't another installed mod's, kept to put back when that mod is removed |
| `Data\ConfigBaselines\` | A copy of the config files each mod version shipped, which is what lets an update tell your changes from the author's |
| `Data\mod_configs.json` | Your per-mod choice of what an update does with that mod's configs, and any unusual places it keeps them |
| `Data\ServerMap\` | On a server: the shared key (`servermap-key.txt`), the list it publishes, and the map's machines (`clients.json`) |
| `.tcfmm-update\` | Hidden. Only exists while an app update is downloading, or if one failed; cleaned up on the next launch |

Three more folders are created inside your **SPT install**, all hidden: `.tcfmm-work\` (scratch space while a mod installs, swept each run), `.tcfmm-duplicates\` (copies set aside by **Sort out**, kept until you delete them) and `.tcfmm-removed\` (removed mods, kept for Undo as long as **Keep removed mods** says).

`Data\installed-mods.json` - not folder names, not DLL file versions - is the authority on what's installed and at what version.

## Logging
Info level by default, rotated daily as `tcfmm-<yyyyMMdd>.log`. To get Debug-level output in the same log, drop an empty file named `verbose` - no extension - next to the exe.
