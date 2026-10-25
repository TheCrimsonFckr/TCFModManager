**Conflicts** come first: mods that will fight when the game loads. Worked out from your install alone, so it works offline and for mods you installed by hand, and checked again every time you open the page.

- **The same plugin installed twice** - two folders registering one plugin, such as `SAIN` and `SAIN.4.4.3`. BepInEx loads only one, and you don't get to choose which.
- **The same server mod installed twice** - SPT skips a server mod whose ID it finds twice, so **neither copy loads**.
- **Different copies of one file** - two mods each shipping their own, different copy of a library. Only one copy loads, for everyone. Identical copies are fine and aren't listed.

Each lists every mod involved with the folder it's in, and a button to open it. For a mod installed twice, **Keep this one** keeps that copy and removes the others the normal way - a mod's client and server halves together - so **Undo** on the Installed page can put them back. It's greyed out when removing a copy would also take files that aren't duplicated: hover it to see why, and use **Remove** on the Installed page instead. A mod in a conflict shows a red status on the Installed page, where **Show - Has conflicts** narrows the list to them and the status line counts them with a link here. The Profile page warns too, without stopping you launching.

**Dependencies** resolves the dependency tree of every installed mod that declares one, and reports each dependency's state against what's actually on disk.

Dependencies you've disabled are called out as disabled rather than missing. Anything missing can be installed straight from the list.

## Version conflicts
Mods often need a particular version of another mod - a WTT mod needing CommonLib 3.0.x, say. The page checks two sources, and treats them differently:

- **The mod's own files** - the versions it declares, which are what SPT and BepInEx actually check. A miss here is **red**: the mod won't load. On the server side it is worse - SPT then loads **no server mods at all** - so a red banner at the top of the page names the mod, and the Profile page warns before you start (it doesn't stop you).
- **sp-mod's listing** - the versions the author listed. Authors often list one exact version when others work fine, so a miss here is only **amber**. If you've checked it works, **Accept** clears it for those two versions; it comes back if either one changes.

The **Version conflicts** block above the per-mod lists shows one card per dependency that needs attention, with every mod asking for it and what each one needs. From there:

- **Switch to x** installs the version of the dependency that every mod's files accept. When no single version suits them all, it offers the one that suits the most, and asks first, naming the mods it would stop loading.
- **Update to x** on a mod's row installs that mod's own update, when one is on offer - a newer version may accept what you have.
- **Disable** on a red row turns the asking mod off, so it stops asking. Anything that needs it is asked about first, as on the Installed page, and you turn it back on from Installed.

Optional dependencies are listed greyed out: never a warning when missing, still checked when installed. A mod that won't load is red on the Installed page too, included in **Show - Has conflicts**, and installing an update that would push another mod into that state asks first.

## Moving to a newer SPT
At the top of the page, **Moving to a newer SPT** answers "if I moved to SPT x, which of my mods would come with me?" Pick a release newer than yours and every installed mod is listed as **ready** (the version you have runs on it), **update first** (a newer version does - it names which), **not ready yet** (nothing is published for it), or **check by hand** (installed by hand and not matched to sp-mod, or its listing doesn't say). It's worked out from the mods' sp-mod listings, and nothing is changed.
