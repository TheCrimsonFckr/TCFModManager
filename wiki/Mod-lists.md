A **mod list** is a named set of mods and the versions of them you run. Capture what you have now, switch between sets, send one to a friend, or follow the one a server publishes. It records mods and versions, never files, so capturing is instant however big your install is.

**New list → From what's installed…** saves whatever is enabled right now, under a name you give it; **New list → Empty list…** starts one with nothing on it, for building a list by hand. Pick a list on the left and its name, **Preview** and a **⋯** menu sit at the top. **Preview** works out what applying it would do, and nothing moves until you press **Apply** - which takes Preview's place once a plan is showing, so the button is always the next step. The preview starts with coloured counts - *1 missing* in red, *2 incorrect versions* in amber, *1 disabled* in blue, *72 correct* in green, and grey for anything you have to fetch yourself, anything it would set aside and pinned mods. Every row below reads like the list itself - name, folder, version, scope - with a badge in the same colours and the icon the Installed page uses for that state. An incorrect version shows the move, *3.0.0 → 3.1.2*, and the Apply button says what it is about to do: *Install 1 mod*, *Install 2, update 1*.

**Apply** downloads what the list names and you do not have, enables what you have but had switched off, and moves anything the list does not name into the disabled folder. **Nothing is ever deleted.** Setting a mod aside is the same move the Installed page's disable button makes, and it is near-instant however large the mod is.

**Pin** the mods you want kept whatever list you apply - a HUD, a sound pack, the quality-of-life mods that don't belong on any one list. A pinned mod is never set aside: Preview lists it as **Pinned** instead, with a button to unpin it. Pin from that Preview row, or with **Pin** on the mod's opened card on the Installed page. A pinned mod shows a pin under its disable button on Installed, beside its name in any list's contents, and on its Browse card. Pins belong to your install and never travel with a list you share.

**Also needed.** Preview asks sp-mod what the mods on the list need, and lists any the list doesn't name under **Also needed**, with what needs each one - to install, to enable, or to keep on when the list would otherwise set it aside. Ticked ones go with the apply; untick any you don't want. They are not added to the list itself unless you tick **Also add the ticked ones to this list** (your own lists only). One already installed at another version is left as it is - the Dependencies and Conflicts page judges versions. An addon's own mod is offered here too when the list doesn't name it, at the newest version every listed addon accepts. If sp-mod can't be reached, Preview says so and you can still apply.

**Undo last apply** (bottom left) puts the install back the way it was before the last apply. There is one undo point, kept up to date for you: applying a list replaces it, and using it clears it.

> [!WARNING]
> The disable half needs the game and the server closed - BepInEx holds the files it has loaded open. Preview says so before you apply.

## Editing a list
The pencil next to a list's name renames it (Enter saves, Esc cancels). Everything else you do to the list as a whole - export, copy, publish, refresh, stop following, delete - is in its **⋯** menu.

Each mod on a list is a card: its thumbnail, author, downloads and category, its sp-mod summary and badges, with folder, version and remove at the top right and its scope at the bottom right. **Compact**, above the list, switches to one short row per mod - easier to scan on a long list - and **Cards** switches back. The choice is remembered.

Changing what's on it is three steps, and they are separate on purpose:

1. **Add mods**, a row's scope dropdown and the **X** on a row change the list on screen only.
2. **Save**, on the bar that appears above the mods while there are changes, writes the list. **Discard** throws the changes away. No downloads, nothing moved - the only thing Save changes is what the list says.
3. **Apply** is the only thing on this page that touches your game folder.

**Add mods** offers what is installed here *and* the whole sp-mod catalog, so a list can name a mod this machine has never had. The installed tab filters by your groups from the Installed page and sorts by name or most recently installed; the sp-mod.com tab filters by category and to your SPT version, and sorts by downloads, last update or name. Mods already on the list are hidden unless you untick that. A mod added from the catalog is left unlocked - it means "the newest published version", because a version nobody here has run is not a version anybody has tested. **Refresh versions** re-reads the versions of every mod on the list that is installed here, for after an update round has moved a dozen of them.

Preview and Apply are switched off while there are unsaved changes, and the page says why: they work off the stored list, and running them against something the panel no longer agrees with is the one genuinely confusing state this page can reach.

