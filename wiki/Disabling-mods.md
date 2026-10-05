Turn a mod off for a run without uninstalling it. Nothing is deleted, and nothing is lost.

Disabling moves the mod into a `.disabled` copy of the folder SPT loads it from - `user\mods` becomes `user\mods.disabled`, `BepInEx\plugins` becomes `BepInEx\plugins.disabled`. SPT doesn't look in those folders, so the mod simply isn't loaded. Enabling moves it straight back where it came from.

Its own files travel with it, server configs included, and client settings in `BepInEx\config` are never touched - so switching a mod off and back on again loses no settings.

**How to disable something**

- One mod, from its card, its List row, or its row in Groups view
- Several at once, by ticking them in Cards view's **Select** mode
- A whole group, with its **enable all** / **disable all** / **invert** buttons

Disabled mods stay in the list, dimmed and marked, and the enabled/disabled filter pulls up either set on its own.

**You get a warning before you break something.** If disabling a mod would take away something another mod depends on - or if you enable a mod whose own dependencies are still switched off - the app lists what's affected and offers to carry those along. Dependencies are read from the mods themselves, so this works offline and covers mods you installed by hand that were never matched to a listing here.

**Undo** puts the last change back.

> [!NOTE]
> Whether a mod is disabled is worked out purely from where it sits on disk. Move folders around by hand if you prefer - the app reads the result correctly on the next scan.
