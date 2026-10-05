An optional page - **off by default**, turned on under **Options - Mod footprint page** - with one row per installed mod, showing what each mod's files contain. Open a row for the full breakdown; rows sort by patch classes, components or size.

**Nothing is measured.** The app does not launch the game, load a mod or run any mod's code. Nothing is timed or profiled, no frame rates are reported, and there are no scores, ratings or rankings. Everything is read from the files on disk:

- **Patch classes shipped**, and components the engine would call on a timer - per-frame updates, physics steps, on-screen interface drawing, camera hooks - broken down by which.
- **Asset bundles** and their size, held in memory once loaded.
- **Whether it ships a preloader patcher**, which runs before the game loads.
- **Whether it has a server half** under `user\mods`, counted separately and never folded into the client figures.
- **Disk usage**, in files and megabytes.

## What the figures do not cover
- It counts patch classes, not patched methods. One class can target several methods, so the real number can be higher.
- It cannot see what a patch touches. A patch on a per-frame method and a patch on a menu button are counted the same way.
- A declared component only runs if the mod creates and enables one, which the files do not show. Every line says *declares* or *ships*.
- Some mods are packed or obfuscated in ways it cannot parse. The breakdown says so rather than guessing.

> [!NOTE]
> Readings are cached in `Data\mod_footprints.json` and a mod is re-read only when its files change; **Rescan** forces a fresh read. Disabled mods are included and marked as not loaded. Nothing is uploaded - the page makes no network requests at all.
