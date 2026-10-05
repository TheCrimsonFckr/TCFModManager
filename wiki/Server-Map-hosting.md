**If you run the server** - how do I...

- [Get the Server Map mod?](#get-the-server-map-mod)
- [Install the Server Map mod?](#install-the-server-map-mod)
- [Where exactly does the payload folder go?](#where-exactly-does-the-payload-folder-go)
- [Know when it is loaded?](#know-when-it-is-loaded)
- [Find my server's key?](#find-my-servers-key)
- [Change my server's key?](#change-my-servers-key)
- [Publish a mod list to my server?](#publish-a-mod-list-to-my-server)
- [Stop a server mod being sent to my players?](#stop-a-server-mod-being-sent-to-my-players)
- [Serve a Fika headless only what it needs?](#serve-a-fika-headless-only-what-it-needs)
- [Update the list my server is serving?](#update-the-list-my-server-is-serving)
- [Let people connect from outside my network?](#let-people-connect-from-outside-my-network)
- [Keep it to my own network?](#keep-it-to-my-own-network)
- [See who is on my server?](#see-who-is-on-my-server)
- [Update the mod?](#update-the-mod)
- [Remove it?](#remove-it)

## Get the Server Map mod?

It is **not bundled with TCF Mod Manager** it is a separate download, published as an addon on the
app's mod page at sp-mod.com. That is deliberate: it is installed on a different machine from the
app, by a different person, and a player joining your server needs none of it.

In the app: switch the Server map page on (**Options** → **Pages** → **Server map**), then
**Options** → **Fika and servers** → **Server map connection** → **Get the Server Map mod**. That opens the page in your
browser.

**There is one zip per SPT line** - `...-SPT4.1.zip` and `...-SPT4.0.13.zip`. Take the one that
matches your server. The payload inside is the same build either way; only the stub differs.

If you take the wrong one, SPT refuses to load the stub and says so - it will not half-work.

## Install the Server Map mod?

The zip is laid out exactly as it installs, so installing it is one extraction.

1. **Stop the server.** The payload DLL is locked while it runs, and a copy over a locked file
   fails silently you will then spend an hour debugging the previous build.
2. **Extract the zip into your SPT root** - the folder that holds `SPT_Runtime\` (4.1) or `SPT\`
   (4.0), and `TCFModManager\`. The **stub** lands in `user\mods\TCFMM.ServerMap\`, where SPT
   looks for mods; the **payload** lands in `TCFModManager\ServerMap\payload\`, outside it, so
   removing the feature later is one folder deletion.
3. **Start the server.**

## Where exactly does the payload folder go?

The stub starts in its own folder and walks **up** the tree looking for
`TCFModManager\ServerMap\payload\`, so it does not care which SPT layout you have. Two real
examples:

```
SPT 4.0.13
  \SPT\user\mods\TCFMM.ServerMap\   <- stub
  \TCFModManager\ServerMap\payload\ <- payload
  \TCFModManager\Data\ServerMap\    <- key and published list

SPT 4.1.x
  \SPT_Runtime\user\mods\TCFMM.ServerMap\ <- stub
  \TCFModManager\ServerMap\payload\       <- payload
  \TCFModManager\Data\ServerMap\          <- key and published list
```

If TCF Mod Manager is already installed on that machine, it lives in that same `TCFModManager`
folder so `ServerMap\payload\` goes right next to the app.

**`Data\` is the folder to keep.** Your key and your published list live there, with the app's
settings and install history. Replacing `TCFModManager\` wholesale when you deploy takes all of it
with you lay a new build over the old one instead, the way the app's own updater does.

## Know when it is loaded?

Watch the server's console on startup:

```
[TCFMM ServerMap] Ready - serving /tcfservermap from <path>
```

That is the only line you need. Two others tell you what went wrong:

- `No payload found, so this mod does nothing. Looked for TCFMM.ServerMap.Payload.dll in: …` the
  stub loaded but cannot find the payload. The message lists every folder it checked; put the
  payload in one of them. **The server starts normally**, which is the point: deleting the payload
  folder is the supported way to switch the feature off.
- `Found <path> but could not load it: …` the payload is there but broken. If it complains about
  casting `IServerMapPayload` to `IServerMapPayload`, there is a stray copy of
  `TCFMM.ServerMap.Shared.dll` in the payload folder. There should not be one; delete it.

## Find my server's key?

Every route except the handshake needs a shared key, so a stranger who finds your port cannot read
your list. The server generates one **on startup** before anyone connects, because it is the
thing you send out with the address.

It is in a text file in the app's data folder:

```
<...>\TCFModManager\Data\ServerMap\servermap-key.txt
```

24 characters in six groups of four. Send it to your players along with the address and port. It is
compared with dashes, spaces and case ignored, so it does not matter how they paste it.

The key is generated **once**, the first time the server starts without one. After that it is left
alone restarting the server does not change it, and neither does updating the mod. The file is the
key: whatever is in it is what the server expects.

**If TCF Mod Manager is on the same machine as the server**, you never need to open this file: the
app reads it and fills the key in on its own. See "How do I fill in my own server's details".

## Change my server's key?

**Options** → **Server map connection** → **Generate a new key**. It only appears on the machine actually
running the server.

Everyone holding the old key stops being able to see what your server publishes the moment you press
it, so only do this if a key has gone somewhere it should not have and send the new one out
afterwards.

The server picks the change up on its own. **It does not need restarting.**

## Publish a mod list to my server?

1. Get the server's install the way you want it, then on **Mod lists** press **Capture** to make a
   list of what is there. (Or pick an existing list.)
2. Check the scopes see the next question. This is the step people skip and regret.
3. With the list selected, press **Publish to this server**.

That writes the list into `TCFModManager\Data\ServerMap\` on this machine. Nothing is installed,
enabled or disabled; no files on the game folder change.

The list is re-read whenever its file changes, so **publishing again takes effect without
restarting the server**.

The published list gets a red **Serving** badge on the Mod lists page, so among a dozen personal
lists you can see which one other people are being handed.

> [!WARNING]
> **Do not apply your published list on the machine that hosts.** Scope decides what a machine takes
> from a list a *server* served it; a list of your own applies whole, every entry whatever its scope.
> So a list pruned down to what players need names none of your server's mods, and applying it on the
> server box sets aside `fika-server`, SVM and the Server Map mod itself which is the thing serving the
> list. Nothing is deleted and **Undo** puts it straight back, but your server stops publishing until
> you do.

Keep **two lists on the server box**: the full one you apply there, and the pruned one you publish.
A list is a manifest rather than a copy of anything, so a second one costs nothing.

## Stop a server mod being sent to my players?

Mark it **Server only**.

The problem: your server needs `fika-server`, the Server Map mod itself, and anything else living
in `user\mods`. None of those are on The Forge, and none of them belong on a player's machine. But
if you leave them off your list entirely, applying your own list on the server switches them off.

So every entry on a list has a **scope**, named after the machines that get the mod. What is in the
name gets it; what is not, does not.

| Scope | Who installs it |
|---|---|
| **Server + Client + Headless** | everything. A mod with both halves, by default |
| **Server + Client** | the server and the players, but not the headless |
| **Client + Headless** | every machine running the game. A plugin, by default |
| **Client only** | players only. HUD tweaks, sound packs, anything drawn at a person |
| **Headless only** | the headless box alone |
| **Server only** | the server's own mods. `fika-server`, the Server Map mod, SVM |

A **Server only** entry is not fetched by a player, not counted as missing, and never appears in
their "you are behind" list.

Scope is worked out for you when you capture a list, from where the mod's files actually live. To
change one: select the list, find the row, and press the **scope button** to cycle it. The full set
leads into **Client + Headless** first, because taking a HUD mod off the headless is the edit
anybody actually makes. Then **Save**, then **Publish to this server** again.

The **scope filter** above the list matches one exactly, which is the question you have while tidying
one: what have I already pruned, and what is still carrying the capture default?

## Serve a Fika headless only what it needs?

A headless runs the game but nobody sits at it. It needs the mods that decide how a raid goes - bots,
items, weapons, locations - and has no use for the ones that draw things at a player. Served a
player's list it installs a pile of HUD tweaks and sound packs it will never show anyone.

One list still covers everybody. The machine reading it takes the entries scoped to it.

- **Capture gives every plugin to the headless** (`Client + Headless`) and you take away what it does
  not need. Nothing on disk separates a bot overhaul from a HUD widget, so the guess goes the safe
  way: a headless carrying a spare mod costs nothing anyone can see, while one missing an item or bot
  mod is felt by everybody in the raid it is hosting.
- **A headless still takes Server only entries.** It is a full SPT install, and a server-only entry
  is a whole mod rather than half of one. **Server + Client** is how you say the server and the
  players need this and the headless does not.
- **The headless machine has to know what it is.** On that machine: **Options** → **What this
  machine is**. The app asks by itself when you set the install folder and it finds
  `FikaHeadlessManager.exe`; until somebody answers, the machine is treated as an ordinary player,
  which installs more rather than less.

Lists written before this keep meaning what they meant: **Client** used to mean "not the server",
because the server and a player were the only two machines a list could describe, so an entry written
then is read as **Client + Headless** now.

## Update the list my server is serving?

Edit the list, **Save** it, then **Publish to this server** again. That is all the server picks
up the new file on its own.

**The list you publish stays yours to edit.** A copy of it coming back off your own server never
replaces the original, so hosting and playing on one machine does not turn your own list read-only.

**Publishing moves the revision when what you published has changed.** That number is what tells
connected players their copy is stale: they see "the server is publishing revision 4; you have
revision 3" on their Play page. Republishing an unchanged list changes nothing, so the number still
means something when it does move.

If somebody's copy looks stale anyway, they can press **Refresh from server** on the list itself,
which asks again whether or not the revision has moved.

## Let people connect from outside my network?

Server Map uses the SPT server's own port there is no second listener so if people can already
join your server, they can already reach it.

If you are setting that up from scratch, in `SPT_Data/configs/http.json`:

- `ip` → `0.0.0.0`
- `backendIp` → your machine's LAN address
- allow TCP **6969** inbound, and forward it if you want people outside your network

Then give people your external address, the port, and the key.

## Keep it to my own network?

If only people at home - or on your Tailscale - ever play, turn on **Only answer this network** in
**Options - Server map connection** on the server machine. Needs the Server Map mod **0.2.0** or
later, stub included.

From then on the server refuses every Server Map request from outside its own network, even the
handshake, and the player sees *This server only answers machines on its own network*. It takes
effect straight away; no restart.

What counts as your network:

- the server machine itself
- the private ranges a router hands out - `192.168.x.x`, `10.x.x.x`, `172.16-31.x.x`
- **Tailscale** peers (`100.64.0.0/10`), so a group on Tailscale still gets in. ZeroTier uses
  private ranges already.

It only covers Server Map. Whether people can join the SPT server itself is still `http.json` and
your router.

The switch writes `TCFModManager\Data\ServerMap\servermap.json`; `{ "lanOnly": true }` there by
hand does the same.

## See who is on my server?

The **Server map** page on any machine connected to your server - yours included - lists every
machine that reports to it, with your server first. Needs the Server Map mod **0.2.0** or later.

Your server's own row comes from **TCF Mod Manager on the server machine**: connect it to your own
server and press **Share** like anyone else. The mod never scans your install - the row shows what
the app installed and keeps a record of.

Each machine is asked once before it reports anything, so a player who says no simply is not on the
map. Machines that do not run TCF Mod Manager never appear.

The list lives in `TCFModManager\Data\ServerMap\clients.json`. A machine not heard from for 30 days
is forgotten on its own.

## Update the mod?

**TCF Mod Manager tells you when to.** Its **App update** page has a **Server Map mod** card on the
machine that runs the server: the version installed, the newest on sp-mod.com, and a warning - with
a coloured dot on the sidebar item - when a newer one is out. It also warns when the stub in
`user\mods` is older than the payload, which is easy to miss and stops LAN-only letting anyone in.
Players connected to your server see the same card for your server's version, telling them to ask
you.

1. **Stop the server.**
2. Extract the new zip for your SPT line into your SPT root, over the old files.
3. Start the server.

**Leave `TCFModManager\Data\ServerMap\` alone.** Your key, your published list and the map's
`clients.json` are there, and none of them are in the zip.

The app never updates the mod itself - it is installed by hand, on the server machine - so the card
only says it is behind and links the mod's page.

## Remove it?

Delete the `TCFModManager\ServerMap\payload\` folder. The server starts normally, the stub logs one
line and does nothing. Delete `user\mods\TCFMM.ServerMap\` too if you want it gone entirely.
`TCFModManager\Data\ServerMap\` holds your key, your list and the map - delete it as well if you
will not be putting the mod back.