## Sharing a list
**Export** (in the list's **⋯** menu) writes a `.tcfmodlist` file. Send it to whoever you like; **New list → Import a file…** reads it back.

What travels is a manifest - "install mod 2426 at version 5" - and never mod files. The receiving app downloads from sp-mod.com exactly as it would for any other install, which is faster than anything a person could serve you, costs the sender no bandwidth, raises no redistribution question, and keeps the mod author's download count honest.

- **A list you imported is read-only**, and so is one a server served you - a blue bar above its mods says so. **Make a copy** on that bar (or in **⋯**) turns it into one of your own, pointing back at where it came from.
- **Mods the catalog cannot resolve** - GitHub-only mods, hand-built things - are listed by name rather than quietly dropped, so you know what to go and fetch yourself.
- **A locked version that has since been withdrawn** asks rather than failing: it offers the nearest version and lets you untick anything you would rather skip.

## Importing a list from sp-mod
sp-mod has Mod Lists of its own, and **New list → Import from sp-mod** brings one in by its address. Paste the link - a private list's share link works too - press **Read list**, and see everything on it before anything is saved. **Find lists on sp-mod** opens the site's lists page, where you can search and filter by SPT version; copy a list's address, come back, and the box fills itself.

The review sorts the list into what each entry is, with a tick on every row:

- **Parent mods missing** - an addon on the list whose mod isn't. An addon does nothing without its mod, so these are ticked for you.
- **Mods** and **Addons** - what the list holds, at the version its page shows.
- **Dependencies to add** - mods the list needs but doesn't name, with what needs each one. One with no version for the SPT is shown but can't be added. When the mods that need one disagree on its version, it says which wants what.
- **No version for your SPT** - see below.

A list on sp-mod doesn't store versions: its page shows each mod's newest version for the list's SPT, or the newest outright when it names none. If that isn't the SPT you run, switch on **Use versions for SPT x** and every mod moves to its newest version for yours, each addon to the newest that fits its mod. Anything with nothing published for your SPT goes under **No version for your SPT**, unticked.

**Create list** stores it as an imported list - read-only, like any list you didn't write - and applying it only adds: it installs and enables what it names and sets nothing aside. **Refresh from sp-mod** (in **⋯**) reads the page again and shows what was added, removed or changed before updating, keeping the rows you unticked last time; importing the same list again does the same. **Open on sp-mod** opens its page. sp-mod's page doesn't say which machines a mod is for, so an imported list's entries start as **Not checked yet**. Once you apply it, each one takes the scope its installed files show, the same way a captured list's do, and a refresh keeps it.

## Who each mod is for
Every entry names the machines it is for. It is worked out for you at capture time from where the mod's files actually live, and shown on every row. Pick a different one from the **scope dropdown** on the right of the row, and the filter above the list matches one exactly - which answers the question you have when tidying a list: what have I already pruned, and what is still carrying the capture default?

| Scope | Who gets it |
| --- | --- |
| Server + Client + Headless | Everything. A mod with both halves, by default |
| Server + Client | The server and the players; not the headless |
| Client + Headless | Every machine running the game. A plugin, by default |
| Client only | Players only - HUD tweaks, sound packs, anything drawn at a person |
| Headless only | The headless box alone |
| Server only | The server's own mods - `fika-server` and friends |

What is in the name gets it; what is not, does not. Nothing on disk separates a bot overhaul from a HUD widget - both are a DLL in `BepInEx\plugins` - so a capture gives every plugin to the headless and you take away what it does not need. That direction is deliberate: a headless carrying a spare mod costs nothing anyone can see, while one missing an item or bot mod is felt by everybody in the raid it is hosting.

### Headless advice
Many mod authors say on their sp-mod page whether a mod belongs on a Fika headless - VRAM Cleaner's install steps say *Don't install onto a fika headless client*. The page reads each mod's description and shows what it says, quoting the author:

- **Keep off headless** - the page says not to put it on a headless, and this entry still sends it there. **Make it Client only** (or **Server + Client** for a mod with a server half) beside the scope does what the page says.
- **Headless needs it** - a badge when the page says the headless needs it. If you've taken the headless off that entry, it shows as a warning with a button to put it back.
- Anything else the page says about a headless is in the row's tooltip.
- **Keep** beside a Keep off headless hint hides it for that list on this PC, if you want the headless to have the mod anyway. It comes back if the mod's page changes its advice.
- An addon uses its own sp-mod page first, and its mod's page when its own says nothing about a headless; the tooltip says which.

Nothing changes until you press the button and save. The **Headless advice** entry in the scope filter shows just the rows with something to act on, and a capture tells you how many there are. Descriptions are read once and remembered, and read again when the mod's page changes. On a list you can't edit, the advice shows without the buttons.

A **headless still takes Server only entries**. It is a full SPT install, and a server-only entry is a whole mod rather than half of one. **Server + Client** is how you say the server and the players need this and the headless does not.

> [!NOTE]
> Scope only ever narrows a list a server served you. **Your own lists describe your own install and apply whole** - which matters if you host and play on the same machine, see below.

## If you run the server
Publishing needs the Server Map mod on the server - see [Server map](Server-map). The steps here are the mod list half.

1. **Capture the list on the machine the server runs on**, so the server's own mods are in it and scoped `Server only` for you.
2. **Prune it for the people receiving it.** Take the headless off anything it does not need; leave the players everything they do.
3. **Publish to this server** (in the list's **⋯** menu) writes it where the server serves it from. The list gets a blue **Serving** badge so you can see which of a dozen personal lists is the one going out.
4. **Edit, Save, and publish again** to hand out a new version. The revision moves only when what you published actually changed, so republishing an unchanged list is a no-op and the number still means something.

On the joining side, a served list is stored read-only and marked as coming from that server. **Refresh from server** (in **⋯**) asks for it again whether or not the revision has moved - for a copy you doubt - and names what came back. It installs nothing; applying is still a separate step.

> [!WARNING]
> **Do not apply your published client list on the machine that hosts.** A list of your own applies whole - every entry, whatever its scope - so one pruned down to what players need names none of your server's mods, and applying it there sets aside `fika-server`, SVM and the Server Map mod itself, which is the thing publishing the list. Nothing is deleted and **Undo** puts it straight back, but your server stops serving until you do.

The shape that works is **two lists on the server box**: the full one you apply there, and the pruned one you publish. They are cheap - a list is a manifest, not a copy of anything.

## Following a server and your own list at once
Both can be active, and each shows a green **Following** badge. Following a server does not cost you the personal list you were already following, and applying a list of your own will not sweep away what the server requires - the mods it asks for are spared.

A list a server hands you **never disables anything**, whatever its author chose. An operator writing a list is describing their install; applying that verbatim on your machine would set aside mods the server has never heard of and has no opinion about.
