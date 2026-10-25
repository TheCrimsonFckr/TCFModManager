Some mods have **addons** - extra content published against the mod itself rather than against an SPT release. A voice-command pack for a companion mod, a Linux build of a GUI, an overlay for a web tool.

Addons aren't listed separately, because an addon is no use without its parent. Open a mod - from Browse or from Installed - and its addons are listed at the bottom of the dialog, each with its own version picker and Install button.

### Finding an addon
Search on Browse finds addons as well as mods, by name, teaser or `@author`. The mod an addon belongs to is listed in the results with the addon named on its card, and the matching addons are in an **Addons** bar above the results (collapsed until you open it; the **Addon matches** switch beside the search box turns it off). Clicking one opens a small window for just that addon - its versions, notes and **Install**, and a button to open its mod. A mod that your filters hide keeps its addons hidden too, and an addon sp-mod no longer ties to a mod opens its sp-mod page.

The thing that makes an addon different from a mod is what it's measured against:

### A mod version says: which **SPT release** it needs.
### An addon version says: which version of its **parent mod** it needs

So the app checks each addon version against the mod you actually have installed. If none of them fit, the addon is still listed - with the reason, rather than quietly missing:

> Needs Raid Review ^1.5.0 - you have 1.4.2

Requirements are read the way sp-mod reads them: `~1.17` is 1.17 up to below 2.0, `1.*` and `~1` are any 1.x, and a bare `1` or `1.16.0` is that release only. Looser forms such as `>= 1.0.1, < 2.0.0` or `v1.19.0` are read too. If a newer release of the mod would let a blocked addon in, the row says so: *Updating Camo And Stickers to 1.19.0 makes it available.*

### Installing an addon anyway
Authors don't always update an addon's list of mod versions when the mod moves on - a Fika sync addon still listing Icebreaker 1.1.0 after Icebreaker 1.1.3 is out, say. If you know it works, press **Install anyway** under the blocked version and confirm. It installs like any addon, and its card on Installed shows **Installed anyway** (a warning, not "nothing fits"), naming the mod version its author doesn't list. When a version that does fit is published, it's offered as an ordinary update, and installing it takes the warning away. You can't install an addon anyway if its mod isn't installed.

Updating the mod later doesn't quietly undo your choice: if no version of the addon fits the new version either, you're asked whether to keep it - **Keep enabled** (the default) leaves it enabled, **Disable after the update** disables it once the update has installed. A mod list you capture remembers it too, and applying that list installs the addon the same way.

Some authors put an SPT version where the mod's version belongs - ORBIT - Fika Addon asks for ORBIT ~4.1.6, which is ORBIT's own SPT range. When a requirement doesn't fit your mod but does fit your SPT, the app counts it as fitting - the same way sp-mod's own page lists such an addon as compatible.

## Installed addons
Every addon you install gets its own card on the Installed page, with *Addon for* and its mod under its name - including the usual kind that installs into its mod's own folder (a sticker pack in `BepInEx\plugins\7Bpencil.WeaponCamoAndStickers`, say). It updates, disables and removes like any mod, and touches only its own files. A new addon card starts in its mod's group.

- **Removing a mod** asks whether to remove its addons too - **Remove addons too** (the default) or **Remove the mod only**. They go first, then the mod, and one **Undo** puts the lot back, listed as *Camo And Stickers and its addons*. A disabled addon is named and left in place.
- **Disabling a mod** lists its addons with the mods that depend on it; **Disable these too** takes them along. Enabling the mod later offers back exactly the addons that went with it - not ones you'd turned off yourself.
- **Multi select** works the same way, asking once for the whole selection.

An addon's update is the newest version that fits the mod you have installed, and it updates in one click like a mod - its card's **Update** button, or **Update selected**. An addon whose installed version doesn't fit its mod shows the grey "nothing fits" status, with *Doesn't fit Camo And Stickers 1.19.0* on hover, and its card names a version that does fit when there is one, even an older one. One you [installed anyway](#installing-an-addon-anyway) shows **Installed anyway** instead.

To see your addons together: **Show only - Has installed addons** lists every mod with addons installed alongside those addons, and right-clicking a mod or one of its addons offers **Show Camo And Stickers and its addons**, which narrows the page to just that family. Its pill's X goes back.

**Mod lists cover them too** - capturing a list records your addons, and applying one installs, updates or disables them alongside your mods. An addon goes to the same machines its own files say it's for, so a client-side sticker pack isn't sent to a server.

> [!NOTE]
> Installing an addon from its mod's dialog won't drag the mod in behind it - install the mod first, then its addon. Anything else the addon needs is offered the usual way, on the Downloads page.
>
> A mod list is different: **Preview** offers the mod of any addon on the list that the list doesn't name, under **Also needed**, at the newest version every listed addon accepts. And if a list pins an addon at a version that won't fit the version of its mod the list leaves you with, applying asks whether to take the newest one that does instead.

> [!WARNING]
> A mod list containing an addon needs this version of the app or newer to open. Sharing one with someone on an older build will tell them the file is too new - send them a list without addons, or ask them to update. Lists with no addons in them are unaffected.

> [!WARNING]
> **Update, Redownload and Remove are unavailable while a mod is disabled.** The record of what it installed points at folders it no longer occupies, so those actions would put files in the wrong place. Enable the mod first, then update it. Browse refuses a reinstall of a disabled mod for the same reason.

## Updating a mod that has addons
Updating, downgrading or redownloading a mod checks its installed addons against the version you pick. The update dialog lists them under **Your addons with 1.19.0** - *Fits*, *Doesn't fit - 1.1.0 does*, or *No version fits* - and rechecks as you pick another version.

Pressing **Update** asks only when an addon is affected:

- **An addon with another version that fits** - **Update mod and addons** updates the mod and changes the addon to that version, queued behind the mod so cancelling the mod cancels it too. That version can be older than the one you have. **Update mod only** leaves the addon as it is.
- **An addon with no version for the new one** - **Update anyway** goes ahead and disables the addon once the update has installed (one you [installed anyway](#installing-an-addon-anyway) is asked about separately, and kept unless you say otherwise). Its files and settings stay, and you can enable it from its card. If the mod later goes back to a version the addon fits, the app offers to enable it again.

**Update selected** and a card's **Update** button ask the same, once for the whole selection. When a selection has a mod and its addon, the addon gets the newest version that fits the mod's new version. In Monitor mode nothing is installed, so nothing is disabled.

## If a mod ends up in two places at once
A move interrupted partway - or one done by hand - can leave the same mod in both the normal folder and the `.disabled` one. When that happens the card says so and offers **Sort out**: pick which copy to keep, and the other is moved into a hidden `.tcfmm-duplicates` folder inside your SPT install rather than deleted. Undo puts that back too.
