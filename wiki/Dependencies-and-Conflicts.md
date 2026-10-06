**Conflicts** come first: mods that will fight when the game loads. Worked out from your install alone, so it works offline and for mods you installed by hand, and checked again every time you open the page.

- **The same plugin installed twice** - two folders registering one plugin, such as `SAIN` and `SAIN.4.4.3`. BepInEx loads only one, and you don't get to choose which.
- **The same server mod installed twice** - SPT skips a server mod whose ID it finds twice, so **neither copy loads**.
- **Different copies of one file** - two mods each shipping their own, different copy of a library. Only one copy loads, for everyone. Identical copies are fine and aren't listed.

Each lists every mod involved with the folder it's in, and a button to open it. For a mod installed twice, **Keep this one** keeps that copy and removes the others the normal way - a mod's client and server halves together - so **Undo** on the Installed page can put them back. It's greyed out when removing a copy would also take files that aren't duplicated: hover it to see why, and use **Remove** on the Installed page instead. A mod in a conflict shows a red status on the Installed page, where **Show - Has conflicts** narrows the list to them and the status line counts them with a link here. The Play page warns too, without stopping you launching.

**Dependencies** resolves the dependency tree of every installed mod that declares one, and reports each dependency's state against what's actually on disk.

That includes **version conflicts** - where two installed mods want incompatible versions of the same dependency - which is the failure mode that usually shows up as an unexplained crash on load rather than an error message. Dependencies you've disabled are called out as disabled rather than missing.

Anything missing can be installed straight from the list.

## Moving to a newer SPT
At the top of the page, **Moving to a newer SPT** answers "if I moved to SPT x, which of my mods would come with me?" Pick a release newer than yours and every installed mod is listed as **ready** (the version you have runs on it), **update first** (a newer version does - it names which), **not ready yet** (nothing is published for it), or **check by hand** (installed by hand and not matched to sp-mod, or its listing doesn't say). It's worked out from the mods' sp-mod listings, and nothing is changed.
